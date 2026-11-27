using UnityEngine;
using VirtualCafe.Recommendation;

// Metadata travels with each finished cup from pickup to the selected table.
public sealed class CafeDrinkLabel : MonoBehaviour
{
    public string drinkName;
    public int cupSizeMl;
    public string DisplayName => CafeUiLocalization.Text(drinkName) + " / " + cupSizeMl + " ml";
}
