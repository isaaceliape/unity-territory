// Generates Assets/Scenes/Main.unity headlessly:
//   Unity -batchmode -projectPath . -executeMethod Territory.Editor.SceneSetup.Generate -quit
// Every serialized reference the scripts need is wired here, so no manual
// scene work is required for the first playable WebGL build.
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
            Object.DestroyImmediate(GameObject.Find("Main Camera") == cam.gameObject ? null : null);

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
            canvas.gameObject.AddComponent<CanvasScaler>();
            canvas.gameObject.AddComponent<GraphicRaycaster>();

            // --- HUD ---
            hud.clockText = MkText(canvas.transform, "Clock", new Vector2(0.5f, 1), "3:00", 28);
            hud.statusText = MkText(canvas.transform, "Status", new Vector2(0.5f, 1), "", 16);
            hud.boardList = MkList(canvas.transform, "Board", new Vector2(0, 1), new Vector2(220, 400));
            hud.boardRowPrefab = MkPlayerRowTemplate(hud.boardList, "BoardRow");
            var mini = MkRawImage(canvas.transform, "Minimap", new Vector2(1, 1), new Vector2(168, 126));
            hud.minimap = mini;
            board.minimapView = mini;
            hud.feedList = MkList(canvas.transform, "Feed", new Vector2(1, 1), new Vector2(260, 200));
            hud.feedRowPrefab = MkFeedTemplate(hud.feedList);
            hud.restartButton = MkButton(canvas.transform, "Restart", new Vector2(0, 0), "Restart");
            (hud.muteButton, hud.muteLabel) = MkLabeledButton(canvas.transform, "Mute", new Vector2(1, 0), "Sound on", new Vector2(0, 40));
            (hud.musicButton, hud.musicLabel) = MkLabeledButton(canvas.transform, "Music", new Vector2(1, 0), "Music on", new Vector2(0, 80));
            hud.touchLeft = MkButton(canvas.transform, "TouchLeft", new Vector2(0, 0), "<");
            hud.touchRight = MkButton(canvas.transform, "TouchRight", new Vector2(1, 0), ">");
            hud.touchLeft.gameObject.SetActive(false);
            hud.touchRight.gameObject.SetActive(false);

            // --- overlay cards ---
            hud.overlay = MkPanel(canvas.transform, "Overlay");
            hud.joinPanel = MkPanel(hud.overlay.transform, "Join");
            MkText(hud.joinPanel.transform, "Title", new Vector2(0.5f, 1), "Territory", 48);
            hud.blurbText = MkText(hud.joinPanel.transform, "Blurb", new Vector2(0.5f, 1),
                "Close a loop to take the ground inside it.", 16);
            hud.nameInput = MkInput(hud.joinPanel.transform, "NameInput");
            hud.joinButton = MkButton(hud.joinPanel.transform, "JoinButton", new Vector2(0.5f, 0), "Join");
            hud.roomPanel = MkPanel(hud.overlay.transform, "Room");
            hud.roomPanel.SetActive(false);
            MkText(hud.roomPanel.transform, "NickLabel", new Vector2(0.5f, 1), "Your name", 14);
            hud.nickInput = MkInput(hud.roomPanel.transform, "NickInput");
            hud.steerToggle = MkToggle(hud.roomPanel.transform, "SteerToggle", "Steer with the cursor");
            hud.rosterList = MkList(hud.roomPanel.transform, "Roster", new Vector2(0.5f, 0.5f), new Vector2(360, 160));
            hud.resultsList = MkList(hud.roomPanel.transform, "Results", new Vector2(0.5f, 0.5f), new Vector2(360, 160));
            hud.shareText = MkText(hud.roomPanel.transform, "Share", new Vector2(0.5f, 0), "", 12);
            hud.minutesDropdown = MkDropdown(hud.roomPanel.transform, "Minutes",
                new[] { "1 minute", "2 minutes", "3 minutes", "5 minutes" }, 2);
            hud.botLevelDropdown = MkDropdown(hud.roomPanel.transform, "BotLevel",
                new[] { "easy", "normal", "hard" }, 1);
            hud.addBotButton = MkButton(hud.roomPanel.transform, "AddBot", new Vector2(0.5f, 0), "Add bot");
            hud.addBotLabel = hud.addBotButton.GetComponentInChildren<Text>();
            hud.startButton = MkButton(hud.roomPanel.transform, "Start", new Vector2(0.5f, 0), "Start");
            hud.startLabel = hud.startButton.GetComponentInChildren<Text>();
            hud.waitingText = MkText(hud.roomPanel.transform, "Waiting", new Vector2(0.5f, 0), "", 13);
            hud.errorText = MkText(hud.roomPanel.transform, "Error", new Vector2(0.5f, 0), "", 13);
            hud.playerRowPrefab = MkPlayerRowTemplate(hud.roomPanel.transform, "PlayerRow");

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

        private static Text MkText(Transform parent, string name, Vector2 anchor, string text, int size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(500, size + 14);
            return t;
        }

        private static Transform MkList(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<VerticalLayoutGroup>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            return go.transform;
        }

        private static GameObject MkPlayerRowTemplate(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<HorizontalLayoutGroup>();
            var swatch = new GameObject("Swatch");
            swatch.transform.SetParent(go.transform, false);
            var img = swatch.AddComponent<Image>();
            img.color = Color.white;
            var nameT = new GameObject("Name");
            nameT.transform.SetParent(go.transform, false);
            var nt = nameT.AddComponent<Text>();
            nt.font = _font; nt.fontSize = 14; nt.color = Color.white;
            var stat = new GameObject("Stat");
            stat.transform.SetParent(go.transform, false);
            var st = stat.AddComponent<Text>();
            st.font = _font; st.fontSize = 14; st.color = Color.white;
            go.SetActive(false);
            return go;
        }

        private static GameObject MkFeedTemplate(Transform parent)
        {
            var go = new GameObject("FeedRow");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = 13; t.color = Color.white;
            go.SetActive(false);
            return go;
        }

        private static Button MkButton(Transform parent, string name, Vector2 anchor, string label)
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
            txt.text = label;
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var brt = go.GetComponent<RectTransform>();
            brt.anchorMin = anchor; brt.anchorMax = anchor;
            brt.sizeDelta = new Vector2(140, 44);
            return btn;
        }

        private static (Button, Text) MkLabeledButton(Transform parent, string name, Vector2 anchor, string label, Vector2 offset)
        {
            var btn = MkButton(parent, name, anchor, label);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchoredPosition = offset;
            return (btn, btn.GetComponentInChildren<Text>());
        }

        private static GameObject MkPanel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.03f, 0.05f, 0.08f, 0.92f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520, 640);
            return go;
        }

        private static InputField MkInput(Transform parent, string name)
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
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
            input.textComponent = txt;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(360, 40);
            return input;
        }

        private static Toggle MkToggle(Transform parent, string name, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<HorizontalLayoutGroup>();
            var box = new GameObject("Background");
            box.transform.SetParent(go.transform, false);
            var img = box.AddComponent<Image>();
            img.color = new Color(0.2f, 0.25f, 0.3f);
            var check = new GameObject("Checkmark");
            check.transform.SetParent(box.transform, false);
            var cimg = check.AddComponent<Image>();
            cimg.color = new Color(0.2f, 1f, 0.83f);
            var lab = new GameObject("Label");
            lab.transform.SetParent(go.transform, false);
            var txt = lab.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 14; txt.color = Color.white; txt.text = label;
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
            var t = new GameObject("Label");
            t.transform.SetParent(go.transform, false);
            var txt = t.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 14; txt.color = Color.white;
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
            dd.captionText = txt;
            dd.template = trt;
            dd.ClearOptions();
            dd.AddOptions(new System.Collections.Generic.List<string>(options));
            dd.value = value;
            dd.RefreshShownValue();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220, 36);
            return dd;
        }

        private static RawImage MkRawImage(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.color = Color.white;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.sizeDelta = size;
            return img;
        }
    }
}
