using System;
using System.IO;
using UnityEngine;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    [Serializable]
    public sealed class CafeOrderReceipt
    {
        public string pickupNumber, confirmedAt, drinkName, beanName, milkName;
        public int totalPriceCents;
        public MenuSelection selection;

        // Called only AFTER the inventory service has confirmed the purchase.
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
                // A numbering-storage failure must never invite payment a second time.
                pickupNumber = "T" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
                Debug.LogWarning("取餐序号保存失败，已生成独立取餐号。");
            }
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "CafeOrders");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, day + "-" + pickupNumber + ".json"), JsonUtility.ToJson(this, true));
            }
            catch (Exception) { Debug.LogWarning("订单已确认，取餐凭据文件保存失败；请保留界面上的号码，无需重新下单。"); }
        }

        public string ClerkMessage => "点单完成啦！你的" + selection.CupSizeMl + "ml" + drinkName +
            (string.IsNullOrEmpty(milkName) ? "" : "（" + milkName + "）") +
            "已确认，取餐号是 " + pickupNumber + "。可以先找个喜欢的位置坐一会儿，取餐时出示这个号码就好，谢谢你～";
    }
}
