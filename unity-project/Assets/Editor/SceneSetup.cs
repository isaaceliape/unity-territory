// Generates Assets/Scenes/Main.unity headlessly:
//   Unity -batchmode -projectPath . -executeMethod Territory.Editor.SceneSetup.Generate -quit
// Every serialized reference the scripts need is wired here, so no manual
// scene work is required for the first playable WebGL build.
//
// Layout rules used throughout:
//  - CanvasScaler is Scale-With-Screen-Size against a 1280x720 reference so
//    the UI keeps proportions on any window.
//  - Every rect sets anchor AND pivot AND an inset offset, so nothing sits
//    half off-screen or stacked on top of a sibling.
//  - Panels arrange children top-down with explicit Y offsets.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Territory.Client.Unity;

namespace Territory.Editor
{
    public static class SceneSetup
    {
        private static Font _font;
        private static Transform _templates;

        // Web palette (public/style.css): bg #070910, card rgba(8,12,22,.92),
        // ink #e7fff8, muted #8ea0b8, line rgba(0,245,212,.45), glow #39ffd2.
        private static readonly Color Ink = new Color(0.905f, 1f, 0.972f);
        private static readonly Color Muted = new Color(0.557f, 0.627f, 0.722f);
        private static readonly Color CardFill = new Color(0.031f, 0.047f, 0.086f, 0.94f);
        private static readonly Color Line = new Color(0f, 0.961f, 0.831f, 0.45f);
        private static readonly Color Glow = new Color(0.224f, 1f, 0.824f);
        private static readonly Color BtnFill = new Color(0.03f, 0.16f, 0.11f, 0.98f);
        private static readonly Color InputFill = new Color(0.05f, 0.07f, 0.11f, 0.98f);
        private static readonly Color Pink = new Color(1f, 0.18f, 0.43f);

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _sprites =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        // Rounded 9-slice panel sprite generated at setup time (uGUI has no
        // CSS border-radius). Border insets so corners scale cleanly.
        private static Sprite RoundedSprite(Color fill, Color edge, int edgePx, int radius)
        {
            string key = $"{fill}_{edge}_{edgePx}_{radius}";
            if (_sprites.TryGetValue(key, out var cached)) return cached;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[S * S];
            float half = S / 2f;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    Vector2 q = new Vector2(Mathf.Abs(x - half + 0.5f), Mathf.Abs(y - half + 0.5f))
                        - new Vector2(half - radius, half - radius);
                    float sd = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude
                        + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    Color c = new Color(0, 0, 0, 0);
                    if (sd < 0) c = sd < -edgePx ? fill : edge;
                    pixels[y * S + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var spr = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
                100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            _sprites[key] = spr;
            return spr;
        }

        private static Sprite CardSprite() => RoundedSprite(CardFill, Line, 2, 26);
        private static Sprite ButtonSprite() => RoundedSprite(BtnFill, BtnFill, 0, 22);
        private static Sprite PillSprite() => RoundedSprite(new Color(0.02f, 0.05f, 0.07f, 0.9f), Line, 2, 20);
        private static Sprite InputSprite() => RoundedSprite(InputFill, new Color(0.35f, 0.45f, 0.55f, 0.6f), 2, 18);

        // Tiny how-to diagrams (web draws these on canvas): pink square is the
        // player, white/mint lines are trails. 0 = leave land, 1 = loop back,
        // 2 = cross to cut.
        private static Texture2D DiagramTexture(int kind)
        {
            const int W = 132, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[W * H]; // transparent bg
            System.Action<int, int, Color> dot = (x, y, c) =>
            {
                if (x >= 0 && y >= 0 && x < W && y < H) pixels[y * W + x] = c;
            };
            System.Action<int, int, int, Color> disc = (cx, cy, r, c) =>
            {
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        int dx = x - cx, dy = y - cy;
                        if (dx * dx + dy * dy <= r * r) dot(x, y, c);
                    }
            };
            System.Action<Vector2, Vector2, int, Color> seg = (a, b, w, c) =>
            {
                float len = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.RoundToInt(len));
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                    disc(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), w, c);
                }
            };
            Color trail = new Color(1, 1, 1, 0.95f);
            if (kind == 0)
            {
                for (int y = 24; y < 42; y++)
                    for (int x = 14; x < 32; x++) dot(x, y, Pink);
                seg(new Vector2(32, 33), new Vector2(112, 33), 2, trail);
                disc(112, 33, 4, trail);
            }
            else if (kind == 1)
            {
                for (int y = 22; y < 40; y++)
                    for (int x = 12; x < 30; x++) dot(x, y, Pink);
                seg(new Vector2(30, 31), new Vector2(96, 31), 2, trail);
                seg(new Vector2(96, 31), new Vector2(96, 48), 2, trail);
                seg(new Vector2(96, 48), new Vector2(40, 48), 2, trail);
                seg(new Vector2(40, 48), new Vector2(40, 40), 2, trail);
            }
            else
            {
                seg(new Vector2(14, 44), new Vector2(118, 44), 2, Glow);
                seg(new Vector2(66, 8), new Vector2(66, 58), 2, trail);
                for (int y = 2; y < 16; y++)
                    for (int x = 59; x < 73; x++) dot(x, y, Pink);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // Shared card header: mint title, rules paragraph, how-to trio.
        // Returns the rules text so callers can bind it to hud.blurbText.
        private static Text MkCardHeader(Transform parent)
        {
            var title = MkText(parent, "CardTitle", "Territory", 38);
            title.color = Glow;
            title.alignment = TextAnchor.MiddleLeft;
            var rules = MkText(parent, "CardRules",
                "Close a loop to take the ground inside it. Cross a line and that player is out. " +
                "Most ground when the clock stops wins.", 14);
            rules.color = Ink;
            rules.alignment = TextAnchor.UpperLeft;
            var rle = rules.gameObject.AddComponent<LayoutElement>();
            rle.minHeight = 62;
            rle.preferredHeight = 62;
            var row = new GameObject("HowTo");
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childControlWidth = true;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 108;
            rowLe.preferredHeight = 108;
            string[] caps = { "Leave your land", "Loop back to claim", "Cross a line to cut" };
            for (int i = 0; i < 3; i++) MkHowTo(row.transform, caps[i], i);
            return rules;
        }

        private static void MkHowTo(Transform parent, string caption, int kind)
        {
            var go = new GameObject("How" + kind);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = InputSprite();
            img.type = Image.Type.Sliced;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.minHeight = 104;
            le.preferredHeight = 104;
            var pic = new GameObject("Diagram");
            pic.transform.SetParent(go.transform, false);
            var raw = pic.AddComponent<RawImage>();
            raw.texture = DiagramTexture(kind);
            var prt = pic.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 1); prt.anchorMax = new Vector2(0.5f, 1);
            prt.pivot = new Vector2(0.5f, 1);
            prt.anchoredPosition = new Vector2(0, -6);
            prt.sizeDelta = new Vector2(120, 58);
            var cap = new GameObject("Caption");
            cap.transform.SetParent(go.transform, false);
            var txt = cap.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 12; txt.color = Muted;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.text = caption;
            var crt = cap.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 0);
            crt.pivot = new Vector2(0.5f, 0);
            crt.offsetMin = new Vector2(6, 4); crt.offsetMax = new Vector2(-6, 26);
        }

        // Reference resolution for the CanvasScaler.
        private const float RefW = 1280f;
        private const float RefH = 720f;

        public static void Generate()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.027f, 0.035f, 0.063f);
            cam.orthographic = true;
            cam.orthographicSize = 63;
            cam.transform.position = new Vector3(84, -63, -10);
            cam.gameObject.AddComponent<AudioListener>();

            var mapQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mapQuad.name = "MapQuad";
            Object.DestroyImmediate(mapQuad.GetComponent<MeshCollider>());

            var root = new GameObject("Client");
            var ws = root.AddComponent<UnityWsClient>();
            var bridge = root.AddComponent<TerritoryWsBridge>();
            bridge.gameObject.name = "TerritoryWs";
            var game = root.AddComponent<GameClient>();
            var board = root.AddComponent<BoardRenderer>();
            var hud = root.AddComponent<HudController>();
            var input = root.AddComponent<InputController>();
            var sfx = root.AddComponent<SfxPlayer>();
            var fxr = root.AddComponent<FxRenderer>();

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvas = new GameObject("Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvas.gameObject.AddComponent<GraphicRaycaster>();

            // --- HUD top bar ---
            // --- HUD top bar: clock pill + status --- 
            MkCardBack(canvas.transform, "ClockBack",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -32),
                new Vector2(170, 58));
            hud.clockText = MkText(canvas.transform, "Clock",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -32),
                new Vector2(170, 52), "3:00", 30);
            hud.clockText.color = Glow;
            hud.statusText = MkText(canvas.transform, "Status",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84),
                new Vector2(760, 30), "", 15);

            // --- leaderboard (top-left) ---
            // Templates live under a hidden container, NOT inside the lists:
            // RenderList destroys every child of a list on repaint.
            var templates = new GameObject("Templates");
            templates.transform.SetParent(canvas.transform, false);
            templates.SetActive(false);
            _templates = templates.transform;
            hud.boardList = MkList(canvas.transform, "Board",
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24),
                new Vector2(234, 364), 6);
            MkCardBack(canvas.transform, "BoardBack",
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16),
                new Vector2(250, 380));
            hud.boardRowPrefab = MkPlayerRowTemplate(templates.transform, "BoardRow");

            // --- minimap (top-right) + kill feed under it ---
            var mini = MkRawImage(canvas.transform, "Minimap",
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24),
                new Vector2(194, 142));
            MkCardBack(canvas.transform, "MiniBack",
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16),
                new Vector2(210, 158));
            hud.minimap = mini;
            board.minimapView = mini;
            hud.feedList = MkList(canvas.transform, "Feed",
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -186),
                new Vector2(280, 220), 4);
            hud.feedRowPrefab = MkFeedTemplate(templates.transform);

            // --- bottom corners ---
            hud.restartButton = MkButton(canvas.transform, "Restart",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 16),
                new Vector2(120, 38), "Restart", true);
            (hud.muteButton, hud.muteLabel) = MkSizedLabeledButton(canvas.transform, "Mute",
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 60),
                new Vector2(116, 36), "Sound on");
            (hud.musicButton, hud.musicLabel) = MkSizedLabeledButton(canvas.transform, "Music",
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 16),
                new Vector2(116, 36), "Music on");
            hud.touchLeft = MkButton(canvas.transform, "TouchLeft",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(150, 16),
                new Vector2(64, 44), "<", true);
            hud.touchRight = MkButton(canvas.transform, "TouchRight",
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-182, 16),
                new Vector2(64, 44), ">", true);
            hud.touchLeft.gameObject.SetActive(false);
            hud.touchRight.gameObject.SetActive(false);

            // --- overlay cards (web #card style: one morphing card) ---
            hud.overlay = MkFullscreenDim(canvas.transform, "Overlay");
            hud.joinPanel = MkPanel(hud.overlay.transform, "Join", new Vector2(560, 640));
            var joinContent = MkPanelContent(hud.joinPanel);
            MkCardHeader(joinContent);
            var joinName = MkText(joinContent, "NameLabel", "Name", 14);
            joinName.color = Muted;
            joinName.alignment = TextAnchor.MiddleLeft;
            hud.nameInput = MkInput(joinContent, "NameInput", new Vector2(0, 46));
            hud.joinButton = MkRowButton(joinContent, "JoinButton", "Join");
            var joinHint = MkText(joinContent, "Hint",
                "No VPN? Online rooms are coming to this client soon.", 13);
            joinHint.color = Muted;
            joinHint.alignment = TextAnchor.MiddleCenter;

            hud.roomPanel = MkPanel(hud.overlay.transform, "Room", new Vector2(620, 660));
            hud.roomPanel.SetActive(false);
            BuildRoomPanel(hud);

            // --- wiring ---
            game.socket = ws;
            game.board = board;
            game.hud = hud;
            game.input = input;
            game.sfx = sfx;
            game.fx = fxr;
            board.client = game;
            board.worldCamera = cam;
            board.mapQuad = mapQuad;
            hud.client = game;
            hud.board = board;
            input.client = game;
            input.touchLeft = hud.touchLeft;
            input.touchRight = hud.touchRight;
            sfx.client = game;
            fxr.client = game;
            fxr.board = board;
            bridge.target = ws;

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            Debug.Log("Scene generated: Assets/Scenes/Main.unity");
        }

        // Room card mirrors the web room view: same header, then roster and
        // host controls in a scrollable column.
        private static void BuildRoomPanel(HudController hud)
        {
            var content = MkScrollContent(hud.roomPanel);
            hud.blurbText = MkCardHeader(content);
            MkText(content.transform, "NickLabel", "Your name", 14);
            hud.nickInput = MkInput(content.transform, "NickInput", new Vector2(0, 40));
            hud.steerToggle = MkToggle(content.transform, "SteerToggle", "Steer with the cursor");
            hud.rosterList = MkList(content.transform, "Roster", new Vector2(120, 4));
            hud.playerRowPrefab = MkPlayerRowTemplate(_templates, "PlayerRow");
            hud.shareText = MkText(content.transform, "Share", "", 12);
            hud.minutesDropdown = MkDropdown(content.transform, "Minutes",
                new[] { "1 minute", "2 minutes", "3 minutes", "5 minutes" }, 2);
            hud.botLevelDropdown = MkDropdown(content.transform, "BotLevel",
                new[] { "easy", "normal", "hard" }, 1);
            hud.addBotButton = MkRowButton(content.transform, "AddBot", "Add bot");
            hud.addBotLabel = hud.addBotButton.GetComponentInChildren<Text>();
            hud.startButton = MkRowButton(content.transform, "Start", "Start");
            hud.startLabel = hud.startButton.GetComponentInChildren<Text>();
            hud.waitingText = MkText(content.transform, "Waiting", "", 13);
            hud.errorText = MkText(content.transform, "Error", "", 13);
            hud.resultsList = MkList(content.transform, "Results", new Vector2(90, 4));
        }

        // Full-stretch vertical content inside a fixed card.
        private static Transform MkPanelContent(GameObject panel)
        {
            var content = new GameObject("Content");
            content.transform.SetParent(panel.transform, false);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(28, 28, 24, 24);
            vlg.spacing = 10;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            return content.transform;
        }

        // Scrollable vertical content inside a fixed card (room view).
        private static Transform MkScrollContent(GameObject panel)
        {
            var scrollGO = new GameObject("Scroll");
            scrollGO.transform.SetParent(panel.transform, false);
            var srt = scrollGO.AddComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGO.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = vrt;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(28, 28, 24, 24);
            vlg.spacing = 8;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            return content.transform;
        }

        // Rounded card backing behind a HUD element (web #board/#clock style).
        private static void MkCardBack(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var img = go.AddComponent<Image>();
            img.sprite = CardSprite();
            img.type = Image.Type.Sliced;
            Place(go.GetComponent<RectTransform>(), anchor, anchor, pivot, offset, size);
        }
        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 offset, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        private static Text MkText(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offset, Vector2 size,
            string text, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = Ink;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Place(go.GetComponent<RectTransform>(), anchorMin, anchorMax,
                new Vector2(0.5f, 0.5f), offset, size);
            return t;
        }

        // Layout-group child: full-width row with a fixed minimum height.
        private static Text MkText(Transform parent, string name, string text, int fontSize)
        {
            var t = MkText(parent, name, Vector2.zero, Vector2.one, Vector2.zero,
                new Vector2(0, fontSize + 12), text, fontSize);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 12;
            return t;
        }

        private static Transform MkList(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offset, Vector2 size, int spacing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            Place(go.GetComponent<RectTransform>(), anchorMin, anchorMax,
                anchorMin, offset, size);
            return go.transform;
        }

        // Layout-group child list: stretches full width, fixed height.
        private static Transform MkList(Transform parent, string name, Vector2 size)
        {
            var list = MkList(parent, name, Vector2.zero, Vector2.one, Vector2.zero, size, 4);
            var le = list.gameObject.AddComponent<LayoutElement>();
            le.minHeight = size.y;
            le.preferredHeight = size.y;
            return list;
        }

        private static GameObject MkPlayerRowTemplate(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 24;
            le.preferredHeight = 24;

            var swatch = new GameObject("Swatch");
            swatch.transform.SetParent(go.transform, false);
            swatch.AddComponent<Image>().color = Color.white;
            var sle = swatch.AddComponent<LayoutElement>();
            sle.minWidth = 16; sle.minHeight = 16;
            sle.preferredWidth = 16; sle.preferredHeight = 16;
            sle.flexibleWidth = 0; sle.flexibleHeight = 0;
            var swRt = swatch.GetComponent<RectTransform>();
            swRt.sizeDelta = new Vector2(16, 16);

            var nameT = new GameObject("Name");
            nameT.transform.SetParent(go.transform, false);
            var nt = nameT.AddComponent<Text>();
            nt.font = _font; nt.fontSize = 14; nt.color = Ink;
            nt.alignment = TextAnchor.MiddleLeft;
            var nle = nameT.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1;

            var stat = new GameObject("Stat");
            stat.transform.SetParent(go.transform, false);
            var st = stat.AddComponent<Text>();
            st.font = _font; st.fontSize = 14; st.color = Muted;
            st.alignment = TextAnchor.MiddleRight;
            var sle2 = stat.AddComponent<LayoutElement>();
            sle2.minWidth = 90; sle2.preferredWidth = 90; sle2.flexibleWidth = 0;

            go.SetActive(false);
            return go;
        }

        private static GameObject MkFeedTemplate(Transform parent)
        {
            var go = new GameObject("FeedRow");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = 13; t.color = Ink;
            t.alignment = TextAnchor.MiddleRight;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 18;
            go.SetActive(false);
            return go;
        }

        private static Button MkButton(Transform parent, string name,
            Vector2 anchorMin, Vector2 pivot, Vector2 offset, Vector2 size, string label,
            bool pill = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = pill ? PillSprite() : ButtonSprite();
            img.type = Image.Type.Sliced;
            var btn = go.AddComponent<Button>();
            var t = new GameObject("Label");
            t.transform.SetParent(go.transform, false);
            var txt = t.AddComponent<Text>();
            txt.font = _font; txt.fontSize = pill ? 14 : 16; txt.color = Glow;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = label;
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            Place(go.GetComponent<RectTransform>(), anchorMin, anchorMin, pivot, offset, size);
            return btn;
        }

        private static (Button, Text) MkSizedLabeledButton(Transform parent, string name,
            Vector2 anchorMin, Vector2 pivot, Vector2 offset, Vector2 size, string label)
        {
            var btn = MkButton(parent, name, anchorMin, pivot, offset, size, label, true);
            return (btn, btn.GetComponentInChildren<Text>());
        }

        // Full-width row button for layout groups.
        private static Button MkRowButton(Transform parent, string name, string label)
        {
            var btn = MkButton(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(200, 44), label);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 44;
            le.preferredHeight = 44;
            return btn;
        }

        private static GameObject MkPanel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = CardSprite();
            img.type = Image.Type.Sliced;
            Place(go.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return go;
        }

        private static GameObject MkFullscreenDim(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.01f, 0.015f, 0.03f, 0.78f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static InputField MkInput(Transform parent, string name,
            Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = InputSprite();
            img.type = Image.Type.Sliced;
            var input = go.AddComponent<InputField>();
            var t = new GameObject("Text");
            t.transform.SetParent(go.transform, false);
            var txt = t.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 16; txt.color = Ink;
            txt.alignment = TextAnchor.MiddleLeft;
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-10, 0);
            input.textComponent = txt;
            Place(go.GetComponent<RectTransform>(), anchor, anchor,
                new Vector2(0.5f, 0.5f), offset, size);
            return input;
        }

        // Layout-group child input: full width, fixed height.
        private static InputField MkInput(Transform parent, string name, Vector2 size)
        {
            var input = MkInput(parent, name, Vector2.zero, Vector2.zero, size);
            var le = input.gameObject.AddComponent<LayoutElement>();
            le.minHeight = size.y;
            le.preferredHeight = size.y;
            return input;
        }

        private static Toggle MkToggle(Transform parent, string name, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 28;

            var box = new GameObject("Background");
            box.transform.SetParent(go.transform, false);
            var img = box.AddComponent<Image>();
            img.color = InputFill;
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.sizeDelta = new Vector2(20, 20);
            var check = new GameObject("Checkmark");
            check.transform.SetParent(box.transform, false);
            var cimg = check.AddComponent<Image>();
            cimg.color = new Color(0.2f, 1f, 0.83f);
            var lab = new GameObject("Label");
            lab.transform.SetParent(go.transform, false);
            var txt = lab.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 14; txt.color = Ink; txt.text = label;
            txt.alignment = TextAnchor.MiddleLeft;
            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = img;
            toggle.graphic = cimg;
            return toggle;
        }

        private static Dropdown MkDropdown(Transform parent, string name, string[] options, int value)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = InputSprite();
            img.type = Image.Type.Sliced;
            var dd = go.AddComponent<Dropdown>();
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;

            var cap = new GameObject("Label");
            cap.transform.SetParent(go.transform, false);
            var txt = cap.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 14; txt.color = Ink;
            txt.alignment = TextAnchor.MiddleLeft;
            var trt = cap.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-10, 0);
            dd.captionText = txt;

            // Proper dropdown list template: Template > Viewport > Content > Item.
            var template = new GameObject("Template");
            template.transform.SetParent(go.transform, false);
            var timg = template.AddComponent<Image>();
            timg.color = new Color(0.05f, 0.07f, 0.1f, 0.98f);
            var scroll = template.AddComponent<ScrollRect>();
            var trt2 = template.GetComponent<RectTransform>();
            trt2.anchorMin = new Vector2(0, 0); trt2.anchorMax = new Vector2(1, 0);
            trt2.pivot = new Vector2(0.5f, 1);
            trt2.anchoredPosition = new Vector2(0, -2);
            trt2.sizeDelta = new Vector2(0, 120);

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            crt.sizeDelta = new Vector2(0, 28 * options.Length);

            var item = new GameObject("Item", typeof(RectTransform));
            item.transform.SetParent(content.transform, false);
            var itemToggle = item.AddComponent<Toggle>();
            var irt = item.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 1); irt.anchorMax = new Vector2(1, 1);
            irt.pivot = new Vector2(0.5f, 1);
            irt.sizeDelta = new Vector2(0, 28);
            var itemBg = new GameObject("Item Background");
            itemBg.transform.SetParent(item.transform, false);
            var ibimg = itemBg.AddComponent<Image>();
            ibimg.color = new Color(0.1f, 0.13f, 0.18f);
            var ibRt = itemBg.GetComponent<RectTransform>();
            ibRt.anchorMin = Vector2.zero; ibRt.anchorMax = Vector2.one;
            ibRt.offsetMin = Vector2.zero; ibRt.offsetMax = Vector2.zero;
            var itemCheck = new GameObject("Item Checkmark");
            itemCheck.transform.SetParent(item.transform, false);
            var icimg = itemCheck.AddComponent<Image>();
            icimg.color = new Color(0.2f, 1f, 0.83f);
            var icRt = itemCheck.GetComponent<RectTransform>();
            icRt.anchorMin = new Vector2(0, 0.5f); icRt.anchorMax = new Vector2(0, 0.5f);
            icRt.pivot = new Vector2(0.5f, 0.5f);
            icRt.sizeDelta = new Vector2(16, 16);
            icRt.anchoredPosition = new Vector2(14, 0);
            var itemLabel = new GameObject("Item Label");
            itemLabel.transform.SetParent(item.transform, false);
            var iltxt = itemLabel.AddComponent<Text>();
            iltxt.font = _font; iltxt.fontSize = 14; iltxt.color = Color.white;
            iltxt.alignment = TextAnchor.MiddleLeft;
            var ilRt = itemLabel.GetComponent<RectTransform>();
            ilRt.anchorMin = Vector2.zero; ilRt.anchorMax = Vector2.one;
            ilRt.offsetMin = new Vector2(30, 0); ilRt.offsetMax = new Vector2(-6, 0);
            itemToggle.targetGraphic = ibimg;
            itemToggle.graphic = icimg;
            item.SetActive(true);

            dd.template = trt2;
            dd.itemText = iltxt;
            scroll.content = crt;
            scroll.viewport = vrt;
            template.SetActive(false);

            dd.ClearOptions();
            dd.AddOptions(new System.Collections.Generic.List<string>(options));
            dd.value = value;
            dd.RefreshShownValue();
            return dd;
        }

        private static RawImage MkRawImage(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.color = Color.white;
            Place(go.GetComponent<RectTransform>(), anchorMin, anchorMax, anchorMax, offset, size);
            return img;
        }
    }
}
