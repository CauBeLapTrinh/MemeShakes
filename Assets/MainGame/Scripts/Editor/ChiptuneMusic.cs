#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Catshakes.EditorTools
{
    /// <summary>
    /// Composes the gameplay background music in code: a cheerful 8-bit loop (F major, 112 BPM, 16 bars, about 34 s)
    /// with a triangle bass, a quiet pulse-wave arpeggio, a square-wave lead and soft drums. It is light on purpose so it
    /// stays out of the way while the player thinks. The first 8 bars have the tune in the middle register; bars 9-16
    /// repeat it an octave up with busier hi-hats. Nothing rings past the last bar, so the loop is seamless.
    /// Rendered once by  Catshakes/Setup Sound And Effect Assets  into Assets/MainGame/Sounds/BGM_Gameplay.wav.
    /// </summary>
    public static class ChiptuneMusic
    {
        private const float Bpm = 112f;
        private const int StepsPerBar = 8; // eighth notes
        private const int Rest = -1;

        // Chords of the 8-bar progression: F  Dm  Bb  C | F  Am  Bb  C (MIDI notes).
        private static readonly int[][] Chords =
        {
            new[] { 65, 69, 72 }, new[] { 62, 65, 69 }, new[] { 58, 62, 65 }, new[] { 60, 64, 67 },
            new[] { 65, 69, 72 }, new[] { 57, 60, 64 }, new[] { 58, 62, 65 }, new[] { 60, 64, 67 }
        };

        private static readonly int[] BassRoots = { 41, 38, 46, 36, 41, 45, 46, 36 };

        // The tune, one row per bar, eighth notes.
        private static readonly int[][] Melody =
        {
            new[] { 72, Rest, 69, 72, 77, Rest, 76, 74 },
            new[] { 74, Rest, 69, Rest, 65, 67, 69, Rest },
            new[] { 70, Rest, 74, 70, 77, Rest, 74, Rest },
            new[] { 76, 74, 72, Rest, 67, Rest, Rest, Rest },
            new[] { 72, Rest, 69, 72, 77, Rest, 79, 77 },
            new[] { 76, Rest, 72, Rest, 69, 72, 76, Rest },
            new[] { 74, 72, 70, 74, 77, Rest, 74, 72 },
            new[] { 76, Rest, 74, Rest, 72, Rest, Rest, Rest }
        };

        /// <summary>Renders the whole loop as mono samples in -1..1.</summary>
        public static float[] RenderGameplayLoop(int sampleRate)
        {
            float stepSeconds = 60f / Bpm / 2f;
            int stepSamples = Mathf.RoundToInt(stepSeconds * sampleRate);
            const int bars = 16;
            var buffer = new float[stepSamples * StepsPerBar * bars];
            var random = new System.Random(112);

            for (int bar = 0; bar < bars; bar++)
            {
                int phrase = bar % 8;
                bool secondPass = bar >= 8;
                int barStart = bar * StepsPerBar * stepSamples;

                // Bass: root, octave, fifth, octave on eighths.
                int root = BassRoots[phrase];
                int[] bassPattern = { 0, 12, 7, 12, 0, 12, 7, 12 };
                for (int step = 0; step < StepsPerBar; step++)
                {
                    AddNote(buffer, sampleRate, barStart + step * stepSamples, stepSeconds * 0.9f, root + bassPattern[step], 0.2f, Wave.Triangle, 0.004f, 0.06f);
                }

                // Arpeggio: chord tones going up on sixteenths, an octave higher in the second pass.
                int[] chord = Chords[phrase];
                int sixteenth = stepSamples / 2;
                for (int i = 0; i < StepsPerBar * 2; i++)
                {
                    int note = chord[i % chord.Length] + (i % 6 >= 3 ? 12 : 0) + (secondPass ? 12 : 0);
                    AddNote(buffer, sampleRate, barStart + i * sixteenth, stepSeconds * 0.4f, note, secondPass ? 0.045f : 0.055f, Wave.Pulse25, 0.002f, 0.03f);
                }

                // Lead: a note lasts until the next one (rests extend it a little).
                int[] tune = Melody[phrase];
                for (int step = 0; step < StepsPerBar; step++)
                {
                    if (tune[step] == Rest)
                    {
                        continue;
                    }

                    int length = 1;
                    while (step + length < StepsPerBar && tune[step + length] == Rest && length < 2)
                    {
                        length++;
                    }

                    int note = tune[step] + (secondPass ? 12 : 0);
                    AddNote(buffer, sampleRate, barStart + step * stepSamples, stepSeconds * length * 0.92f, note, secondPass ? 0.1f : 0.13f, Wave.Square, 0.004f, 0.04f, true);
                }

                // Drums: kick on 1 and 3 (and a pickup), snare on 2 and 4, hi-hats on eighths (sixteenths later on).
                bool fillBar = phrase == 7;
                for (int step = 0; step < StepsPerBar; step++)
                {
                    int at = barStart + step * stepSamples;
                    if (step == 0 || step == 4 || step == 3)
                    {
                        AddKick(buffer, sampleRate, at, step == 3 ? 0.22f : 0.34f);
                    }

                    if (step == 2 || step == 6 || (fillBar && step == 7))
                    {
                        AddSnare(buffer, sampleRate, at, random, 0.13f);
                    }

                    if (fillBar && step == 7)
                    {
                        AddSnare(buffer, sampleRate, at + sixteenth, random, 0.1f);
                    }

                    AddHat(buffer, sampleRate, at, random, step % 2 == 0 ? 0.035f : 0.025f);
                    if (secondPass)
                    {
                        AddHat(buffer, sampleRate, at + sixteenth, random, 0.018f);
                    }
                }
            }

            Normalize(buffer, 0.85f);
            return buffer;
        }

        // ---------------------------------------------------------------- instruments

        private enum Wave
        {
            Square,
            Pulse25,
            Triangle
        }

        private static void AddNote(float[] buffer, int sampleRate, int start, float duration, int midi, float volume, Wave wave, float attack, float release, bool vibrato = false)
        {
            float frequency = 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            int count = Mathf.CeilToInt((duration + release) * sampleRate);
            double phase = 0d;
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                float t = i / (float)sampleRate;
                float wobble = vibrato && t > 0.12f ? 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 5.5f * t) : 1f;
                phase += frequency * wobble / sampleRate;
                float cycle = (float)(phase - Math.Floor(phase));

                float sample;
                switch (wave)
                {
                    case Wave.Triangle: sample = 4f * Mathf.Abs(cycle - 0.5f) - 1f; break;
                    case Wave.Pulse25: sample = cycle < 0.25f ? 1f : -1f; break;
                    default: sample = cycle < 0.5f ? 1f : -1f; break;
                }

                float envelope = Mathf.Min(t / attack, 1f);
                // A little decay to a sustain level, then a release after the note ends.
                envelope *= 0.7f + 0.3f * Mathf.Exp(-t * 12f);
                if (t > duration)
                {
                    envelope *= Mathf.Clamp01(1f - (t - duration) / release);
                }

                buffer[start + i] += volume * envelope * sample;
            }
        }

        private static void AddKick(float[] buffer, int sampleRate, int start, float volume)
        {
            const float duration = 0.22f;
            int count = Mathf.CeilToInt(duration * sampleRate);
            double phase = 0d;
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                float t = i / (float)sampleRate;
                float frequency = Mathf.Lerp(130f, 45f, Mathf.Clamp01(t / 0.1f));
                phase += frequency / sampleRate;
                buffer[start + i] += volume * Mathf.Exp(-t * 18f) * Mathf.Sin((float)(2d * Math.PI * phase));
            }
        }

        private static void AddSnare(float[] buffer, int sampleRate, int start, System.Random random, float volume)
        {
            const float duration = 0.18f;
            int count = Mathf.CeilToInt(duration * sampleRate);
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                float t = i / (float)sampleRate;
                float noise = (float)(random.NextDouble() * 2d - 1d);
                float body = Mathf.Sin(2f * Mathf.PI * 185f * t) * Mathf.Exp(-t * 35f);
                buffer[start + i] += volume * (noise * Mathf.Exp(-t * 20f) + 0.6f * body);
            }
        }

        private static void AddHat(float[] buffer, int sampleRate, int start, System.Random random, float volume)
        {
            const float duration = 0.045f;
            int count = Mathf.CeilToInt(duration * sampleRate);
            float previous = 0f;
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                float t = i / (float)sampleRate;
                float noise = (float)(random.NextDouble() * 2d - 1d);
                // Difference of two noise samples: a crude high-pass, so it sounds like a tick, not a hiss.
                float bright = noise - previous;
                previous = noise;
                buffer[start + i] += volume * bright * Mathf.Exp(-t * 90f);
            }
        }

        private static void Normalize(float[] buffer, float peak)
        {
            float max = 0f;
            foreach (float sample in buffer)
            {
                max = Mathf.Max(max, Mathf.Abs(sample));
            }

            if (max <= 0f)
            {
                return;
            }

            float gain = peak / max;
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= gain;
            }
        }
    }
}
#endif
