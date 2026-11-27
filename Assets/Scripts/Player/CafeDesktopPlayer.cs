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
        private CafeTableDrinkArea seatedDrinkTable;
        private Vector3 entryPosition;
        private float pitch, verticalSpeed;
        private int resumeLookAfterFrame;
        private bool hasFocus = true;
        private string notice = "";
        private Font font;
        private Canvas controlsCanvas;
        private Text controlsText;
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
            if (!value && terminal && terminal.IsPreparingOrder) return;
            if (UiMode && !value) resumeLookAfterFrame = Time.frameCount + 1;
            UiMode = value;
            ReleasePointer();
            if (terminal) terminal.SetPointerInteraction(value && !terminal.IsPreparingOrder);
            if (!value && EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
        private static void ReleasePointer()
        {
            // A cafe is a point-and-click experience: closing a window must not hide the pointer.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        private void Update()
        {
            if (!terminal) terminal = FindObjectOfType<CafeFrontDeskUI>();
            bool interactionVisible = terminal && (terminal.IsInterfaceVisible || terminal.IsPreparingOrder);
            if (UiMode != interactionVisible) SetUiMode(interactionVisible);
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible) ReleasePointer();
            UpdateControlsHint();
            if (!hasFocus) return;
            // Preparation owns the camera and input until the order animation finishes.
            if (terminal && terminal.IsPreparingOrder) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (terminal && terminal.IsInterfaceVisible) terminal.EndPlayerInteraction();
                SetUiMode(false); return;
            }
            if (Typing()) return;
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (UiMode) { if (terminal) terminal.EndPlayerInteraction(); SetUiMode(false); return; }
                if (!IsSeated && terminal && terminal.TryOpenInteraction())
                {
                    SetUiMode(true); return;
                }
            }
            if (UiMode) return;
            if (IsSeated && seatedDrinkTable && seatedDrinkTable.IsDrinking) return;
            if (IsSeated && seatedDrinkTable && view)
            {
                bool overUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
                seatedDrinkTable.UpdateDrinkPointer(view.ScreenPointToRay(Input.mousePosition), !overUi && Input.GetMouseButtonDown(0));
            }
            if (IsSeated && Input.GetKeyDown(KeyCode.G) && seatedDrinkTable)
            {
                if (seatedDrinkTable.TryDrink(view)) { UpdateControlsHint(); return; }
            }
            // Free look follows ordinary mouse movement, while the pointer remains visible.
            // Do not rotate on the same click that selects a world object or closes a window.
            if (Time.frameCount > resumeLookAfterFrame && !Input.GetMouseButtonDown(0))
            {
                float dx = Input.GetAxis("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -75, 75);
                transform.Rotate(0, dx, 0); view.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            nearbySeat = FindNearbySeat();
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (IsSeated) TryStand(); else if (nearbySeat) TrySit(nearbySeat);
            }
            if (!IsSeated) Move(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), Time.deltaTime);
        }
        public void Move(Vector2 input, float dt)
        {
            if (!motor || !motor.enabled || IsSeated || UiMode || (terminal && terminal.IsPreparingOrder)) return;
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

        private CafeTableDrinkArea FindTableForSeat(CafeSeat target)
        {
            CafeTableDrinkArea bestTable = null;
            float bestDistance = 1.8f;

            Vector3 forward = target.Forward;
            forward.y = 0f;
            forward.Normalize();

            foreach (var area in FindObjectsOfType<CafeTableDrinkArea>())
            {
                if (!area.isActiveAndEnabled)
                    continue;

                Vector3 offset =
                    area.transform.position - target.EyePosition;

                offset.y = 0f;

                float distance = offset.magnitude;

                if (distance >= bestDistance)
                    continue;

                if (distance > 0.01f &&
                    Vector3.Dot(forward, offset.normalized) <= 0.2f)
                {
                    continue;
                }

                bestTable = area;
                bestDistance = distance;
            }

            return bestTable;
        }
        private void PlaceCarriedDrinksOnTable(CafeSeat target)
        {
            if (!target)
                return;

            var production = FindObjectOfType<CafeMachineProduction>();

            var carried = production
                ? production.carriedDrinks
                : null;

            if (!carried)
                carried = FindObjectOfType<CafeCarriedDrinks>();

            if (!carried || !carried.HasDrinks)
                return;

            var bestTable = seatedDrinkTable;

            if (!bestTable)
            {
                CafeCapacityNotice.Show(
                    "No table placement area was found. Your drinks remain in your carried orders.",
                    "未找到桌面放杯位置，饮品仍保留在随身订单中。",
                    "飲み物を置く場所が見つかりません。注文はそのまま持っています。");

                return;
            }

            bool placed = carried.TryPlaceOn(bestTable);

            // 完全放不下，或只放下部分订单、仍有订单随身携带。
            if (!placed || carried.HasDrinks)
            {
                CafeCapacityNotice.Show(
                    "There is not enough space on the table. The remaining drinks stay in your carried orders.",
                    "桌子位置不足，饮品仍保留在随身订单中。",
                    "テーブルの空きが足りません。残りの飲み物はそのまま持っています。");
            }
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
            pitch = 0;
            view.transform.localRotation = Quaternion.identity;
            verticalSpeed = 0;
            notice = "";

            seatedDrinkTable = FindTableForSeat(target);
            if (seatedDrinkTable) seatedDrinkTable.ClearDrinkSelection();
            PlaceCarriedDrinksOnTable(target);

            return true;
        }
        public bool TryStand()
        {
            if (!IsSeated || (seatedDrinkTable && seatedDrinkTable.IsDrinking)) return false;
            Vector3 center = seat.EyePosition; center.y = entryPosition.y;
            Vector3 right = Vector3.Cross(Vector3.up, seat.Forward);
            Vector3[] choices = { entryPosition, center + seat.Forward * .9f, center + right * .9f, center - right * .9f, center - seat.Forward * .9f };
            foreach (var candidate in choices)
            {
                if (Physics.CheckCapsule(candidate + Vector3.up * .27f, candidate + Vector3.up * 1.56f, .215f, ~0, QueryTriggerInteraction.Ignore)) continue;
                // A safe standing point also needs a floor, not empty space beyond the room.
                if (!Physics.Raycast(candidate + Vector3.up * .3f, Vector3.down, .65f, ~0, QueryTriggerInteraction.Ignore)) continue;
                transform.position = candidate; seat.Occupied = false; seat = null;
                if (seatedDrinkTable) seatedDrinkTable.ClearDrinkSelection(); seatedDrinkTable = null; motor.enabled = true; verticalSpeed = 0; notice = ""; return true;
            }
            notice = "旁边暂时没有足够的起身空间，请稍后再试。"; return false;
        }
        private void OnApplicationFocus(bool focus)
        {
            hasFocus = focus;
            ReleasePointer();
            if (focus)
                SetUiMode(terminal && (terminal.IsInterfaceVisible || terminal.IsPreparingOrder));
        }
        private void OnDisable()
        {
            if (controlsCanvas) controlsCanvas.enabled = false;
            ReleasePointer();
            if (seatedDrinkTable) seatedDrinkTable.CancelDrinking();
            seatedDrinkTable = null;
            if (seat) { seat.Occupied = false; seat = null; }
        }
        private void OnDestroy() { if (controlsCanvas) Destroy(controlsCanvas.gameObject); if (font) Destroy(font); }
        private void UpdateControlsHint()
        {
            if (!controlsCanvas)
            {
                var hud = new GameObject("Player controls HUD", typeof(Canvas), typeof(CanvasScaler));
                controlsCanvas = hud.GetComponent<Canvas>();
                controlsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                controlsCanvas.sortingOrder = 32000;
                var scale = hud.GetComponent<CanvasScaler>();
                scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scale.referenceResolution = new Vector2(1280, 720);
                scale.matchWidthOrHeight = .5f;
                var box = new GameObject("Controls", typeof(RectTransform), typeof(Image));
                box.transform.SetParent(hud.transform, false);
                var rect = box.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(16, 14); rect.sizeDelta = new Vector2(600, 48);
                box.GetComponent<Image>().color = new Color(.15f,.12f,.09f,.88f);
                box.GetComponent<Image>().raycastTarget = false;
                var label = new GameObject("Hint", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(box.transform, false); controlsText = label.GetComponent<Text>();
                var t = controlsText.rectTransform;t.anchorMin=Vector2.zero;t.anchorMax=Vector2.one;
                t.offsetMin=new Vector2(14,4);t.offsetMax=new Vector2(-14,-4);
                controlsText.font=font;controlsText.fontSize=17;controlsText.color=new Color(1,.97f,.90f);
                controlsText.alignment=TextAnchor.MiddleLeft;controlsText.raycastTarget=false;
            }
            controlsCanvas.enabled = isActiveAndEnabled;
            string hint = UiMode ? "Mouse: select / Esc: close and walk" :
                IsSeated ? "E: stand up / Mouse: look around" :
                nearbySeat ? "E: sit / WASD: move / Mouse: look" :
                "WASD: move / Mouse: look / Click: inspect";
            if (!UiMode && terminal && terminal.CanOpenInteraction)
                hint = "F: talk / WASD: move / Mouse: look";
            if (!UiMode && terminal && !string.IsNullOrEmpty(terminal.HoveredCupName))
                hint = CafeUiLocalization.Format("Click: learn about {0}", "点击：了解{0}", "クリック：{0}について聞く", terminal.HoveredCupName);
            if (!UiMode && IsSeated && seatedDrinkTable)
            {
                if (seatedDrinkTable.IsDrinking)
                    hint = CafeUiLocalization.Format("Drinking coffee... {0}%", "正在饮用咖啡……{0}%", "コーヒーを飲んでいます… {0}%", Mathf.RoundToInt(seatedDrinkTable.DrinkProgress * 100));
                else if (seatedDrinkTable.CanDrink)
                    hint = CafeUiLocalization.Format("{0} / G: drink / E: stand up", "{0} / G：饮用 / E：起身", "{0} / G：飲む / E：立つ", seatedDrinkTable.SelectedDrinkName);
                else if (!seatedDrinkTable.IsPlacing && seatedDrinkTable.DrinkCount > 0)
                    hint = CafeUiLocalization.Format("Click a cup, then press G / E: stand up", "点击桌上咖啡选中，再按G饮用 / E：起身", "カップをクリックしてGで飲む / E：立つ");
            }
            if (terminal && terminal.IsPreparingOrder) hint = "Preparing your drink... Please wait.";
            controlsText.text = CafeUiLocalization.Text(string.IsNullOrEmpty(notice) ? hint : "Not enough room to stand. Please try again.");
        }
    }
}
