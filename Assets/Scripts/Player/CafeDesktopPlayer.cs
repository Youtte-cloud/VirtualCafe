using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VirtualCafe.Recommendation;
namespace VirtualCafe.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class CafeDesktopPlayer : MonoBehaviour
    {
        public Camera view;
        public float walkSpeed = 2.3f;
        public float lookSensitivity = 2f;
        public bool IsSeated => seat != null;
        public bool UiMode { get; private set; }
        private CharacterController motor;
        private CafeFrontDeskUI terminal;
        private CafeSeat seat, nearbySeat;
        private Vector3 entryPosition;
        private float pitch, verticalSpeed;
        private string notice = "";
        private Font font;
        public CharacterController Motor => motor;
        private void Start()
        {
            motor = GetComponent<CharacterController>(); motor.height = 1.8f; motor.radius = .22f;
            motor.center = new Vector3(0, .9f, 0); motor.stepOffset = .25f; motor.skinWidth = .025f;
            terminal = FindObjectOfType<CafeFrontDeskUI>();
            view.transform.localPosition = Vector3.up * 1.65f;
            view.transform.localRotation = Quaternion.identity;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 18);
            SetUiMode(false);
        }
        private bool Typing()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            var field = selected ? selected.GetComponent<InputField>() : null;
            return field && field.isFocused;
        }
        public void SetUiMode(bool value)
        {
            UiMode = value;
            Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = value;
            if (terminal) terminal.SetPointerInteraction(value);
            if (!value && EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool nextMode = !UiMode;
                if (!nextMode && terminal) terminal.EndPlayerInteraction();
                SetUiMode(nextMode); return;
            }
            if (Typing()) return;
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (UiMode) { if (terminal) terminal.EndPlayerInteraction(); SetUiMode(false); }
                else if (!IsSeated && terminal && terminal.TryOpenInteraction())
                {
                    SetUiMode(true);
                    return; // Do not apply movement or mouse-look on the opening frame.
                }
            }
            if (UiMode) return;
            float dx = Input.GetAxis("Mouse X") * lookSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -75, 75);
            transform.Rotate(0, dx, 0); view.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            nearbySeat = FindNearbySeat();
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (IsSeated) TryStand(); else if (nearbySeat) TrySit(nearbySeat);
            }
            if (!IsSeated) Move(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), Time.deltaTime);
        }
        public void Move(Vector2 input, float dt)
        {
            if (!motor || !motor.enabled || IsSeated || UiMode) return;
            if (motor.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed += Physics.gravity.y * dt;
            Vector3 direction = transform.right * input.x + transform.forward * input.y;
            if (direction.sqrMagnitude > 1) direction.Normalize();
            motor.Move((direction * walkSpeed + Vector3.up * verticalSpeed) * dt);
        }
        public void FacePoint(Vector3 point)
        {
            Vector3 direction = point - view.transform.position;
            if (direction.sqrMagnitude < .001f) return;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -75, 75);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            view.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
        }
        private CafeSeat FindNearbySeat()
        {
            CafeSeat best = null; float distance = 1.7f;
            foreach (var candidate in FindObjectsOfType<CafeSeat>())
            {
                if (candidate.Occupied) continue;
                Vector3 delta = candidate.EyePosition - view.transform.position; delta.y = 0;
                if (delta.magnitude < distance && Vector3.Dot(view.transform.forward, delta.normalized) > .2f)
                { distance = delta.magnitude; best = candidate; }
            }
            return best;
        }
        public bool TrySit(CafeSeat target)
        {
            if (!target || target.Occupied || IsSeated || UiMode || Vector3.Distance(view.transform.position, target.EyePosition) > 2) return false;
            Vector3 sight = target.EyePosition - view.transform.position;
            foreach (var hit in Physics.RaycastAll(view.transform.position, sight.normalized, sight.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.transform == target.transform || hit.transform.IsChildOf(target.transform)) continue;
                return false;
            }
            entryPosition = transform.position;
            motor.enabled = false; seat = target; seat.Occupied = true;
            transform.position = target.EyePosition - Vector3.up * 1.65f;
            transform.rotation = Quaternion.LookRotation(target.Forward, Vector3.up);
            pitch = 0; view.transform.localRotation = Quaternion.identity; verticalSpeed = 0; notice = "";
            return true;
        }
        public bool TryStand()
        {
            if (!IsSeated) return false;
            Vector3 center = seat.EyePosition; center.y = entryPosition.y;
            Vector3 right = Vector3.Cross(Vector3.up, seat.Forward);
            Vector3[] choices = { entryPosition, center + seat.Forward * .9f, center + right * .9f, center - right * .9f, center - seat.Forward * .9f };
            foreach (var candidate in choices)
            {
                if (Physics.CheckCapsule(candidate + Vector3.up * .27f, candidate + Vector3.up * 1.56f, .215f, ~0, QueryTriggerInteraction.Ignore)) continue;
                // A safe standing point also needs a floor, not empty space beyond the room.
                if (!Physics.Raycast(candidate + Vector3.up * .3f, Vector3.down, .65f, ~0, QueryTriggerInteraction.Ignore)) continue;
                transform.position = candidate; seat.Occupied = false; seat = null;
                motor.enabled = true; verticalSpeed = 0; notice = ""; return true;
            }
            notice = "旁边暂时没有足够的起身空间，请稍后再试。"; return false;
        }
        private void OnApplicationFocus(bool focus) { if (!focus) SetUiMode(true); }
        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (seat) { seat.Occupied = false; seat = null; }
        }
        private void OnDestroy() { if (font) Destroy(font); }
        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box) { font = font, fontSize = 18, alignment = TextAnchor.MiddleCenter };
            style.wordWrap = true;
            string hint = UiMode ? "鼠标操作 · Esc 继续行走" : IsSeated ? "E 起身 · 鼠标环顾" : nearbySeat ? "E 坐下 · WASD 移动" : "WASD 移动 · 鼠标环顾 · 靠近前台点单";
            if (!UiMode && terminal && terminal.CanOpenInteraction)
                hint = "F 与店员交谈 · WASD 移动";
            float width = Mathf.Min(440, Screen.width - 20);
            GUI.Box(new Rect(10, Screen.height - 65, width, 55), string.IsNullOrEmpty(notice) ? hint : notice, style);
            if (!UiMode) GUI.Label(new Rect(Screen.width / 2 - 5, Screen.height / 2 - 10, 20, 20), "+");
        }
    }
}
