using System;
using UnityEngine;
using UnityEngine.Events;

namespace VirtualCafe.Inventory
{
    /// <summary>Button-friendly selection adapter; no UI creation and no automatic confirmation.</summary>
    public sealed class CafeMenuSceneAdapter : MonoBehaviour
    {
        [Serializable] public sealed class TextEvent : UnityEvent<string> { }
        [Serializable] public sealed class BoolEvent : UnityEvent<bool> { }
        [Serializable] public sealed class ConfirmationEvent : UnityEvent<bool, string> { }

        public TextEvent PriceChanged = new TextEvent();
        public TextEvent StatusChanged = new TextEvent();
        public BoolEvent CanConfirmChanged = new BoolEvent();
        public ConfirmationEvent ConfirmationCompleted = new ConfirmationEvent();
        public TextEvent ResultChanged = new TextEvent();

        private readonly MenuSelection selection = new MenuSelection {
            MenuItemId = "black-blue-mountain", CupSizeMl = 400
        };
        private CafeMenuService menu;
        private bool confirming;
        public string PriceText { get; private set; } = "—";
        public string StatusText { get; private set; } = "尚未初始化";
        public bool CanConfirm { get; private set; }
        public string LastResult { get; private set; } = "";
        public bool LastConfirmationSucceeded { get; private set; }
        public MenuSelection CurrentSelection => new MenuSelection {
            MenuItemId = selection.MenuItemId, CupSizeMl = selection.CupSizeMl,
            BeanId = selection.BeanId, MilkId = selection.MilkId, Servings = selection.Servings
        };

        private void OnEnable() { if (Application.isPlaying) RefreshDisplay(); }
        private void OnDisable()
        {
            if (menu != null) menu.InventoryChanged -= RefreshDisplay;
            menu = null;
        }
        private void EnsureServices()
        {
            var shared = CafeRuntimeServices.Menu;
            if (ReferenceEquals(shared, menu)) return;
            if (menu != null) menu.InventoryChanged -= RefreshDisplay;
            menu = shared;
            if (isActiveAndEnabled) menu.InventoryChanged += RefreshDisplay;
        }

        public void SelectDrink(string menuItemId)
        {
            selection.MenuItemId = menuItemId;
            // Fixed-bean coffees resolve their own bean; latte starts with explicit options.
            selection.BeanId = menuItemId == "latte" ? "bean-blue-mountain" : null;
            selection.MilkId = menuItemId == "latte" ? "milk-whole" : null;
            RefreshDisplay();
        }
        public void SelectBean(string beanId) { selection.BeanId = beanId; RefreshDisplay(); }
        public void SelectMilk(string milkId) { selection.MilkId = milkId; RefreshDisplay(); }
        public void SelectCupSize(int sizeMl) { selection.CupSizeMl = sizeMl; RefreshDisplay(); }
        public void SetQuantity(int quantity) { selection.Servings = quantity; RefreshDisplay(); }

        public void RefreshDisplay()
        {
            try
            {
                EnsureServices();
                bool priced = menu.TryGetPrice(selection, out var price, out string reason);
                PriceText = priced ? FormatCents(price.totalPriceCents) : "—";
                CanConfirm = priced && menu.IsAvailable(selection, out reason);
                StatusText = CanConfirm ? "可制作" : reason;
            }
            catch (Exception e)
            {
                CanConfirm = false;
                PriceText = "—";
                StatusText = "库存初始化或查询失败：" + e.Message;
            }
            Emit(() => PriceChanged.Invoke(PriceText));
            Emit(() => StatusChanged.Invoke(StatusText));
            Emit(() => CanConfirmChanged.Invoke(CanConfirm && !confirming));
        }

        public void ConfirmCurrentSelection()
        {
            if (confirming) return; // Prevent reentrant confirmation from UI event callbacks.
            confirming = true;
            bool success = false;
            string result;
            try
            {
                EnsureServices();
                // Check again even if UI previously showed available; never trust a cached flag.
                if (!menu.TryGetPrice(selection, out var price, out result)) { }
                else
                {
                    success = menu.TryConfirmOrder(new[] { CurrentSelection }, out result);
                    if (success) result = "确认成功，库存已保存；金额 " + FormatCents(price.totalPriceCents);
                }
            }
            catch (Exception e) { result = "确认失败：" + e.Message; }
            LastConfirmationSucceeded = success;
            LastResult = result;
            // Keep the guard raised through event delivery and refresh.
            Emit(() => ResultChanged.Invoke(LastResult));
            Emit(() => ConfirmationCompleted.Invoke(success, LastResult));
            confirming = false;
            RefreshDisplay();
        }
        private static string FormatCents(int cents) => "¥" + (cents / 100) + "." + (cents % 100).ToString("D2");
        private static void Emit(Action callback)
        {
            try { callback(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
