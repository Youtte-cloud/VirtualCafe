using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualCafe.Inventory
{
    [Serializable] public sealed class CafeMenu
    {
        public int blackCoffeeWaterIceFeeCents;
        public int largeCupSurchargeCents;
        public CafeMenuItem[] items;
    }
    [Serializable] public sealed class CafeMenuItem
    {
        public string id;
        public string name;
        public string recipeId;
        public string[] beanIds;
        public string[] milkIds;
        public int[] cupSizesMl;
    }
    [Serializable] public sealed class MenuSelection
    {
        public string MenuItemId;
        public int CupSizeMl;
        // May be omitted for fixed-bean black coffees; required for latte.
        public string BeanId;
        public string MilkId;
        public int Servings = 1;
    }
    [Serializable] public sealed class PriceQuote
    {
        public int beanCents;
        public int milkCents;
        public int waterIceCents;
        public int sizeSurchargeCents;
        public int unitPriceCents;
        public int totalPriceCents;
    }
    [Serializable] public sealed class MenuOffer
    {
        public MenuSelection selection;
        public PriceQuote price;
        public bool available;
        public string reason;
    }
    [Serializable] public sealed class MenuRecommendationSnapshot
    {
        public string usage = "Read-only recommendations; availability is a snapshot. Revalidate whole order on confirmation.";
        public CafeMenu menu;
        public RecommendationSnapshot inventory;
        public MenuOffer[] offers;
    }

    // Use the same InventoryService instance for queries, confirmation and notifications.
    public sealed class CafeMenuService
    {
        private readonly InventoryService inventory;
        private readonly CafeMenu menu;
        public event Action InventoryChanged
        {
            add { inventory.InventoryChanged += value; }
            remove { inventory.InventoryChanged -= value; }
        }
        public CafeMenuService(InventoryService inventory)
            : this(inventory, LoadMenu()) { }
        private static string LoadMenu()
        {
            var asset = Resources.Load<TextAsset>("Cafe/menu");
            if (asset == null) throw new InvalidOperationException("Missing Resources/Cafe/menu.json");
            return asset.text;
        }
        public CafeMenuService(InventoryService inventory, string menuJson)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            menu = JsonUtility.FromJson<CafeMenu>(menuJson);
            if (menu == null || menu.items == null || menu.blackCoffeeWaterIceFeeCents < 0 || menu.largeCupSurchargeCents < 0)
                throw new ArgumentException("Invalid menu JSON.");
        }
        public CafeMenu GetMenu() => JsonUtility.FromJson<CafeMenu>(JsonUtility.ToJson(menu, false));

        private bool TryResolve(MenuSelection selection, out DrinkRequest drink, out string reason)
        {
            drink = null;
            reason = "菜单饮品、杯型、豆奶选项或数量无效。";
            if (selection == null || selection.Servings <= 0) return false;
            var item = Array.Find(menu.items, i => i.id == selection.MenuItemId);
            if (item == null || Array.IndexOf(item.cupSizesMl, selection.CupSizeMl) < 0) return false;
            string bean = selection.BeanId;
            if (string.IsNullOrEmpty(bean) && item.beanIds.Length == 1) bean = item.beanIds[0];
            if (Array.IndexOf(item.beanIds, bean) < 0 ||
                (item.milkIds.Length == 0 ? !string.IsNullOrEmpty(selection.MilkId) : Array.IndexOf(item.milkIds, selection.MilkId) < 0)) return false;
            drink = new DrinkRequest { RecipeId = item.recipeId, CupSizeMl = selection.CupSizeMl,
                BeanId = bean, MilkId = selection.MilkId, Servings = selection.Servings };
            return true;
        }

        // Price/animation queries work even when ingredients are currently sold out.
        public bool TryGetRecipe(MenuSelection selection, out DrinkRecipe recipe, out string reason)
        {
            recipe = null;
            return TryResolve(selection, out var drink, out reason) && inventory.TryGetRecipe(drink, out recipe, out reason);
        }
        public bool IsAvailable(MenuSelection selection, out string reason) =>
            TryResolve(selection, out var drink, out reason) && inventory.CanMake(drink, out reason);

        public bool TryGetPrice(MenuSelection selection, out PriceQuote quote, out string reason)
        {
            quote = null;
            if (!TryResolve(selection, out var drink, out reason) || !inventory.TryGetRecipe(drink, out var recipe, out reason)) return false;
            try
            {
                var result = new PriceQuote();
                foreach (var requirement in recipe.requirements)
                {
                    var definition = inventory.GetItemDefinition(requirement.source == "bean" ? drink.BeanId : drink.MilkId);
                    // All supported amounts produce exact cents. Reject fractional-cent configuration instead of rounding silently.
                    int numerator = checked(requirement.quantity * definition.priceUnitCents);
                    if (definition.priceUnitQuantity <= 0 || definition.priceUnitCents < 0 || numerator % definition.priceUnitQuantity != 0)
                    { reason = "计价参数无效或无法精确到分。"; return false; }
                    int cents = numerator / definition.priceUnitQuantity;
                    if (requirement.source == "bean") result.beanCents = checked(result.beanCents + cents);
                    else result.milkCents = checked(result.milkCents + cents);
                }
                result.waterIceCents = drink.RecipeId == "black-coffee" ? menu.blackCoffeeWaterIceFeeCents : 0;
                result.sizeSurchargeCents = drink.CupSizeMl == 500 ? menu.largeCupSurchargeCents : 0;
                result.unitPriceCents = checked(result.beanCents + result.milkCents + result.waterIceCents + result.sizeSurchargeCents);
                result.totalPriceCents = checked(result.unitPriceCents * drink.Servings);
                quote = result;
                reason = "成功";
                return true;
            }
            catch (OverflowException) { reason = "金额超出整数分范围。"; return false; }
        }

        // Thin integration boundary, not a cart/payment system. Inventory rechecks all lines together.
        public bool TryConfirmOrder(IReadOnlyList<MenuSelection> selections, out string reason)
        {
            reason = "订单不能为空。";
            if (selections == null || selections.Count == 0) return false;
            var drinks = new List<DrinkRequest>();
            foreach (var selection in selections)
            {
                if (!TryResolve(selection, out var drink, out reason)) return false;
                drinks.Add(drink);
            }
            return inventory.TryConfirmOrder(drinks, out reason);
        }

        public MenuOffer[] GetMenuOffers()
        {
            var offers = new List<MenuOffer>();
            foreach (var item in menu.items)
                foreach (int size in item.cupSizesMl)
                    foreach (string bean in item.beanIds)
                        foreach (string milk in item.milkIds.Length == 0 ? new string[] { null } : item.milkIds)
                        {
                            var selection = new MenuSelection { MenuItemId = item.id, CupSizeMl = size, BeanId = bean, MilkId = milk };
                            if (!TryGetPrice(selection, out var price, out string reason)) throw new InvalidOperationException(reason);
                            bool available = IsAvailable(selection, out reason);
                            offers.Add(new MenuOffer { selection = selection, price = price, available = available, reason = reason });
                        }
            return offers.ToArray();
        }
        public string GetRecommendationSnapshotJson() => JsonUtility.ToJson(new MenuRecommendationSnapshot {
            menu = GetMenu(), offers = GetMenuOffers(),
            inventory = JsonUtility.FromJson<RecommendationSnapshot>(inventory.GetRecommendationSnapshotJson())
        }, true);
    }
}
