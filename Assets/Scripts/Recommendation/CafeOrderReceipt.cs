using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    [Serializable]
    public sealed class CafeOrderLine
    {
        public MenuSelection selection;
        public string drinkName, beanName, milkName;
        public int totalPriceCents;
    }

    [Serializable]
    public sealed class CafeOrderReceipt
    {
        public string pickupNumber, confirmedAt, drinkName, beanName, milkName;
        public int totalPriceCents;
        // Keep legacy fields so existing single-line receipts remain readable.
        public MenuSelection selection;
        public CafeOrderLine[] lines;

        public CafeOrderLine[] GetLines()
        {
            if (lines != null && lines.Length > 0) return lines;
            if (selection == null) return new CafeOrderLine[0];
            return new[] { new CafeOrderLine {
                selection = selection, drinkName = drinkName,
                beanName = beanName, milkName = milkName,
                totalPriceCents = totalPriceCents
            }};
        }

        public int TotalServings
        {
            get
            {
                int count = 0;
                foreach (var line in GetLines())
                    if (line != null && line.selection != null)
                        count = checked(count + line.selection.Servings);
                return count;
            }
        }

        public void IssuePickupNumber()
        {
            string day = DateTime.Now.ToString("yyyyMMdd");
            confirmedAt = DateTime.Now.ToString("o");
            try
            {
                int next = PlayerPrefs.GetString("CafePickupDay", "") == day
                    ? checked(PlayerPrefs.GetInt("CafePickupSequence", 0) + 1) : 1;
                PlayerPrefs.SetString("CafePickupDay", day);
                PlayerPrefs.SetInt("CafePickupSequence", next);
                PlayerPrefs.Save();
                pickupNumber = "A" + next.ToString("D3");
            }
            catch (Exception)
            {
                pickupNumber = "T" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
                Debug.LogWarning("取餐序号保存失败，已生成独立取餐号。");
            }
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "CafeOrders");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, day + "-" + pickupNumber + ".json"), JsonUtility.ToJson(this, true));
            }
            catch (Exception)
            {
                Debug.LogWarning("订单已确认，取餐凭据文件保存失败；请保留界面上的号码，无需重新下单。");
            }
        }

        public static bool TryLoadLatest(out CafeOrderReceipt receipt)
            => TryLoadLatest(Path.Combine(Application.persistentDataPath, "CafeOrders"), out receipt);

        public static bool TryLoadLatest(string folder, out CafeOrderReceipt receipt)
        {
            receipt = null;
            DateTimeOffset newest = DateTimeOffset.MinValue;
            try
            {
                if (!Directory.Exists(folder)) return false;
                foreach (string file in Directory.GetFiles(folder, "*.json"))
                {
                    try
                    {
                        var candidate = JsonUtility.FromJson<CafeOrderReceipt>(File.ReadAllText(file));
                        if (candidate == null || string.IsNullOrEmpty(candidate.pickupNumber) ||
                            candidate.totalPriceCents < 0 || !DateTimeOffset.TryParse(candidate.confirmedAt, out var date) ||
                            date <= newest) continue;
                        var entries = candidate.GetLines();
                        if (entries.Length == 0) continue;
                        bool valid = true;
                        foreach (var line in entries)
                            if (line == null || line.selection == null || line.selection.Servings <= 0 ||
                                string.IsNullOrEmpty(line.selection.MenuItemId)) { valid = false; break; }
                        if (!valid) continue;
                        int count = candidate.TotalServings;
                        if (count <= 0) continue;
                        newest = date; receipt = candidate;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { return false; }
            return receipt != null;
        }

        public string ClerkMessage
        {
            get
            {
                return CafeUiLocalization.Format(
                    "Order confirmed: {0} cups. Pickup number: {1}.",
                    "订单已确认，共 {0} 杯。取餐号：{1}。",
                    "合計 {0} 杯の注文が確定しました。受取番号：{1}。",
                    TotalServings, pickupNumber);
            }
        }
    }
}
