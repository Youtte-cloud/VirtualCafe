using System;
using System.Collections.Generic;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    // UI-only rules. Inventory remains authoritative at order confirmation.
    internal static class CafeUiSelectionRules
    {
        internal static string[] MissingOptions(MenuSelection selection, CafeMenuItem item,
            bool cupChosen, bool beanChosen, bool milkChosen)
        {
            var missing = new List<string>();
            if (!cupChosen || selection == null || item == null || Array.IndexOf(item.cupSizesMl, selection.CupSizeMl) < 0)
                missing.Add("Cup size");
            string bean = selection == null ? null : selection.BeanId;
            if (item != null && string.IsNullOrEmpty(bean) && item.beanIds.Length == 1) bean = item.beanIds[0];
            if (!beanChosen || item == null || Array.IndexOf(item.beanIds, bean) < 0) missing.Add("Coffee bean");
            if (item != null && item.milkIds.Length > 0 &&
                (!milkChosen || selection == null || Array.IndexOf(item.milkIds, selection.MilkId) < 0)) missing.Add("Milk");
            return missing.ToArray();
        }

        internal static bool HasAvailableOption(MenuOffer[] offers, MenuSelection candidate, string dimension,
            Func<MenuSelection, bool> canMake, bool cupChosen = false, bool beanChosen = false, bool milkChosen = false)
        {
            if (candidate == null || offers == null || canMake == null) return false;
            if (dimension != "cup" && dimension != "bean" && dimension != "milk") return false;
            foreach (var offer in offers)
            {
                var option = offer.selection;
                if (option.MenuItemId != candidate.MenuItemId) continue;
                if (dimension == "cup" && option.CupSizeMl != candidate.CupSizeMl) continue;
                if (dimension == "bean" && option.BeanId != candidate.BeanId) continue;
                if (dimension == "milk" && option.MilkId != candidate.MilkId) continue;
                if (dimension != "cup" && cupChosen && option.CupSizeMl != candidate.CupSizeMl) continue;
                if (dimension != "bean" && beanChosen && option.BeanId != candidate.BeanId) continue;
                if (dimension != "milk" && milkChosen && option.MilkId != candidate.MilkId) continue;
                var test = new MenuSelection { MenuItemId=option.MenuItemId, CupSizeMl=option.CupSizeMl,
                    BeanId=option.BeanId, MilkId=option.MilkId, Servings=candidate.Servings };
                if (canMake(test)) return true;
            }
            return false;
        }
    }
}
