using System;

namespace VirtualCafe.Inventory
{
    // Arrays and public fields deliberately keep these DTOs compatible with JsonUtility.
    [Serializable] public sealed class CafeCatalog
    {
        public InventoryItemDefinition[] items;
    }

    [Serializable] public sealed class RecipeBook
    {
        public DrinkRecipe[] recipes;
    }

    [Serializable] public sealed class InventoryItemDefinition
    {
        public string id;
        public string name;
        public string category;
        public string unit;
        public string flavorTendency;
        public bool containsMilk;
        public string allergenNote;
        public int initialQuantity;
        public int priceUnitQuantity;
        public int priceUnitCents;
    }

    [Serializable] public sealed class DrinkRecipe
    {
        public string id;
        public string name;
        public string note;
        public int cupSizeMl;
        public int extractionTargetG;
        public int waterMl;
        public int iceG;
        public RecipeRequirement[] requirements;
    }

    [Serializable] public sealed class RecipeRequirement
    {
        // "bean" / "milk" selects a request option; "item" uses itemId.
        public string source;
        public string itemId;
        public int quantity;
    }

    [Serializable] public sealed class InventoryQuantity
    {
        public string itemId;
        public int quantity;
    }

    [Serializable] public sealed class InventoryDocument
    {
        public int version = 1;
        public InventoryQuantity[] items;
    }

    [Serializable] public sealed class RecommendationSnapshot
    {
        public string usage = "Read-only recommendation input. Recommendations must be confirmed through InventoryService.";
        public CafeCatalog catalog;
        public RecipeBook recipeBook;
        public InventoryDocument stock;
    }

    public sealed class DrinkRequest
    {
        public string RecipeId;
        public int CupSizeMl; // Required for coffee; 0 for bottled/canned soda.
        public string BeanId;
        public string MilkId;
        public int Servings = 1;
    }
}
