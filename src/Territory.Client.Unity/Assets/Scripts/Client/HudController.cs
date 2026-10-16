// All 2D UI: cards (join/room/results), scoreboard, clock, minimap,
// kill feed, buttons, and touch controls. Mirrors the DOM in index.html
// and the paint/showCard logic in client.js.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Territory.Client.Unity
{
    public sealed class HudController : MonoBehaviour
    {
        public GameClient client;
        public BoardRenderer board;

        [Header("HUD")]
        public Text clockText;
        public Text statusText;
        public Transform boardList;
        public GameObject boardRowPrefab;
        public RawImage minimap;
        public Transform feedList;
        public GameObject feedRowPrefab;
        public Button restartButton;
        public Button muteButton;
        public Text muteLabel;
        public Button musicButton;
        public Text musicLabel;
        public Button touchLeft;
        public Button touchRight;

        [Header("Cards")]
        public GameObject overlay;
        public GameObject joinPanel;
        public InputField nameInput;
        public Button joinButton;
        public GameObject roomPanel;
        public InputField nickInput;
        public Toggle steerToggle;
        public Transform rosterList;
        public Transform resultsList;
        public GameObject playerRowPrefab;
        public Dropdown minutesDropdown;
        public Dropdown botLevelDropdown;
        public Button addBotButton;
        public Text addBotLabel;
        public Button startButton;
        public Text startLabel;
        public Text waitingText;
        public Text errorText;
        public Text blurbText;
        public Text shareText;

        private readonly Dictionary<int, GameObject> _boardRows = new Dictionary<int, GameObject>();
        private readonly List<GameObject> _feedRows = new List<GameObject>();
        private string _resultsKey = "";

        void Awake()
        {
            if (client == null) client = FindObjectOfType<GameClient>();
            joinButton.onClick.AddListener(() => client.Join(nameInput.text.Trim()));
            startButton.onClick.AddListener(() => client.StartRound(MinutesValue()));
            restartButton.onClick.AddListener(() => client.Restart());
            addBotButton.onClick.AddListener(() => client.AddBot(BotLevelValue()));
            muteButton.onClick.AddListener(() => { client.sfx.ToggleMuted(); Refresh(); });
            musicButton.onClick.AddListener(() => { client.sfx.ToggleMusicMuted(); Refresh(); });
            nickInput.onEndEdit.AddListener(OnNickEndEdit);
            steerToggle.isOn = PlayerPrefs.GetInt("territory-steer", 0) == 1;
            steerToggle.onValueChanged.AddListener(v =>
            {
                PlayerPrefs.SetInt("territory-steer", v ? 1 : 0);
                PlayerPrefs.Save();
            });
            nameInput.text = PlayerPrefs.GetString("territory-name", "");
        }

        private int MinutesValue()
        {
            string[] options = { "1", "2", "3", "5" };
            int i = Mathf.Clamp(minutesDropdown.value, 0, options.Length - 1);
            return int.Parse(options[i]);
        }

        private string BotLevelValue()
        {
            string[] options = { "easy", "normal", "hard" };
            return options[Mathf.Clamp(botLevelDropdown.value, 0, options.Length - 1)];
        }

        private void OnNickEndEdit(string value)
        {
            string name = value.Trim();
            if (name == "") return;
            client.Rename(name);
        }

        public void Refresh()
        {
            muteLabel.text = client.sfx.IsMuted ? "Sound off" : "Sound on";
            musicLabel.text = client.sfx.IsMusicMuted ? "Music off" : "Music on";
            errorText.text = client.lastError;
        }

        public void Feed(string text, string colorHtml)
        {
            GameObject row;
            if (_feedRows.Count >= 4)
            {
                row = _feedRows[0];
                _feedRows.RemoveAt(0);
                Destroy(row);
            }
            row = Instantiate(feedRowPrefab, feedList);
            row.SetActive(true);
            row.GetComponent<Text>().text = text;
            _feedRows.Add(row);
            Destroy(row, 5f);
        }

        public void ShowCard(List<CardPlayer> roster, CardScores scores)
        {
            overlay.SetActive(true);
            bool ended = scores != null;
            joinPanel.SetActive(false);
            roomPanel.SetActive(true);
            blurbText.text = ended ? ViewHelpers.WinnerText(scores) : RulesBlurb();
            bool host = client.myId != null && client.myId == client.hostId;
            addBotButton.gameObject.SetActive(host && !ended);
            startButton.interactable = host;
            startLabel.text = host ? (ended ? "Play again" : "Start") : "Waiting for the host";
            minutesDropdown.gameObject.SetActive(host && !ended);
            RenderList(rosterList, roster ?? new List<CardPlayer>(), false);
            RenderList(resultsList, ended ? scores.List : new List<CardPlayer>(), true);
            waitingText.text = ended ? "" : host
                ? "You are the host. Start when everyone is in."
                : "";
        }

        public void HideCard() => overlay.SetActive(false);

        private static string RulesBlurb() =>
            "Close a loop to take the ground inside it. Cross a line and that player is out. " +
            "Most ground when the clock stops wins. Move with WASD or the arrow keys.";

        private void RenderList(Transform parent, List<CardPlayer> players, bool scored)
        {
            foreach (Transform child in parent) Destroy(child.gameObject);
            foreach (var p in players)
            {
                var row = Instantiate(playerRowPrefab, parent);
                row.SetActive(true);
                var texts = row.GetComponentsInChildren<Text>();
                string tags = p.Id == client.myId ? " (you)" : "";
                if (p.Id == client.hostId && !scored) tags += " (host)";
                if (p.Bot) tags += $" ({ViewHelpers.BotLabel(p.Level)})";
                string wins = ViewHelpers.WinsLabel(client.sessionWins, p.Name);
                texts[0].text = $"{p.Name}{tags}{(wins != "" ? $"  {wins}" : "")}";
                texts[1].text = scored ? $"{p.Pct:F1}%" : "";
                var img = row.GetComponentInChildren<Image>();
                if (img != null && ColorUtility.TryParseHtmlString(p.Color, out var col)) img.color = col;
            }
        }

        // Called on every full/tick message.
        public void Paint(JObject msg, BoardModel model, int? myId, int? hostId, string phase)
        {
            double msLeft = msg["msLeft"]?.Value<double>() ?? 0;
            bool counting = phase == "countdown";
            clockText.text = counting
                ? System.Math.Max(1, (long)System.Math.Ceiling(msLeft / 1000)).ToString()
                : ViewHelpers.FormatClock(msLeft);
            var players = new List<BodyState>();
            var arr = msg["players"] as JArray;
            if (arr != null) foreach (JObject p in arr) players.Add(GameClient.ParseBody(p));
            PaintBoard(BodyStateToCards(players, model));
            statusText.text = model.StatusText(myId, phase);
            bool mineAlive = false;
            if (myId != null && model.Bodies.TryGetValue(myId.Value, out var me)) mineAlive = me.Alive;
            restartButton.gameObject.SetActive(myId != null && myId == hostId
                && (phase == "play" || phase == "countdown"));
            bool touch = mineAlive && (phase == "play" || phase == "countdown")
                && SystemInfo.deviceType == DeviceType.Handheld;
            touchLeft.gameObject.SetActive(touch);
            touchRight.gameObject.SetActive(touch);
            Refresh();
        }

        private static List<CardPlayer> BodyStateToCards(List<BodyState> bodies, BoardModel model)
        {
            var cards = new List<CardPlayer>();
            foreach (var b in bodies)
            {
                cards.Add(new CardPlayer
                {
                    Id = b.Id, Name = b.Name, Color = b.Color, Alive = b.Alive,
                    Land = b.Land, Kills = b.Kills,
                    Pct = model.W > 0 ? (b.Land / (double)(model.W * model.H)) * 100 : 0,
                });
            }
            cards.Sort((a, b2) =>
            {
                int c = b2.Land.CompareTo(a.Land);
                if (c != 0) return c;
                c = b2.Kills.CompareTo(a.Kills);
                return c != 0 ? c : string.Compare(a.Name, b2.Name, System.StringComparison.Ordinal);
            });
            return cards;
        }

        private void PaintBoard(List<CardPlayer> rows)
        {
            var seen = new HashSet<int>();
            foreach (var p in rows)
            {
                seen.Add(p.Id);
                if (!_boardRows.TryGetValue(p.Id, out var row))
                {
                    row = Instantiate(boardRowPrefab, boardList);
                    _boardRows[p.Id] = row;
                }
                var texts = row.GetComponentsInChildren<Text>();
                texts[0].text = p.Id == client.myId ? $"{p.Name} (you)" : p.Name;
                texts[1].text = $"{p.Pct:F1}%";
                row.SetActive(true);
            }
            foreach (var kv in new Dictionary<int, GameObject>(_boardRows))
            {
                if (seen.Contains(kv.Key)) continue;
                Destroy(kv.Value);
                _boardRows.Remove(kv.Key);
            }
            int i = 0;
            foreach (var p in rows)
            {
                _boardRows[p.Id].transform.SetSiblingIndex(i++);
            }
        }
    }
}
