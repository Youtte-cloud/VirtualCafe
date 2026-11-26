using System.Collections.Generic;
using UnityEngine;

public sealed class CafeCarriedDrinks : MonoBehaviour
{
    private readonly List<GameObject> carriedCups =
        new List<GameObject>();

    private readonly HashSet<string> collectedOrders =
        new HashSet<string>();

    public bool HasDrinks => carriedCups.Count > 0;

    public string PickupNumber { get; private set; }

    // 接收取餐区已经制作完成的杯子。
    public bool TryReceive(
        string pickupNumber,
        GameObject[] cups)
    {
        if (HasDrinks ||
            string.IsNullOrEmpty(pickupNumber) ||
            collectedOrders.Contains(pickupNumber) ||
            cups == null ||
            cups.Length == 0)
        {
            return false;
        }

        var unique = new HashSet<GameObject>();

        foreach (var cup in cups)
        {
            if (!cup || !unique.Add(cup))
                return false;
        }

        foreach (var cup in cups)
        {
            cup.SetActive(false);
            cup.transform.SetParent(transform, true);
            carriedCups.Add(cup);
        }

        PickupNumber = pickupNumber;
        collectedOrders.Add(pickupNumber);

        return true;
    }

    // 桌子接收成功后，才清除随身订单。
    public bool TryPlaceOn(CafeTableDrinkArea table)
    {
        if (!HasDrinks || !table)
            return false;

        if (!table.TryPlace(carriedCups.ToArray()))
            return false;

        carriedCups.Clear();
        PickupNumber = null;

        return true;
    }

    private void OnDestroy()
    {
        foreach (var cup in carriedCups)
        {
            if (cup)
                Destroy(cup);
        }

        carriedCups.Clear();
    }
}