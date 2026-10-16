// Effect state math ported from territory/public/fx.js: particle motion,
// rings, capture flashes, floaters, confetti, and screen shake. Pure data +
// Update(dt); rendering stays in the Unity shell.
using System;
using System.Collections.Generic;

namespace Territory.Client
{
    public sealed class Particle
    {
        public double X, Y, Vx, Vy, Drag, Age, Life, Size;
        public double[] Color = { 255, 255, 255 };
    }

    public sealed class Ring
    {
        public double X, Y, Reach, Age, Delay, Life, Width;
        public double[] Color = { 255, 255, 255 };
    }

    public sealed class Flash
    {
        public int[] Xs, Ys;
        public double[] Dist;
        public double[] Bright = { 255, 255, 255 };
        public double Age, Speed, Life;
    }

    public sealed class Floater
    {
        public double X, Y, Age, Life;
        public string Text = "";
        public double[] Color = { 255, 255, 255 };
    }

    public sealed class Confetto
    {
        public double X, Y, Vx, Vy, Gravity, Drag, Rot, Spin, W, H, Age;
        public double[] Color = { 255, 255, 255 };
    }

    public sealed class FxModel
    {
        public const int MaxParticles = 1800;
        public readonly List<Particle> Particles = new List<Particle>();
        public readonly List<Ring> Rings = new List<Ring>();
        public readonly List<Flash> Flashes = new List<Flash>();
        public readonly List<Floater> Floaters = new List<Floater>();
        public readonly List<Confetto> Confetti = new List<Confetto>();
        public double ShakeAmp;
        public bool ReducedMotion;

        private readonly Func<double> _random;
        private static readonly double[][] Palette = {
            new double[] {255,42,109}, new double[] {57,255,210},
            new double[] {255,214,10}, new double[] {20,184,255},
            new double[] {255,106,0}, new double[] {190,90,255},
        };
        private static readonly double[] White = { 255, 255, 255 };

        public FxModel(Func<double> random = null, bool reducedMotion = false)
        {
            _random = random ?? new Func<double>(() => new Random().NextDouble());
            ReducedMotion = reducedMotion;
        }

        private double Rand(double min, double max) => min + _random() * (max - min);

        private void AddParticle(Particle p)
        {
            if (Particles.Count < MaxParticles) Particles.Add(p);
        }

        private void Burst(double x, double y, double[] color, int count,
            double minSpeed, double maxSpeed, double minLife, double maxLife, double size)
        {
            for (int i = 0; i < count; i += 1)
            {
                double angle = Rand(0, Math.PI * 2);
                double speed = Rand(minSpeed, maxSpeed);
                AddParticle(new Particle
                {
                    X = x, Y = y,
                    Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed,
                    Drag = 2.2, Age = 0, Life = Rand(minLife, maxLife),
                    Size = Rand(size * 0.5, size),
                    Color = _random() < 0.25 ? White : color,
                });
            }
        }

        // A closed loop: returns the flash origin for the label.
        public void Capture(List<int> cells, int w, double[] color, double? ox, double? oy, string label)
        {
            if (ReducedMotion || cells.Count == 0) return;
            int n = cells.Count;
            var xs = new int[n];
            var ys = new int[n];
            double cx = 0, cy = 0;
            for (int k = 0; k < n; k += 1)
            {
                int x = cells[k] % w;
                xs[k] = x; ys[k] = (cells[k] - x) / w;
                cx += x; cy += ys[k];
            }
            cx = cx / n + 0.5; cy = cy / n + 0.5;
            double originX = ox ?? cx, originY = oy ?? cy;
            var dist = new double[n];
            double maxDist = 1;
            for (int k = 0; k < n; k += 1)
            {
                dist[k] = Math.Sqrt((xs[k] + 0.5 - originX) * (xs[k] + 0.5 - originX)
                    + (ys[k] + 0.5 - originY) * (ys[k] + 0.5 - originY));
                if (dist[k] > maxDist) maxDist = dist[k];
            }
            double speed = Math.Max(28, maxDist / 0.55);
            var bright = new double[] {
                Math.Round(color[0] + (255 - color[0]) * 0.65),
                Math.Round(color[1] + (255 - color[1]) * 0.65),
                Math.Round(color[2] + (255 - color[2]) * 0.65) };
            Flashes.Add(new Flash { Xs = xs, Ys = ys, Dist = dist, Bright = bright, Age = 0, Speed = speed, Life = maxDist / speed + 0.8 });
            double reach = Math.Min(maxDist + 4, 30);
            Rings.Add(new Ring { X = originX, Y = originY, Reach = reach, Age = 0, Delay = 0, Life = 0.85, Color = color, Width = 0.9 });
            Rings.Add(new Ring { X = originX, Y = originY, Reach = reach * 0.7, Age = 0, Delay = 0.12, Life = 0.7, Color = White, Width = 0.5 });
            Floaters.Add(new Floater { X = originX, Y = originY - 2.5, Text = label, Color = color, Age = 0, Life = 1.7 });
            int sparks = Math.Min(70, 12 + n / 4);
            for (int i = 0; i < sparks; i += 1)
            {
                int k = (int)(_random() * n);
                AddParticle(new Particle
                {
                    X = xs[k] + 0.5, Y = ys[k] + 0.5,
                    Vx = Rand(-2, 2), Vy = Rand(-9, -2), Drag = 1.2,
                    Age = 0, Life = Rand(0.6, 1.3), Size = Rand(0.12, 0.3),
                    Color = _random() < 0.5 ? White : color,
                });
            }
        }

