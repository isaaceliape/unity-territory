// Synthesized sound ported from territory/public/sfx.js: the same oscillator
// recipes and the same 32-step theme, rendered to PCM floats with no audio
// files. Unity plays the buffers through an AudioClip; `dotnet test` renders
// them here to prove they are non-silent and correctly enveloped.
using System;
using System.Collections.Generic;

namespace Territory.Client
{
    public enum WaveType { Sine, Square, Saw, Triangle }

    public sealed class Note
    {
        public double Freq, Start, Length, Gain;
        public WaveType Type;
        public double SlideTo; // 0 means none
        public bool IsNoise;
        public double NoiseFrom = 1800, NoiseTo = 200;
        public bool IsMusic;
    }

    public sealed class SfxSynth
    {
        public const int SampleRate = 22050;
        private const double MasterVolume = 0.25;
        private const double MusicVolume = 0.5;

        private readonly Func<double> _random;
        private readonly List<Note> _notes = new List<Note>();
        private bool _muted;
        private bool _musicMuted;

        // Theme state, mirroring the sfx.js scheduler.
        public const double MusicBpm = 116;
        public static readonly double MusicStep = 60 / MusicBpm / 2;
        public static readonly double[] MusicLead = {
            440, 0, 523.25, 0, 659.25, 0, 523.25, 0,
            440, 0, 392, 0, 349.23, 0, 329.63, 0,
            523.25, 0, 659.25, 0, 587.33, 0, 523.25, 0,
            587.33, 0, 493.88, 0, 392, 0, 493.88, 0,
        };
        public static readonly double[] MusicBass = {
            110, 0, 0, 0, 110, 0, 0, 0,
            87.31, 0, 0, 0, 87.31, 0, 0, 0,
            130.81, 0, 0, 0, 130.81, 0, 0, 0,
            98, 0, 0, 0, 98, 0, 0, 0,
        };

        private bool _playing;
        private double _nextTime;
        private int _stepIdx;

        public SfxSynth(Func<double> random = null)
        {
            _random = random ?? new Func<double>(() => new Random().NextDouble());
        }

        public bool IsMuted => _muted;
        public bool IsMusicMuted => _musicMuted;
        public bool IsMusicPlaying => _playing;
        public int NoteCount => _notes.Count;

        public void SetMuted(bool value) => _muted = value;
        public void SetMusicMuted(bool value) => _musicMuted = value;

        private void Tone(double freq, double length, WaveType type, double gain,
            double slideTo = 0, double delay = 0, bool music = false)
        {
            _notes.Add(new Note
            {
                Freq = freq, Start = delay, Length = length, Type = type,
                Gain = gain, SlideTo = slideTo, IsMusic = music,
            });
        }

        private void Noise(double length, double gain, double from = 1800, double to = 200, double delay = 0)
        {
            _notes.Add(new Note
            {
                Start = delay, Length = length, Gain = gain, IsNoise = true,
                NoiseFrom = from, NoiseTo = to,
            });
        }

        public void Capture(double size = 0.2, double loudness = 1)
        {
            double s = Math.Min(1, Math.Max(0, size));
            double bas = 392 * Math.Pow(2, s * 0.8);
            int count = size > 0.5 ? 4 : 3;
            double[] ratios = { 1, 1.26, 1.5, 2 };
            for (int i = 0; i < count; i += 1)
                Tone(bas * ratios[i], 0.16, WaveType.Triangle, 0.45 * loudness, 0, i * 0.065);
        }

        public void Death(double loudness = 1)
        {
            double level = Math.Min(1, Math.Max(0.2, loudness));
            Noise(0.4, 0.6 * level);
            Tone(220, 0.35, WaveType.Saw, 0.35 * level, 45);
        }

        public void TickSfx() => Tone(660, 0.07, WaveType.Square, 0.18);

        public void Go()
        {
            Tone(523, 0.4, WaveType.Saw, 0.22);
            Tone(659, 0.4, WaveType.Saw, 0.22);
            Tone(784, 0.4, WaveType.Saw, 0.22);
            Tone(1047, 0.5, WaveType.Triangle, 0.3, 0, 0.08);
        }

