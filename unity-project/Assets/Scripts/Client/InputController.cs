// Steering input: keyboard and on-screen hold buttons feed the same
// held-turn state (mirroring heldTurn/sendTurn in client.js), and the
// cursor-steer toggle sends aim angles. WASD note: W/S do not steer,
// exactly like the browser game.
using UnityEngine;
using UnityEngine.UI;

namespace Territory.Client.Unity
{
    public sealed class InputController : MonoBehaviour
    {
        public GameClient client;
        public Button touchLeft;
        public Button touchRight;

        private bool _heldLeft;
        private bool _heldRight;
        private double? _lastAim;

        void Awake()
        {
            if (client == null) client = FindObjectOfType<GameClient>();
            BindHoldButton(touchLeft, true);
            BindHoldButton(touchRight, false);
        }

        private bool CursorSteer => PlayerPrefs.GetInt("territory-steer", 0) == 1;

        private bool Active =>
            client.myId != null && (client.phase == "play" || client.phase == "countdown");

        private void BindHoldButton(Button button, bool left)
        {
            if (button == null) return;
            var trigger = button.gameObject.AddComponent<HoldTrigger>();
            trigger.Pressed += () => SetHeld(left, true);
            trigger.Released += () => SetHeld(left, false);
        }

        private void SetHeld(bool left, bool held)
        {
            if (left) _heldLeft = held; else _heldRight = held;
            if (Active) client.Steer(ViewHelpers.TurnFromHeld(_heldLeft, _heldRight));
        }

        private static bool IsLeft(KeyCode key) =>
            key == KeyCode.LeftArrow || key == KeyCode.A;

        private static bool IsRight(KeyCode key) =>
            key == KeyCode.RightArrow || key == KeyCode.D;

        void Update()
        {
            foreach (KeyCode key in new[] { KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.A, KeyCode.D })
            {
                if (Input.GetKeyDown(key))
                {
                    if (IsLeft(key)) _heldLeft = true; else _heldRight = true;
                    if (Active && !CursorSteer)
                        client.Steer(ViewHelpers.TurnFromHeld(_heldLeft, _heldRight));
                }
                if (Input.GetKeyUp(key))
                {
                    if (IsLeft(key)) _heldLeft = false; else _heldRight = false;
                    if (Active && !CursorSteer)
                        client.Steer(ViewHelpers.TurnFromHeld(_heldLeft, _heldRight));
                }
            }
            SteerTowardCursor();
        }

        private void SteerTowardCursor()
        {
            if (!CursorSteer || !Active) return;
            if (client.myId == null) return;
            if (!client.model.Bodies.TryGetValue(client.myId.Value, out var me) || !me.Alive) return;
            BoardModel.Pose(me, Time.unscaledTime * 1000, client.tickMs, out double hx, out double hy);
            Vector3 mouse = Input.mousePosition;
            Ray ray = client.board.worldCamera.ScreenPointToRay(mouse);
            // Board plane is z=0.
            if (Mathf.Abs(ray.direction.z) < 1e-6) return;
            float t = -ray.origin.z / ray.direction.z;
            Vector3 hit = ray.origin + ray.direction * t;
            double dx = hit.x - hx, dy = -(hit.y) - hy;
            if (System.Math.Sqrt(dx * dx + dy * dy) < 0.8) return;
            double angle = System.Math.Atan2(dy, dx);
            if (_lastAim != null && System.Math.Abs(angle - _lastAim.Value) < 0.08) return;
            _lastAim = angle;
            client.Aim(angle);
        }
    }

    // Pointer press/release without the Button click delay.
    public sealed class HoldTrigger : MonoBehaviour,
        UnityEngine.EventSystems.IPointerDownHandler,
        UnityEngine.EventSystems.IPointerUpHandler,
        UnityEngine.EventSystems.IPointerExitHandler
    {
        public event System.Action Pressed = delegate { };
        public event System.Action Released = delegate { };

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData) => Pressed();
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData) => Released();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => Released();
    }
}
