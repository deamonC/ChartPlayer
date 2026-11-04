using System;
using System.Collections.Generic;
using UILayout;
using SongFormat;

namespace ChartPlayer
{
    /// <summary>
    /// Scrolling 2D tablature view (Guitar Pro style) synchronized with the song.
    /// High string on top, fret numbers on the string lines, playhead fixed near the left.
    /// </summary>
    public class TabDisplay : UIElement
    {
        public SongPlayer SongPlayer { get; set; }
        public int NumStrings { get; set; } = 6;
        public float DisplaySeconds { get; set; } = 6;

        const float PlayheadFraction = 0.2f;
        const float MarkerScale = 0.7f;
        const int MaxFret = 30;

        UIFont font;
        float rowHeight;
        string[] fretStrings;
        float[] lastDrawnTime = new float[8];
        int lastChordID = -1;

        UIColor backgroundColor = new UIColor(0, 0, 0, 180);
        UIColor lineColor = new UIColor(140, 140, 140, 255);
        UIColor measureColor = new UIColor(200, 200, 200, 255);
        UIColor beatColor = new UIColor(80, 80, 80, 255);
        UIColor playheadColor = new UIColor(255, 220, 0, 255);
        UIColor futureColor = UIColor.White;
        UIColor currentColor = new UIColor(255, 220, 0, 255);
        UIColor pastColor = new UIColor(120, 120, 120, 255);
        UIColor markerColor = new UIColor(255, 170, 60, 255);
        UIColor sustainColor = new UIColor(255, 255, 255, 110);

        public TabDisplay()
        {
            font = ChartPlayerGame.Instance.GetFont("MainFont");
            rowHeight = font.TextHeight * 0.9f;

            fretStrings = new string[MaxFret + 1];
            for (int i = 0; i <= MaxFret; i++)
                fretStrings[i] = i.ToString();
        }

        protected override void GetContentSize(out float width, out float height)
        {
            width = 10;
            // One label row on top (chord names / palm mute) + one row per string
            height = rowHeight * (NumStrings + 1);
        }

        float StringY(int str)
        {
            // String 0 is the lowest string - draw high string on top, like standard tab
            int row = (NumStrings - 1) - str;

            return ContentBounds.Y + (rowHeight * (row + 1.5f));
        }

        float TimeToX(float time, float currentTime, float pixelsPerSecond)
        {
            return ContentBounds.X + (ContentBounds.Width * PlayheadFraction) + ((time - currentTime) * pixelsPerSecond);
        }

        protected override void DrawContents()
        {
            base.DrawContents();

            if ((SongPlayer == null) || (SongPlayer.SongInstrumentNotes == null))
                return;

            GraphicsContext2D context = Layout.Current.GraphicsContext;

            float currentTime = SongPlayer.CurrentSecond;
            float pixelsPerSecond = ContentBounds.Width / Math.Max(DisplaySeconds, 1);
            float startTime = currentTime - (DisplaySeconds * PlayheadFraction);
            float endTime = currentTime + (DisplaySeconds * (1 - PlayheadFraction));

            float top = StringY(NumStrings - 1);
            float bottom = StringY(0);

            context.DrawRectangle(new RectF(ContentBounds.X, ContentBounds.Y, ContentBounds.Width, ContentBounds.Height), backgroundColor);

            // Beats and measure lines
            if (SongPlayer.SongStructure != null)
            {
                foreach (SongBeat beat in SongPlayer.SongStructure.Beats)
                {
                    if (beat.TimeOffset < startTime)
                        continue;

                    if (beat.TimeOffset > endTime)
                        break;

                    float x = TimeToX(beat.TimeOffset, currentTime, pixelsPerSecond);

                    if (beat.IsMeasure)
                        context.DrawRectangle(new RectF(x, top, 2, bottom - top), measureColor);
                    else
                        context.DrawRectangle(new RectF(x, top, 1, bottom - top), beatColor);
                }
            }

            // String lines
            for (int str = 0; str < NumStrings; str++)
            {
                context.DrawRectangle(new RectF(ContentBounds.X, StringY(str), ContentBounds.Width, 1), lineColor);
            }

            List<SongNote> notes = SongPlayer.SongInstrumentNotes.Notes;

            for (int i = 0; i < lastDrawnTime.Length; i++)
                lastDrawnTime[i] = float.MinValue;

            lastChordID = -1;

            // Start a bit earlier so that sustains from older notes are still drawn
            int pos = FindFirstNote(notes, startTime - 10);

            for (; pos < notes.Count; pos++)
            {
                SongNote note = notes[pos];

                if (note.TimeOffset > endTime)
                    break;

                if ((note.EndTime < startTime) && (note.TimeOffset < startTime))
                {
                    if (note.ChordID != -1)
                        lastChordID = note.ChordID;

                    continue;
                }

                UIColor color = GetNoteColor(note, currentTime);
                float x = TimeToX(note.TimeOffset, currentTime, pixelsPerSecond);

                if (note.Techniques.HasFlag(ESongNoteTechnique.Chord) && !note.Techniques.HasFlag(ESongNoteTechnique.ChordNote) && (note.ChordID != -1))
                {
                    SongChord chord = SongPlayer.SongInstrumentNotes.Chords[note.ChordID];

                    for (int str = 0; (str < chord.Frets.Count) && (str < NumStrings); str++)
                    {
                        if (chord.Frets[str] < 0)
                            continue;

                        DrawFret(context, str, chord.Frets[str], note, x, color, currentTime, pixelsPerSecond);
                    }

                    if ((note.ChordID != lastChordID) && !String.IsNullOrEmpty(chord.Name) && (note.TimeOffset >= startTime))
                    {
                        context.DrawText(chord.Name, font, x, ContentBounds.Y, markerColor, MarkerScale);
                    }

                    lastChordID = note.ChordID;
                }
                else if ((note.String >= 0) && (note.String < NumStrings))
                {
                    DrawFret(context, note.String, note.Fret, note, x, color, currentTime, pixelsPerSecond);
                }

                if (note.Techniques.HasFlag(ESongNoteTechnique.PalmMute) && (note.TimeOffset >= startTime))
                {
                    context.DrawText("PM", font, x, ContentBounds.Y + (rowHeight * 0.45f), markerColor, MarkerScale * 0.8f);
                }
            }

            // Playhead
            float playX = TimeToX(currentTime, currentTime, pixelsPerSecond);
            context.DrawRectangle(new RectF(playX - 1, ContentBounds.Y, 3, ContentBounds.Height), playheadColor);
        }

