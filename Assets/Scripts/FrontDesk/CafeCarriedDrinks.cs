using System.Collections.Generic;
using UnityEngine;

public sealed class CafeCarriedDrinks : MonoBehaviour
{
    private sealed class CarriedOrder
    {
        public string pickupNumber;
        public GameObject[] cups;
    }

    private readonly List<CarriedOrder> orders =
        new List<CarriedOrder>();

    private readonly HashSet<string> collectedOrders =
        new HashSet<string>();

    public bool HasDrinks => orders.Count > 0;

    public const int MaxCarriedCups = 5;

    public int CarriedCupCount
    {
        get
        {
            int count = 0;

            foreach (var order in orders)
                count += order.cups.Length;

            return count;
        }
    }

    public int RemainingCapacity =>
        Mathf.Max(0, MaxCarriedCups - CarriedCupCount);

    public string PickupNumber { get; private set; }

    public bool TryReceive(
        string pickupNumber,
        GameObject[] cups)
    {
        if (string.IsNullOrEmpty(pickupNumber) ||
            collectedOrders.Contains(pickupNumber) ||
            cups == null ||
            cups.Length == 0 ||
            cups.Length > RemainingCapacity)
        {
            return false;
        }

        // 检查是否重复领取了同一个杯子。
        var uniqueCups = new HashSet<GameObject>();

        foreach (var order in orders)
        {
            foreach (var cup in order.cups)
            {
                if (cup)
                    uniqueCups.Add(cup);
            }
        }

        foreach (var cup in cups)
        {
            if (!cup || !uniqueCups.Add(cup))
                return false;
        }

        var receivedCups = (GameObject[])cups.Clone();

        foreach (var cup in receivedCups)
        {
            cup.SetActive(false);
            cup.transform.SetParent(transform, true);
        }

        orders.Add(new CarriedOrder
        {
            pickupNumber = pickupNumber,
            cups = receivedCups
        });

        collectedOrders.Add(pickupNumber);
        UpdatePickupNumber();

        return true;
    }

    public bool TryPlaceOn(CafeTableDrinkArea table)
    {
        if (!HasDrinks || !table)
            return false;

        int available = table.AvailableSlots;
        int orderCount = 0;

        var cupsToPlace = new List<GameObject>();

        // 按领取顺序选择桌面容得下的完整订单。
        foreach (var order in orders)
        {
            if (order.cups.Length > available)
                break;

            cupsToPlace.AddRange(order.cups);
            available -= order.cups.Length;
            orderCount++;
        }

        if (orderCount == 0)
            return false;

        if (!table.TryPlace(cupsToPlace.ToArray()))
            return false;

        // 成功交给桌面后，才移除随身记录。
        orders.RemoveRange(0, orderCount);
        UpdatePickupNumber();

        return true;
    }

    private void UpdatePickupNumber()
    {
        PickupNumber = orders.Count > 0
            ? orders[0].pickupNumber
            : null;
    }

    private void OnDestroy()
    {
        foreach (var order in orders)
        {
            foreach (var cup in order.cups)
            {
                if (cup)
                    Destroy(cup);
            }
        }

        orders.Clear();
    }
}