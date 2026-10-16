// Renders the board to a single texture, mirroring drawRoundedMap() in
// client.js: connected same-owner cells share sides, outer corners are
// rounded, diagonal steps are stitched. One repaint per snapshot, partial
// repaints per landRuns delta.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Territory.Client.Unity
{
    public sealed class BoardRenderer : MonoBehaviour
    {
        public RawImage mapView;
        public RawImage minimapView;
        public Camera worldCamera;
        public GameObject mapQuad;
        public GameClient client;
        public int pixelsPerCell = 8;

        private Texture2D _map;
        private int _tw, _th;
        private Vector2 _cam;
        private bool _camReady;
        private readonly Dictionary<int, Color32> _landColors = new Dictionary<int, Color32>();
        private readonly Dictionary<int, Color32> _trailColors = new Dictionary<int, Color32>();
        private readonly Dictionary<int, GameObject> _heads = new Dictionary<int, GameObject>();
        private Sprite _dot;

        public static Color32 ParseColor(string hex)
        {
            int n = System.Convert.ToInt32(hex.Substring(1), 16);
            return new Color32((byte)(n >> 16), (byte)((n >> 8) & 255), (byte)(n & 255), 255);
        }

        void Awake()
        {
            _dot = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            if (client == null) client = FindObjectOfType<GameClient>();
        }

        public void ResetCamera() => _camReady = false;

        private int OwnerAt(int x, int y)
        {
            var m = client.model;
            if (x < 0 || y < 0 || x >= m.W || y >= m.H) return 0;
            int i = y * m.W + x;
            return m.Trail[i] != 0 ? m.Trail[i] : m.Land[i];
        }

        private Color32 ColorFor(int id, bool isTrail)
        {
            if (!_landColors.TryGetValue(id, out var c)) return new Color32(57, 255, 210, 255);
            if (!isTrail) return c;
            if (!_trailColors.TryGetValue(id, out var t)) return c;
            return t;
        }

        private void RememberColors(BodyState b)
        {
            if (_landColors.ContainsKey(b.Id)) return;
            Color32 c = ParseColor(b.Color);
            _landColors[b.Id] = c;
            _trailColors[b.Id] = new Color32(
                (byte)(c.r + (255 - c.r) * 0.42f),
                (byte)(c.g + (255 - c.g) * 0.42f),
                (byte)(c.b + (255 - c.b) * 0.42f), 255);
        }

        public void Rebuild()
        {
            var m = client.model;
            if (m.Land == null) return;
            _tw = m.W * pixelsPerCell;
            _th = m.H * pixelsPerCell;
            if (_map == null || _map.width != _tw || _map.height != _th)
            {
                if (_map != null) Destroy(_map);
                _map = new Texture2D(_tw, _th, TextureFormat.RGBA32, false);
                _map.filterMode = FilterMode.Point;
                mapView.texture = _map;
                minimapView.texture = _map;
            }
            foreach (var b in m.Bodies.Values) RememberColors(b);
            PaintRect(0, 0, m.W, m.H);
            _map.Apply(false);
            if (mapQuad != null)
            {
                mapQuad.transform.position = new Vector3(m.W / 2f, -m.H / 2f, 10);
                mapQuad.transform.localScale = new Vector3(m.W, m.H, 1);
                var rend = mapQuad.GetComponent<MeshRenderer>();
                if (rend != null)
                {
                    if (rend.material == null || rend.material.mainTexture != _map)
                    {
                        rend.material = new Material(Shader.Find("Unlit/Texture"));
                        rend.material.mainTexture = _map;
                    }
                }
            }
            SyncHeads();
        }

        public void ApplyRuns(List<int[]> runs)
        {
            var m = client.model;
            if (_map == null || m.Land == null) return;
            foreach (var b in m.Bodies.Values) RememberColors(b);
            foreach (var run in runs)
            {
                int x0 = run[0] % m.W, y0 = run[0] / m.W;
                int x1 = (run[0] + run[1] - 1) % m.W, y1 = (run[0] + run[1] - 1) / m.W;
                PaintRect(Mathf.Max(0, Mathf.Min(x0, x1) - 1), Mathf.Max(0, Mathf.Min(y0, y1) - 1),
                    Mathf.Abs(x1 - x0) + 3, Mathf.Abs(y1 - y0) + 3);
            }
            _map.Apply(false);
            SyncHeads();
        }

        private void PaintRect(int cx0, int cy0, int cw, int ch)
        {
            var m = client.model;
            float radius = pixelsPerCell * 0.36f;
            for (int cy = cy0; cy < cy0 + ch && cy < m.H; cy += 1)
            {
                for (int cx = cx0; cx < cx0 + cw && cx < m.W; cx += 1)
                {
                    if (cx < 0 || cy < 0) continue;
                    int id = OwnerAt(cx, cy);
                    bool isTrail = m.Trail[cy * m.W + cx] != 0;
                    for (int py = 0; py < pixelsPerCell; py += 1)
                    {
                        for (int px = 0; px < pixelsPerCell; px += 1)
                        {
                            Color32 col = id == 0
                                ? new Color32(7, 9, 16, 255)
                                : ColorFor(id, isTrail);
                            if (id != 0 && IsCutCorner(cx, cy, id, px, py, radius))
                                col = new Color32(7, 9, 16, 255);
                            _map.SetPixel(cx * pixelsPerCell + px, _th - 1 - (cy * pixelsPerCell + py), col);
                        }
                    }
                    // Diagonal stitch, mirroring the seam fill in drawRoundedMap().
                    if (id != 0 && OwnerAt(cx + 1, cy + 1) == id
                        && OwnerAt(cx + 1, cy) != id && OwnerAt(cx, cy + 1) != id)
                    {
                        Color32 col = ColorFor(id, isTrail);
                        int sx = cx * pixelsPerCell + pixelsPerCell - 1;
                        int sy = cy * pixelsPerCell + pixelsPerCell - 1;
                        _map.SetPixel(sx, _th - 1 - sy, col);
                        _map.SetPixel(sx - 1, _th - 1 - sy, col);
                        _map.SetPixel(sx, _th - 1 - (sy - 1), col);
                        _map.SetPixel(sx - 1, _th - 1 - (sy - 1), col);
                    }
                }
            }
        }

        private bool Same(int x, int y, int id) => OwnerAt(x, y) == id;

        private bool IsCutCorner(int cx, int cy, int id, int px, int py, float radius)
        {
            float half = pixelsPerCell;
            // Top-left / top-right / bottom-left / bottom-right in texture space.
            if (!Same(cx, cy - 1, id) && !Same(cx - 1, cy, id)
                && CornerDist(px, py, 0, half, half) < radius) return true;
            if (!Same(cx, cy - 1, id) && !Same(cx + 1, cy, id)
                && CornerDist(px, py, half, half, half) < radius) return true;
            if (!Same(cx, cy + 1, id) && !Same(cx - 1, cy, id)
                && CornerDist(px, py, 0, 0, half) < radius) return true;
            if (!Same(cx, cy + 1, id) && !Same(cx + 1, cy, id)
                && CornerDist(px, py, half, 0, half) < radius) return true;
            return false;
        }

        private static float CornerDist(float px, float py, float ox, float oy, float half)
        {
            float dx = px < half ? (ox == 0 ? half - px : px - (half - 1)) : 999;
            float dy = py < half ? (oy == 0 ? half - py : py - (half - 1)) : 999;
            if (dx > 100 || dy > 100) return 999;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private void SyncHeads()
        {
            var seen = new HashSet<int>();
            foreach (var b in client.model.Bodies.Values)
            {
                if (!b.Alive) continue;
                seen.Add(b.Id);
                if (!_heads.TryGetValue(b.Id, out var go))
                {
                    go = new GameObject("Head" + b.Id);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = _dot;
                    var label = new GameObject("Label");
                    label.transform.SetParent(go.transform, false);
                    var tm = label.AddComponent<TextMesh>();
                    tm.anchor = TextAnchor.LowerCenter;
                    tm.fontSize = 48;
                    tm.characterSize = 0.06f;
                    _heads[b.Id] = go;
                }
                var rend = go.GetComponent<SpriteRenderer>();
                Color32 c = ParseColor(b.Color);
                rend.color = c;
                float radius = 0.34f;
                go.transform.localScale = new Vector3(radius * 2, radius * 2, 1);
                var tm2 = go.transform.GetChild(0).GetComponent<TextMesh>();
                tm2.text = b.Name;
                tm2.characterSize = 0.02f;
                tm2.transform.localPosition = new Vector3(0, 0.9f, 0);
                tm2.color = new Color(c.r / 255f, c.g / 255f, c.b / 255f);
            }
            foreach (var kv in new Dictionary<int, GameObject>(_heads))
            {
                if (seen.Contains(kv.Key)) continue;
                Destroy(kv.Value);
                _heads.Remove(kv.Key);
            }
        }

        void Update()
        {
            var m = client.model;
            if (m.Land == null || worldCamera == null) return;
            // World units are map cells: cell (x,y) sits at (x+0.5, -(y+0.5)).
            float cellPx = (float)ViewHelpers.FollowCellSize(Screen.width);
            BodyState me = client.myId != null && m.Bodies.TryGetValue(client.myId.Value, out var b) ? b : null;
            bool follow = me != null && me.Alive && client.phase != "over";
            if (follow)
            {
                BoardModel.Pose(me, Time.unscaledTime * 1000, client.tickMs, out double px, out double py);
                Vector2 target = new Vector2((float)px, -(float)py);
                if (!_camReady) { _cam = target; _camReady = true; }
                else _cam += (target - _cam) * (1 - Mathf.Exp(-Time.unscaledDeltaTime * 10));
                worldCamera.transform.position = new Vector3(_cam.x, _cam.y, -10);
                worldCamera.orthographicSize = Screen.height / cellPx / 2f;
            }
            else
            {
                _camReady = false;
                worldCamera.transform.position = new Vector3(m.W / 2f, -m.H / 2f, -10);
                worldCamera.orthographicSize = m.H / 2f + 2;
            }
            foreach (var kv in _heads)
            {
                if (!m.Bodies.TryGetValue(kv.Key, out var body) || !body.Alive) continue;
                BoardModel.Pose(body, Time.unscaledTime * 1000, client.tickMs, out double hx, out double hy);
                kv.Value.transform.position = new Vector3((float)hx, -(float)hy, 0);
                kv.Value.transform.rotation = Quaternion.Euler(0, 0, -(float)body.Heading * Mathf.Rad2Deg);
            }
        }
    }
}