        void DrawFret(GraphicsContext2D context, int str, int fret, in SongNote note, float x, in UIColor color, float currentTime, float pixelsPerSecond)
        {
            // Avoid drawing the same string twice at the same time (chord + chord notes)
            if (lastDrawnTime[str] == note.TimeOffset)
                return;

            lastDrawnTime[str] = note.TimeOffset;

            float y = StringY(str);

            string text;

            if (note.Techniques.HasFlag(ESongNoteTechnique.FretHandMute) || (fret < 0))
                text = "x";
            else
                text = fretStrings[Math.Min(fret, MaxFret)];

            float textWidth;
            float textHeight;

            font.MeasureString(text, out textWidth, out textHeight);

            // Sustain line
            if (note.TimeLength > 0.15f)
            {
                float endX = TimeToX(note.EndTime, currentTime, pixelsPerSecond);

                context.DrawRectangle(new RectF(x + textWidth, y - 2, Math.Max(endX - (x + textWidth), 0), 5), sustainColor);
            }

            // Background box "cuts" the string line, like printed tab
            context.DrawRectangle(new RectF(x - 2, y - (rowHeight * 0.45f), textWidth + 4, rowHeight * 0.9f), new UIColor(0, 0, 0, 255));

            context.DrawText(text, font, x, y - (font.TextHeight / 2), color);

            string marker = GetTechniqueMarker(note);

            if (marker != null)
            {
                context.DrawText(marker, font, x + textWidth + 2, y - (font.TextHeight * MarkerScale / 2), markerColor, MarkerScale);
            }
        }

        static string GetTechniqueMarker(in SongNote note)
        {
            ESongNoteTechnique t = note.Techniques;

            if (t.HasFlag(ESongNoteTechnique.HammerOn)) return "h";
            if (t.HasFlag(ESongNoteTechnique.PullOff)) return "p";
            if (t.HasFlag(ESongNoteTechnique.Slide)) return (note.SlideFret > note.Fret) ? "/" : "\\";
            if (t.HasFlag(ESongNoteTechnique.Bend)) return "b";
            if (t.HasFlag(ESongNoteTechnique.Vibrato)) return "~";
            if (t.HasFlag(ESongNoteTechnique.Tremolo)) return "tr";
            if (t.HasFlag(ESongNoteTechnique.PinchHarmonic)) return "PH";
            if (t.HasFlag(ESongNoteTechnique.Harmonic)) return "<>";
            if (t.HasFlag(ESongNoteTechnique.Tap)) return "T";

            return null;
        }

        UIColor GetNoteColor(in SongNote note, float currentTime)
        {
            if (note.TimeOffset > currentTime)
                return futureColor;

            if (currentTime <= Math.Max(note.EndTime, note.TimeOffset + 0.15f))
                return currentColor;

            return pastColor;
        }

        static int FindFirstNote(List<SongNote> notes, float time)
        {
            int low = 0;
            int high = notes.Count;

            while (low < high)
            {
                int mid = (low + high) / 2;

                if (notes[mid].TimeOffset < time)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }
    }
}