        public void Death(double x, double y, double[] color, bool mine, double near)
        {
            if (ReducedMotion) return;
            Burst(x, y, color, mine ? 110 : 80, 5, 22, 0.5, 1.5, 0.42);
            Burst(x, y, White, 24, 8, 26, 0.25, 0.6, 0.28);
            Rings.Add(new Ring { X = x, Y = y, Reach = 8, Age = 0, Delay = 0, Life = 0.45, Color = White, Width = 1.1 });
            Rings.Add(new Ring { X = x, Y = y, Reach = 17, Age = 0, Delay = 0.05, Life = 0.95, Color = color, Width = 0.9 });
            Rings.Add(new Ring { X = x, Y = y, Reach = 26, Age = 0, Delay = 0.2, Life = 1.15, Color = color, Width = 0.45 });
            ShakeAmp = Math.Max(ShakeAmp, mine ? 1 : near);
        }

        public void TrailSparks(double x, double y, double heading, double[] color, double dt)
        {
            if (ReducedMotion) return;
            int count = (int)(dt * 70 + _random());
            for (int i = 0; i < count; i += 1)
            {
                double back = heading + Math.PI + Rand(-0.55, 0.55);
                double speed = Rand(1.5, 5);
                AddParticle(new Particle
                {
                    X = x + Rand(-0.2, 0.2), Y = y + Rand(-0.2, 0.2),
                    Vx = Math.Cos(back) * speed, Vy = Math.Sin(back) * speed,
                    Drag = 3, Age = 0, Life = Rand(0.3, 0.7), Size = Rand(0.1, 0.22),
                    Color = _random() < 0.3 ? White : color,
                });
            }
        }

        public void Celebrate(List<double[]> colors, bool winner, double vw, double vh)
        {
            if (ReducedMotion) return;
            var palette = new List<double[]>();
            if (colors.Count > 0) { palette.AddRange(colors); palette.AddRange(colors); }
            palette.AddRange(Palette);
            int count = winner ? 220 : 80;
            for (int i = 0; i < count; i += 1)
            {
                bool fromCannon = i % 3 != 0;
                bool left = i % 2 == 0;
                var c = new Confetto
                {
                    Color = palette[(int)(_random() * palette.Count)],
                    Rot = Rand(0, 6.3), Age = 0,
                };
                if (fromCannon)
                {
                    c.X = left ? 0 : vw; c.Y = vh;
                    c.Vx = (left ? 1 : -1) * Rand(150, 620); c.Vy = -Rand(520, 1050);
                    c.Gravity = 780; c.Drag = 0.55; c.Spin = Rand(-12, 12);
                    c.W = Rand(7, 14); c.H = Rand(4, 8);
                }
                else
                {
                    c.X = Rand(0, vw); c.Y = Rand(-vh * 0.4, -10);
                    c.Vx = Rand(-50, 50); c.Vy = Rand(80, 240);
                    c.Gravity = 260; c.Drag = 0.2; c.Spin = Rand(-8, 8);
                    c.W = Rand(6, 12); c.H = Rand(3, 7);
                }
                Confetti.Add(c);
            }
        }

        public void Update(double dt, double vh = 10000)
        {
            foreach (var p in Particles)
            {
                p.Age += dt;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                double damp = Math.Exp(-p.Drag * dt);
                p.Vx *= damp; p.Vy *= damp;
            }
            Particles.RemoveAll(p => p.Age >= p.Life);
            foreach (var r in Rings) r.Age += dt;
            Rings.RemoveAll(r => r.Age >= r.Life + r.Delay);
            foreach (var f in Flashes) f.Age += dt;
            Flashes.RemoveAll(f => f.Age >= f.Life);
            foreach (var f in Floaters) f.Age += dt;
            Floaters.RemoveAll(f => f.Age >= f.Life);
            foreach (var c in Confetti)
            {
                c.Age += dt;
                c.Vy += c.Gravity * dt;
                double damp = Math.Exp(-c.Drag * dt);
                c.Vx *= damp;
                c.Vy *= Math.Exp(-0.25 * dt);
                c.X += c.Vx * dt;
                c.Y += c.Vy * dt;
                c.Rot += c.Spin * dt;
            }
            Confetti.RemoveAll(c => c.Y >= vh + 40);
        }

        // Pixel offset for this frame. Fades out on its own.
        public void Shake(double dt, out double x, out double y)
        {
            if (ReducedMotion || ShakeAmp <= 0.01) { ShakeAmp = 0; x = 0; y = 0; return; }
            double magnitude = ShakeAmp * ShakeAmp * 20;
            ShakeAmp = Math.Max(0, ShakeAmp - dt * 2.1);
            x = Rand(-magnitude, magnitude);
            y = Rand(-magnitude, magnitude);
        }
    }
}
