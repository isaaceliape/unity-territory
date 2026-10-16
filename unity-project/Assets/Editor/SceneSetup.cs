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
            hud.clockText = MkText(canvas.transform, "Clock",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30),
                new Vector2(220, 48), "3:00", 30);
            hud.statusText = MkText(canvas.transform, "Status",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -82),
                new Vector2(760, 30), "", 15);

            // --- leaderboard (top-left) ---
            // Templates live under a hidden container, NOT inside the lists:
            // RenderList destroys every child of a list on repaint.
            var templates = new GameObject("Templates");
            templates.transform.SetParent(canvas.transform, false);
            templates.SetActive(false);
            _templates = templates.transform;
            hud.boardList = MkList(canvas.transform, "Board",
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16),
                new Vector2(250, 380), 6);
            hud.boardRowPrefab = MkPlayerRowTemplate(templates.transform, "BoardRow");

            // --- minimap (top-right) + kill feed under it ---
            var mini = MkRawImage(canvas.transform, "Minimap",
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
                new Vector2(140, 44), "Restart");
            (hud.muteButton, hud.muteLabel) = MkSizedLabeledButton(canvas.transform, "Mute",
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 72),
                new Vector2(150, 44), "Sound on");
            (hud.musicButton, hud.musicLabel) = MkSizedLabeledButton(canvas.transform, "Music",
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 16),
                new Vector2(150, 44), "Music on");
            hud.touchLeft = MkButton(canvas.transform, "TouchLeft",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(170, 16),
                new Vector2(64, 44), "<");
            hud.touchRight = MkButton(canvas.transform, "TouchRight",
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-182, 16),
                new Vector2(64, 44), ">");
            hud.touchLeft.gameObject.SetActive(false);
            hud.touchRight.gameObject.SetActive(false);

            // --- overlay cards ---
            hud.overlay = MkFullscreenDim(canvas.transform, "Overlay");
            hud.joinPanel = MkPanel(hud.overlay.transform, "Join", new Vector2(480, 560));
            float jy = 232;
            MkText(hud.joinPanel.transform, "Title",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, jy),
                new Vector2(440, 60), "Territory", 46);
            jy -= 62;
            hud.blurbText = MkText(hud.joinPanel.transform, "Blurb",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, jy),
                new Vector2(420, 48), "Close a loop to take the ground inside it.", 15);
            hud.blurbText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hud.blurbText.verticalOverflow = VerticalWrapMode.Overflow;
            jy -= 72;
            MkText(hud.joinPanel.transform, "NameLabel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, jy),
                new Vector2(360, 24), "Your name", 14);
            jy -= 36;
            hud.nameInput = MkInput(hud.joinPanel.transform, "NameInput",
                new Vector2(0.5f, 0.5f), new Vector2(0, jy), new Vector2(360, 44));
            jy -= 72;
            hud.joinButton = MkButton(hud.joinPanel.transform, "JoinButton",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, jy),
                new Vector2(200, 52), "Join");
            jy -= 70;
            var hint = MkText(hud.joinPanel.transform, "Hint",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, jy),
                new Vector2(420, 44), "WASD / arrows or drag to steer - don't hit trails.", 13);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Overflow;

            hud.roomPanel = MkPanel(hud.overlay.transform, "Room", new Vector2(580, 680));
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

        // Room panel uses a vertical layout group so rows never overlap.
        private static void BuildRoomPanel(HudController hud)
        {
            var content = new GameObject("Content");
            content.transform.SetParent(hud.roomPanel.transform, false);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 8;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;

            MkText(content.transform, "NickLabel", "Your name", 14);
            hud.nickInput = MkInput(content.transform, "NickInput", new Vector2(0, 40));
            hud.steerToggle = MkToggle(content.transform, "SteerToggle", "Steer with the cursor");
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
            MkText(content.transform, "RosterLabel", "Players", 14);
            hud.rosterList = MkList(content.transform, "Roster", new Vector2(120, 4));
            hud.playerRowPrefab = MkPlayerRowTemplate(_templates, "PlayerRow");
            MkText(content.transform, "ResultsLabel", "Results", 14);
            hud.resultsList = MkList(content.transform, "Results", new Vector2(90, 4));
            hud.shareText = MkText(content.transform, "Share", "", 12);
        }

        // Place a rect by anchor + pivot + inset offset with an explicit size.
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
            t.color = Color.white;
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
            nt.font = _font; nt.fontSize = 14; nt.color = Color.white;
            nt.alignment = TextAnchor.MiddleLeft;
            var nle = nameT.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1;

            var stat = new GameObject("Stat");
            stat.transform.SetParent(go.transform, false);
            var st = stat.AddComponent<Text>();
            st.font = _font; st.fontSize = 14; st.color = Color.white;
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
            t.font = _font; t.fontSize = 13; t.color = Color.white;
            t.alignment = TextAnchor.MiddleRight;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 18;
            go.SetActive(false);
            return go;
        }

        private static Button MkButton(Transform parent, string name,
            Vector2 anchorMin, Vector2 pivot, Vector2 offset, Vector2 size, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.2f, 0.2f, 0.9f);
            var btn = go.AddComponent<Button>();
            var t = new GameObject("Label");
            t.transform.SetParent(go.transform, false);
            var txt = t.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 16; txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            Place(go.GetComponent<RectTransform>(), anchorMin, anchorMin, pivot, offset, size);
            return btn;
        }

        private static (Button, Text) MkSizedLabeledButton(Transform parent, string name,
            Vector2 anchorMin, Vector2 pivot, Vector2 offset, Vector2 size, string label)
        {
            var btn = MkButton(parent, name, anchorMin, pivot, offset, size, label);
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
            img.color = new Color(0.03f, 0.05f, 0.08f, 0.94f);
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
            img.color = new Color(0.08f, 0.1f, 0.14f);
            var input = go.AddComponent<InputField>();
            var t = new GameObject("Text");
            t.transform.SetParent(go.transform, false);
            var txt = t.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 16; txt.color = Color.white;
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
            img.color = new Color(0.2f, 0.25f, 0.3f);
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.sizeDelta = new Vector2(20, 20);
            var check = new GameObject("Checkmark");
            check.transform.SetParent(box.transform, false);
            var cimg = check.AddComponent<Image>();
            cimg.color = new Color(0.2f, 1f, 0.83f);
            var lab = new GameObject("Label");
            lab.transform.SetParent(go.transform, false);
            var txt = lab.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 14; txt.color = Color.white; txt.text = label;
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
            img.color = new Color(0.08f, 0.1f, 0.14f);
            var dd = go.AddComponent<Dropdown>();
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;

            var cap = new GameObject("Label");
            cap.transform.SetParent(go.transform, false);
            var txt = cap.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 14; txt.color = Color.white;
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
