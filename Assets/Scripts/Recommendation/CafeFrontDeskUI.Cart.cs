using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    // Additional part of CafeFrontDeskUI. Do not attach this file as another component.
    public sealed partial class CafeFrontDeskUI
    {
        private readonly List<MenuSelection> cart = new List<MenuSelection>();
        private bool cartRefreshPending;
#if UNITY_EDITOR
        // Temporary diagnostics; reads the shared in-memory stock without reloading its file.
        private void LogCartInventoryDebug(string stage, string details)
        {
            try
            {
                Debug.Log("[CartInventoryDebug] " + stage + " | " + details +
                    " | stock=" + CafeRuntimeServices.Inventory.GetInventoryJson(), this);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CartInventoryDebug] " + stage +
                    " | diagnostics unavailable: " + e.GetType().Name, this);
            }
        }
#endif
        private int CartCupCount
        {
            get
            {
                int total = 0;
                foreach (var entry in cart) total = checked(total + entry.Servings);
                return total;
            }
        }

        private static string CartText(string en, string zh, string ja, params object[] args)
            => CafeUiLocalization.Format(en, zh, ja, args);

        private bool KeepPendingReceiptVisible()
        {
            if (!pendingMachineProduction || currentReceipt == null) return false;
            ShowReceipt();
            return true;
        }

        private MenuSelection CanonicalCopy(MenuSelection source)
        {
            var copy = CopySelection(source);
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == copy.MenuItemId);
            if (item != null && string.IsNullOrEmpty(copy.BeanId) && item.beanIds.Length == 1)
                copy.BeanId = item.beanIds[0];
            if (string.IsNullOrEmpty(copy.MilkId)) copy.MilkId = null;
            return copy;
        }

        private static bool SameConfiguration(MenuSelection a, MenuSelection b)
            => a.MenuItemId == b.MenuItemId && a.CupSizeMl == b.CupSizeMl &&
               a.BeanId == b.BeanId && a.MilkId == b.MilkId;

        private List<MenuSelection> CopyCart()
        {
            var result = new List<MenuSelection>();
            foreach (var entry in cart) result.Add(CopySelection(entry));
            return result;
        }

        private string IncompleteSelectionMessage => CartText("Please complete your selection.", "请完成选择。", "選択を完了してください。");
        private string StockMessage => CartText("Insufficient stock.", "库存不足", "在庫不足です。");

        private string OrderFailureMessage(string reason)
        {
            if (!string.IsNullOrEmpty(reason) && reason.StartsWith("Insufficient stock", StringComparison.Ordinal))
                return StockMessage;
            return CartText("Unable to place this order. Please try again.", "暂时无法下单，请重试。", "注文できません。もう一度お試しください。");
        }

        // Read-only quote for the whole basket plus the proposed selection; never reserves stock.
        private bool CanAddSelection(MenuSelection selection, out string reason)
        {
            var candidate = CopyCart();
            candidate.Add(CanonicalCopy(selection));
            return ValidateCart(candidate, out _, out _, out reason);
        }

        private void RefreshAddToCartState(bool selected, bool busy)
        {
            if (!confirm || purchased || purchasing) return;
            var selection = reception.menuAdapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
            string reason = IncompleteSelectionMessage;
            bool complete = selected && CafeUiSelectionRules.MissingOptions(selection, item, cupChosen, beanChosen, milkChosen).Length == 0;
            bool available = complete && CanAddSelection(selection, out reason);
            confirm.interactable = available && !busy && !validationPopup;
            SetUnavailableAppearance(confirm, !available);
            if (status) status.text = busy ? CafeUiLocalization.Text("Checking the latest menu. Please wait...") : available ? "" : reason;
        }

        private void AddCurrentSelectionToCart()
        {
            if (purchasing || purchased || validationPopup || pendingMachineProduction) return;
            if (recommendation.IsBusy || (speech && speech.Active && speech.IsBusy)) return;
            var current = reception.menuAdapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == current.MenuItemId);
            var missing = CafeUiSelectionRules.MissingOptions(current, item, cupChosen, beanChosen, milkChosen);
            if (missing.Length > 0)
            {
                ShowValidationPopup(IncompleteSelectionMessage);
                return;
            }
            var entry = CanonicalCopy(current);
            var candidate = CopyCart();
            int index = candidate.FindIndex(x => SameConfiguration(x, entry));
            try
            {
                if (index < 0) candidate.Add(entry);
                else candidate[index].Servings = checked(candidate[index].Servings + entry.Servings);
            }
            catch (OverflowException)
            {
                ShowValidationPopup(CartText("Quantity is too large.", "数量过大。", "数量が多すぎます。"));
                return;
            }
            if (!ValidateCart(candidate, out _, out _, out string reason))
            {
                ShowValidationPopup(reason);
                return;
            }
            cart.Clear(); cart.AddRange(candidate);
            ShowCart();
        }

        private bool ValidateCart(IReadOnlyList<MenuSelection> entries, out int cents, out int count, out string reason)
        {
            cents = count = 0;
            if (!CafeRuntimeServices.Menu.TryGetOrderQuote(entries, out cents, out count, out reason))
            {
                reason = OrderFailureMessage(reason);
                return false;
            }
            if (!machineProduction)
            {
                reason = CartText("Production is unavailable. Please try later.", "暂时无法制作，请稍后再试。", "現在製作できません。しばらくお待ちください。");
                return false;
            }
            if (!machineProduction.CanAcceptOrder(count, out reason))
            {
                reason = count > machineProduction.OrderCapacity
                    ? CartText("Production capacity reached. Reduce the quantity.", "制作容量不足，请减少杯数。", "製作上限です。数量を減らしてください。")
                    : CartText("Production is unavailable. Please try later.", "暂时无法制作，请稍后再试。", "現在製作できません。しばらくお待ちください。");
                return false;
            }
            return true;
        }

        private string CartLineDescription(MenuSelection selection)
        {
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
            string name = item != null ? DisplayEnglish(item.name) : selection.MenuItemId;
            string bean = string.IsNullOrEmpty(selection.BeanId) ? "" : Name(selection.BeanId);
            string milk = string.IsNullOrEmpty(selection.MilkId) ? CafeUiLocalization.Text("No milk") : Name(selection.MilkId);
            return CartText("{0} / {1} ml × {2}\n{3} · {4}", "{0} / {1} 毫升 × {2}\n{3} · {4}",
                "{0} / {1} ml × {2}\n{3} · {4}", name, selection.CupSizeMl, selection.Servings, bean, milk);
        }

        private void ContinueCartShopping()
        {
            mode = "order"; browsingMenu = true; purchased = false;
            recommendation.CancelSession(); StopVoice();
            menuReturn = ShowWelcomeChoices;
            ShowDrinkMenu();
        }

        private void ChangeCartQuantity(int index, int delta)
        {
            if (purchasing || pendingMachineProduction || index < 0 || index >= cart.Count) return;
            if (delta < 0)
            {
                cart[index].Servings--;
                if (cart[index].Servings <= 0) cart.RemoveAt(index);
                ShowCart();
                return;
            }
            var candidate = CopyCart();
            try { candidate[index].Servings = checked(candidate[index].Servings + delta); }
            catch (OverflowException) { return; }
            if (!ValidateCart(candidate, out _, out _, out string reason))
            {
                ShowValidationPopup(reason); return;
            }
            cart.Clear(); cart.AddRange(candidate); ShowCart();
        }

        private void ShowCart()
        {
            if (purchasing || KeepPendingReceiptVisible()) return;
#if UNITY_EDITOR
            bool processingInventoryRefresh = cartRefreshPending && mode == "cart";
#endif
            cartRefreshPending = false;
            mode = "cart"; browsingMenu = true;
            StopVoice(); recommendation.CancelSession();
            int bodyHeight = Mathf.Max(160, cart.Count * 132);
            ActionWindow(CartText("Your order", "我的订单", "ご注文内容"), bodyHeight + 300, ContinueCartShopping);
            for (int i = 0; i < cart.Count; i++)
            {
                int row = i;
                var entry = cart[row];
                float y = 92 + row * 132;
                var panel = ChatSurface(root, "Cart line", 28, y, 744, 120, false);
                Label(panel, CartLineDescription(entry), 14, 8, 500, 70, 21);
                if (CafeRuntimeServices.Menu.TryGetPrice(entry, out var price, out _))
                    Label(panel, "CNY " + (price.totalPriceCents / 100m).ToString("0.00"), 14, 82, 320, 30, 20);
                MakeButton(panel, "−", 530, 12, 54, 42, () => ChangeCartQuantity(row, -1));
                var increase = MakeButton(panel, "+", 596, 12, 54, 42, () => ChangeCartQuantity(row, 1));
                var candidate = CopyCart();
                bool canIncrease = false;
                try
                {
                    candidate[row].Servings = checked(candidate[row].Servings + 1);
                    canIncrease = ValidateCart(candidate, out _, out _, out _);
                }
                catch (OverflowException) { }
                increase.interactable = canIncrease;
                SetUnavailableAppearance(increase, !canIncrease);
                MakeButton(panel, CartText("Remove", "删除", "削除"), 530, 66, 120, 40,
                    () => { cart.RemoveAt(row); ShowCart(); });
            }
            if (cart.Count == 0)
                Label(root, CartText("Your order is empty. Add a drink first.", "订单为空，请先添加饮品。", "注文は空です。ドリンクを追加してください。"),
                    28, 110, 744, 70, 25);

            bool valid = ValidateCart(cart, out int total, out int count, out string reason);
            // Keep the price visible even when combined stock has become insufficient.
            int displayedTotal = 0;
            bool pricesValid = true;
            try
            {
                foreach (var entry in cart)
                {
                    if (!CafeRuntimeServices.Menu.TryGetPrice(entry, out var q, out _))
                    { pricesValid = false; break; }
                    displayedTotal = checked(displayedTotal + q.totalPriceCents);
                }
            }
            catch (OverflowException) { pricesValid = false; }
            float footer = 106 + bodyHeight;
            Label(root, CartText("{0} cups · Total CNY {1}", "共 {0} 杯 · 合计 ¥{1}",
                "合計 {0} 杯 · CNY {1}", CartCupCount, pricesValid ? (displayedTotal / 100m).ToString("0.00") : "—"), 28, footer, 744, 44, 26);
            status = Label(root, cart.Count > 0 && !valid ? reason : "", 28, footer + 48, 744, 60, 19);
            MakeButton(root, CartText("Add another drink", "继续选购", "ドリンクを追加"), 28, footer + 118, 360, 56, ContinueCartShopping);
            confirm = MakeButton(root, CartText("Confirm entire order", "确认整单", "注文を確定"), 412, footer + 118, 360, 56, Purchase);
            confirm.interactable = valid;
            SetUnavailableAppearance(confirm, !valid);
#if UNITY_EDITOR
            LogCartInventoryDebug(processingInventoryRefresh ? "ShowCart refreshed" : "ShowCart opened",
                "orderAvailable=" + valid + " | reason=" + reason +
                " | confirmInteractable=" + confirm.interactable +
                " | cartRefreshPending=" + cartRefreshPending);
#endif
        }

        private void PurchaseCart()
        {
            if (mode != "cart" || purchased || purchasing || validationPopup || pendingMachineProduction) return;
            var snapshot = CopyCart();
            if (!ValidateCart(snapshot, out int total, out _, out string reason))
            {
                ShowValidationPopup(reason); return;
            }
            // Build all receipt data BEFORE the one irreversible inventory confirmation.
            var rows = new List<CafeOrderLine>();
            foreach (var selection in snapshot)
            {
                var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
                if (item == null || !CafeRuntimeServices.Menu.TryGetPrice(selection, out var price, out reason))
                { ShowValidationPopup(OrderFailureMessage(reason)); return; }
                rows.Add(new CafeOrderLine {
                    selection = CopySelection(selection), drinkName = item.name,
                    beanName = string.IsNullOrEmpty(selection.BeanId) ? "" : Name(selection.BeanId),
                    milkName = string.IsNullOrEmpty(selection.MilkId) ? "" : Name(selection.MilkId),
                    totalPriceCents = price.totalPriceCents
                });
            }
            var receipt = new CafeOrderReceipt {
                lines = rows.ToArray(), selection = CopySelection(rows[0].selection),
                drinkName = rows[0].drinkName, beanName = rows[0].beanName, milkName = rows[0].milkName,
                totalPriceCents = total
            };
            purchasing = true;
            SetPointerInteraction(false);
            bool committed = false;
            try
            {
                // One call for the ENTIRE basket, never one call per row.
                if (!CafeRuntimeServices.Menu.TryConfirmOrder(snapshot, out reason))
                {
                    if (status) status.text = OrderFailureMessage(reason);
                    return;
                }
                committed = true;
                purchased = true;
                currentReceipt = recentReceipt = receipt;
                pendingMachineProduction = true;
                cart.Clear();
                receipt.IssuePickupNumber();
                try { recommendation.NotifyConfirmedOrder(receipt); }
                catch (Exception e) { Debug.LogException(e); }
                orderCompletionRoutine = StartCoroutine(CompleteOrderWithAnimation());
            }
            finally
            {
                if (!committed)
                {
                    purchasing = false;
                    if (root && root.gameObject.activeSelf) SetPointerInteraction(true);
                }
            }
        }

        private void RenderMixedReceipt(CafeOrderReceipt receipt, Action back, bool historical)
        {
            if (receipt == null) { back?.Invoke(); return; }
            var entries = receipt.GetLines();
            float height = Mathf.Max(620, 410 + entries.Length * 108);
            ActionWindow(historical ? "Recent order" : "Order confirmed", height, historical ? back : null);
            Label(root, historical
                ? CartText("Saved order — no new purchase.", "历史订单，不会再次扣库存。", "過去の注文です。在庫は減りません。")
                : CartText("Keep this pickup number.", "请保留取餐号。", "受取番号を保管してください。"), 28, 86, 744, 38, 22);
            Label(root, receipt.pickupNumber, 28, 132, 744, 70, 52).fontStyle = FontStyle.Bold;
            for (int i = 0; i < entries.Length; i++)
                Label(root, CartLineDescription(entries[i].selection), 28, 222 + i * 108, 744, 94, 22);
            float bottom = 230 + entries.Length * 108;
            Label(root, CartText("{0} cups · Total CNY {1}", "共 {0} 杯 · 合计 ¥{1}",
                "合計 {0} 杯 · CNY {1}", receipt.TotalServings,
                (receipt.totalPriceCents / 100m).ToString("0.00")), 28, bottom, 744, 42, 25);
            if (DateTimeOffset.TryParse(receipt.confirmedAt, out var date))
                Label(root, date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 28, bottom + 46, 744, 30, 18);
            MakeButton(root, "Thanks, keep exploring", 28, height - 80, 744, 56, EndPlayerInteraction);
        }
    }
}