        public void Win()
        {
            double[] fs = { 523, 659, 784, 1047, 1319 };
            for (int i = 0; i < fs.Length; i += 1)
                Tone(fs[i], 0.28, WaveType.Triangle, 0.4, 0, i * 0.09);
        }

        public void Lose()
        {
            double[] fs = { 392, 349, 311, 262 };
            for (int i = 0; i < fs.Length; i += 1)
                Tone(fs[i], 0.3, WaveType.Triangle, 0.35, 0, i * 0.14);
        }

        // Theme scheduler: schedules ~0.6s ahead, like pump() in sfx.js.
        public void StartMusic()
        {
            if (_playing) return;
            _playing = true;
            _stepIdx = 0;
            _nextTime = 0.1;
        }

        public void StopMusic() => _playing = false;

        public void PumpMusic(double now)
        {
            if (!_playing || _muted || _musicMuted) return;
            while (_nextTime < now + 0.6)
            {
                PlayStep(_stepIdx, _nextTime);
                _nextTime += MusicStep;
                _stepIdx = (_stepIdx + 1) % MusicLead.Length;
            }
        }

        private void PlayStep(int i, double when)
        {
            if (MusicLead[i] != 0)
                _notes.Add(new Note { Freq = MusicLead[i], Start = when, Length = 0.22, Type = WaveType.Square, Gain = 0.09, IsMusic = true });
            if (MusicBass[i] != 0)
                _notes.Add(new Note { Freq = MusicBass[i], Start = when, Length = 0.24, Type = WaveType.Triangle, Gain = 0.2, IsMusic = true });
            if (i % 2 == 1)
                _notes.Add(new Note { Freq = 8000, Start = when, Length = 0.03, Type = WaveType.Sine, Gain = 0.035, IsMusic = true });
        }

        private static double Wave(WaveType type, double phase)
        {
            double s = Math.Sin(phase);
            switch (type)
            {
                case WaveType.Square: return s >= 0 ? 1 : -1;
                case WaveType.Saw: { double p = phase / (2 * Math.PI); return 2 * (p - Math.Floor(p)) - 1; }
                case WaveType.Triangle: { double p = phase / (2 * Math.PI); return 2 * Math.Abs(2 * (p - Math.Floor(p)) - 1) - 1; }
                default: return s;
            }
        }

        // Mixes everything scheduled in [0, seconds) to mono PCM.
        public float[] Render(double seconds)
        {
            int n = Math.Max(1, (int)(seconds * SampleRate));
            var out_ = new float[n];
            if (_muted) return out_;
            foreach (var note in _notes)
            {
                if (note.IsMusic && _musicMuted) continue;
                double bus = (note.IsMusic ? MasterVolume * MusicVolume : MasterVolume);
                int start = (int)(note.Start * SampleRate);
                int len = (int)(note.Length * SampleRate);
                if (start >= n) continue;
                if (note.IsNoise)
                {
                    double cutoff = note.NoiseFrom, step = (note.NoiseTo - note.NoiseFrom) / Math.Max(1, len);
                    double lp = 0;
                    for (int i = 0; i < len && start + i < n; i += 1)
                    {
                        cutoff += step;
                        double rc = 1.0 / (2 * Math.PI * Math.Max(40, cutoff));
                        double alpha = 1.0 / (1.0 + rc * SampleRate);
                        lp += alpha * ((_random() * 2 - 1) - lp);
                        double env = Math.Exp(-4.0 * i / len);
                        out_[start + i] += (float)(lp * note.Gain * env * bus);
                    }
                    continue;
                }
                for (int i = 0; i < len && start + i < n; i += 1)
                {
                    double t = i / (double)SampleRate;
                    double freq = note.SlideTo > 0
                        ? note.Freq * Math.Pow(note.SlideTo / note.Freq, t / note.Length)
                        : note.Freq;
                    double attack = Math.Min(1, t / 0.012);
                    double env = attack * Math.Exp(-3.0 * t / note.Length);
                    out_[start + i] += (float)(Wave(note.Type, 2 * Math.PI * freq * t) * note.Gain * env * bus);
                }
            }
            return out_;
        }
    }
}
