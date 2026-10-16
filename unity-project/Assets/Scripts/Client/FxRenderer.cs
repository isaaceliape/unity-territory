// Renders FxModel state with Unity primitives: one dynamic quad mesh for
// particles/flashes/rings, pooled TextMesh floaters, pooled LineRenderer
// danger shimmer is folded into the board tint pass (see below).
using System.Collections.Generic;
using UnityEngine;

namespace Territory.Client.Unity
{
    public sealed class FxRenderer : MonoBehaviour
    {
        public GameClient client;
        public BoardRenderer board;
        public int maxQuads = 2048;

        private Mesh _mesh;
        private MeshRenderer _rend;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color32> _cols = new List<Color32>();
        private readonly List<int> _tris = new List<int>();
        private readonly List<TextMesh> _floaters = new List<TextMesh>();
        private readonly List<LineRenderer> _rings = new List<LineRenderer>();

        void Awake()
        {
            if (client == null) client = FindObjectOfType<GameClient>();
            var go = new GameObject("FxMesh");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            _rend = go.AddComponent<MeshRenderer>();
            _mesh = new Mesh();
            _mesh.MarkDynamic();
            filter.mesh = _mesh;
            _rend.material = new Material(Shader.Find("Sprites/Default"));
        }

        private static Color32 C(double[] rgb, float alpha)
        {
            return new Color32((byte)rgb[0], (byte)rgb[1], (byte)rgb[2],
                (byte)(Mathf.Clamp01(alpha) * 255));
        }

        private void Quad(float x, float y, float size, Color32 col)
        {
            if (_verts.Count / 4 >= maxQuads) return;
            int base_ = _verts.Count;
            float h = size / 2;
            _verts.Add(new Vector3(x - h, y - h, -1));
            _verts.Add(new Vector3(x + h, y - h, -1));
            _verts.Add(new Vector3(x + h, y + h, -1));
            _verts.Add(new Vector3(x - h, y + h, -1));
            for (int i = 0; i < 4; i += 1) _cols.Add(col);
            _tris.Add(base_); _tris.Add(base_ + 1); _tris.Add(base_ + 2);
            _tris.Add(base_); _tris.Add(base_ + 2); _tris.Add(base_ + 3);
        }

        public void ShowCapture(List<int> cells, int w, BodyState body)
        {
            double ox = body != null ? body.ToX : 0;
            double oy = body != null ? body.ToY : 0;
            var fx = client.effects;
            var col = body != null
                ? new double[] { BoardRenderer.ParseColor(body.Color).r, BoardRenderer.ParseColor(body.Color).g, BoardRenderer.ParseColor(body.Color).b }
                : new double[] { 57, 255, 210 };
            fx.Capture(cells, w, col, ox, oy, $"+{cells.Count}");
        }

        public void ShowDeath(BodyState d)
        {
            var c = BoardRenderer.ParseColor(d.Color);
            client.effects.Death(d.ToX, d.ToY, new double[] { c.r, c.g, c.b }, false, 0.4);
        }

        void LateUpdate()
        {
            if (client == null || client.model.Land == null) return;
            var fx = client.effects;
            double t = Time.unscaledTime;
            _verts.Clear(); _cols.Clear(); _tris.Clear();

            foreach (var f in fx.Flashes)
            {
                double front = f.Age * f.Speed;
                for (int k = 0; k < f.Xs.Length; k += 1)
                {
                    double since = (front - f.Dist[k]) / f.Speed;
                    if (since < 0) continue;
                    double alpha = 1 - since / 0.75;
                    if (alpha <= 0) continue;
                    Quad(f.Xs[k] + 0.5f, -(f.Ys[k] + 0.5f), 1f, C(f.Bright, (float)(alpha * alpha * 0.8)));
                }
            }
            foreach (var p in fx.Particles)
            {
                double progress = p.Age / p.Life;
                Quad((float)p.X, -(float)p.Y, (float)(p.Size * (1 - progress * 0.5) + 0.05),
                    C(p.Color, (float)System.Math.Pow(1 - progress, 1.3)));
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_cols);
            _mesh.SetTriangles(_tris, 0);

            SyncRings(fx);
            SyncFloaters(fx);
            _ = t;
        }

        private void SyncRings(FxModel fx)
        {
            int i = 0;
            foreach (var r in fx.Rings)
            {
                double progress = (r.Age - r.Delay) / r.Life;
                if (progress <= 0) continue;
                LineRenderer lr = GetRing(i++);
                double eased = 1 - System.Math.Pow(1 - progress, 3);
                float radius = (float)System.Math.Max(0.1, eased * r.Reach);
                lr.positionCount = 48;
                for (int k = 0; k < 48; k += 1)
                {
                    float a = k / 48f * Mathf.PI * 2;
                    lr.SetPosition(k, new Vector3(
                        (float)r.X + Mathf.Cos(a) * radius,
                        -(float)r.Y + Mathf.Sin(a) * radius, -1));
                }
                var col = C(r.Color, (float)System.Math.Pow(1 - progress, 1.4));
                lr.startColor = col; lr.endColor = col;
                lr.widthMultiplier = (float)System.Math.Max(0.05, (1 - progress) * r.Width);
                lr.gameObject.SetActive(true);
            }
            for (; i < _rings.Count; i += 1) _rings[i].gameObject.SetActive(false);
        }

        private LineRenderer GetRing(int i)
        {
            while (_rings.Count <= i)
            {
                var go = new GameObject("Ring" + _rings.Count);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.useWorldSpace = true;
                _rings.Add(lr);
            }
            return _rings[i];
        }

        private void SyncFloaters(FxModel fx)
        {
            int i = 0;
            foreach (var f in fx.Floaters)
            {
                double progress = f.Age / f.Life;
                TextMesh tm = GetFloater(i++);
                tm.text = f.Text;
                tm.transform.position = new Vector3((float)f.X, -(float)f.Y - (float)progress * 5, -1);
                var col = C(f.Color, progress < 0.7 ? 1 : (float)(1 - (progress - 0.7) / 0.3));
                tm.color = col;
                tm.gameObject.SetActive(true);
            }
            for (; i < _floaters.Count; i += 1) _floaters[i].gameObject.SetActive(false);
        }

        private TextMesh GetFloater(int i)
        {
            while (_floaters.Count <= i)
            {
                var go = new GameObject("Floater" + _floaters.Count);
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.MiddleCenter;
                tm.fontSize = 48;
                tm.characterSize = 0.03f;
                _floaters.Add(tm);
            }
            return _floaters[i];
        }
    }
}
