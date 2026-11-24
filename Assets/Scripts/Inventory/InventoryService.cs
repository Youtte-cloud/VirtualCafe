using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VirtualCafe.Inventory
{
    // Injectable persistence allows failed writes to be tested without touching real stock.
    public interface IInventoryStore
    {
        string Location { get; }
        bool Exists { get; }
        string Read();
        // Must leave the previous document intact if it throws.
        void WriteAtomically(string json);
    }

    public sealed class JsonInventoryStore : IInventoryStore
    {
        public string Location { get; }
        public bool Exists => File.Exists(Location);
        public JsonInventoryStore(string path) { Location = Path.GetFullPath(path); }
        public string Read() => File.ReadAllText(Location, Encoding.UTF8);

        public void WriteAtomically(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Location));
            string temp = Location + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(Location)) File.Replace(temp, Location, null);
                else File.Move(temp, Location);
            }
            finally
            {
                // Cleanup must never turn a committed write into a reported failure.
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }

    // One service instance owns the file. Call on Unity's main thread, not concurrently.
    public sealed class InventoryService
    {
        private readonly CafeCatalog catalog;
        private readonly RecipeBook recipeBook;
        private readonly IInventoryStore store;
        private readonly Dictionary<string, InventoryItemDefinition> definitions = new Dictionary<string, InventoryItemDefinition>();
        private readonly Dictionary<string, DrinkRecipe> recipes = new Dictionary<string, DrinkRecipe>();
        private Dictionary<string, int> quantities;
        public string FilePath => store.Location;
        public event Action InventoryChanged;

        // Fresh instance for explicit load/reload. Scene code must use CafeRuntimeServices instead.
        public static InventoryService CreateDefault()
        {
            var asset = Resources.Load<TextAsset>("Cafe/inventory-catalog");
            if (asset == null) throw new InvalidOperationException("Missing Resources/Cafe/inventory-catalog.json");
            return new InventoryService(asset.text, new JsonInventoryStore(
                Path.Combine(Application.persistentDataPath, "CafeInventory", "inventory.json")));
        }

        public InventoryService(string catalogJson, IInventoryStore store)
            : this(catalogJson, LoadRecipes(), store) { }

        private static string LoadRecipes()
        {
            var asset = Resources.Load<TextAsset>("Cafe/drink-recipes");
            if (asset == null) throw new InvalidOperationException("Missing Resources/Cafe/drink-recipes.json");
            return asset.text;
        }

        public InventoryService(string catalogJson, string recipesJson, IInventoryStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            catalog = JsonUtility.FromJson<CafeCatalog>(catalogJson);
            recipeBook = JsonUtility.FromJson<RecipeBook>(recipesJson);
            if (catalog == null || catalog.items == null || recipeBook == null || recipeBook.recipes == null)
                throw new InvalidDataException("Invalid catalog.");
            foreach (var item in catalog.items)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || item.initialQuantity < 0)
                    throw new InvalidDataException("Invalid item definition.");
                definitions.Add(item.id, item);
            }
            foreach (var recipe in recipeBook.recipes)
            {
                if (recipe == null || string.IsNullOrEmpty(recipe.id) || recipe.requirements == null || recipe.requirements.Length == 0)
                    throw new InvalidDataException("Invalid recipe.");
                foreach (var requirement in recipe.requirements)
                    if (requirement == null || requirement.quantity <= 0 ||
                        (requirement.source != "bean" && requirement.source != "milk" &&
                         !(requirement.source == "item" && definitions.ContainsKey(requirement.itemId ?? ""))))
                        throw new InvalidDataException("Invalid recipe requirement.");
                bool bean = Array.Exists(recipe.requirements, r => r.source == "bean");
                if ((bean && recipe.cupSizeMl <= 0) || (!bean && recipe.cupSizeMl != 0) ||
                    recipe.extractionTargetG < 0 || recipe.waterMl < 0 || recipe.iceG < 0)
                    throw new InvalidDataException("Invalid recipe size or preparation target.");
                recipes.Add(RecipeKey(recipe.id, recipe.cupSizeMl), recipe);
            }
            try
            {
                if (store.Exists) quantities = ParseStock(store.Read());
                else
                {
                    quantities = InitialStock();
                    store.WriteAtomically(Serialize(quantities));
                }
            }
            catch (Exception e)
            {
                // Do not dump file contents or arbitrary exception messages into logs.
                Debug.LogError("[Cafe Inventory] 库存读取或首次保存失败，初始化中止；未自动重置已有文件。错误类型：" + e.GetType().Name);
                throw;
            }
        }

        public int GetQuantity(string itemId) => quantities[itemId];
        public string GetInventoryJson() => Serialize(quantities);
        public string GetRecommendationSnapshotJson() => JsonUtility.ToJson(new RecommendationSnapshot
        {
            catalog = new CafeCatalog { items = Array.FindAll(catalog.items, i => i.category != "soda") },
            recipeBook = recipeBook,
            stock = new InventoryDocument { items = Array.FindAll(ToDocument(quantities).items,
                i => definitions[i.itemId].category != "soda") }
        }, true);

        // Defensive copies: callers cannot mutate service definitions or recipes.
        public InventoryItemDefinition GetItemDefinition(string id) =>
            JsonUtility.FromJson<InventoryItemDefinition>(JsonUtility.ToJson(definitions[id], false));

        public bool TryGetRecipe(DrinkRequest drink, out DrinkRecipe recipe, out string reason)
        {
            recipe = null;
            if (!TryBuildDemand(new[] { drink }, out _, out reason, false)) return false;
            recipe = JsonUtility.FromJson<DrinkRecipe>(JsonUtility.ToJson(
                recipes[RecipeKey(drink.RecipeId, drink.CupSizeMl)], false));
            return true;
        }

        public bool CanMake(DrinkRequest drink, out string reason) =>
            TryBuildDemand(new[] { drink }, out _, out reason);

        public bool TryConfirmOrder(IReadOnlyList<DrinkRequest> drinks, out string reason)
        {
            if (!TryBuildDemand(drinks, out var demand, out reason))
            {
                Debug.LogWarning("[Cafe Inventory] 确认失败，整笔未扣减。" + reason);
                return false;
            }
            var candidate = new Dictionary<string, int>(quantities);
            foreach (var entry in demand) candidate[entry.Key] -= entry.Value;
            bool committed = TryCommit(candidate, out reason);
            if (committed) Debug.Log("[Cafe Inventory] 确认成功：" + drinks.Count + " 条饮品需求已统一扣减并保存。");
            return committed;
        }

        // Explicit developer reset only; never invoked by startup or Play mode.
        public bool TryResetToInitial(out string reason) => TryCommit(InitialStock(), out reason);

        private bool TryCommit(Dictionary<string, int> candidate, out string reason)
        {
            try { store.WriteAtomically(Serialize(candidate)); }
            catch (Exception e)
            {
                Debug.LogError("[Cafe Inventory] 库存保存失败，整笔未扣减，未发送库存变化通知。错误类型：" + e.GetType().Name);
                reason = "Inventory could not be saved; no stock was deducted: " + e.Message;
                return false;
            }
            quantities = candidate;
            reason = "Success";
            // A broken UI listener cannot change a committed transaction's outcome.
            if (InventoryChanged != null)
                foreach (Action listener in InventoryChanged.GetInvocationList())
                    try { listener(); } catch (Exception e) { Debug.LogException(e); }
            return true;
        }

        private bool TryBuildDemand(IReadOnlyList<DrinkRequest> drinks, out Dictionary<string, int> demand, out string reason, bool checkStock = true)
        {
            demand = new Dictionary<string, int>();
            reason = null;
            if (drinks == null || drinks.Count == 0) { reason = "Order cannot be empty."; return false; }
            try
            {
                foreach (var drink in drinks)
                {
                    if (drink == null || drink.Servings <= 0 || !recipes.TryGetValue(RecipeKey(drink.RecipeId ?? "", drink.CupSizeMl), out var recipe))
                    { reason = "Invalid drink, cup size or quantity."; return false; }
                    bool needsBean = Array.Exists(recipe.requirements, r => r.source == "bean");
                    bool needsMilk = Array.Exists(recipe.requirements, r => r.source == "milk");
                    if ((!needsBean && !string.IsNullOrEmpty(drink.BeanId)) ||
                        (!needsMilk && !string.IsNullOrEmpty(drink.MilkId)))
                    { reason = "This drink does not support the selected bean or milk."; return false; }
                    foreach (var requirement in recipe.requirements)
                    {
                        string id = requirement.source == "bean" ? drink.BeanId :
                            requirement.source == "milk" ? drink.MilkId : requirement.itemId;
                        if (!definitions.TryGetValue(id ?? "", out var item) ||
                            (requirement.source != "item" && item.category != requirement.source))
                        { reason = "Please select the bean or milk required by the recipe."; return false; }
                        demand.TryGetValue(id, out int existing);
                        demand[id] = checked(existing + checked(requirement.quantity * drink.Servings));
                    }
                }
            }
            catch (OverflowException) { reason = "Order quantity exceeds the allowed range."; return false; }
            foreach (var entry in demand)
                if (checkStock && quantities[entry.Key] < entry.Value)
                {
                    var item = definitions[entry.Key];
                    reason = $"Insufficient stock: {item.name}. Required: {entry.Value} {item.unit}; available: {quantities[entry.Key]} {item.unit}. No stock was deducted.";
                    return false;
                }
            return true;
        }

        private static string RecipeKey(string id, int cupSizeMl) => id + ":" + cupSizeMl;

        // Detached demand copy; succeeds only if the entire order is valid and available.
        public bool TryGetOrderRequirements(IReadOnlyList<DrinkRequest> drinks,
            out InventoryDocument requirements, out string reason)
        {
            bool valid = TryBuildDemand(drinks, out var demand, out reason);
            requirements = null;
            if (!valid) return false;
            var rows = new List<InventoryQuantity>();
            foreach (var item in catalog.items)
                if (demand.TryGetValue(item.id, out int amount))
                    rows.Add(new InventoryQuantity { itemId = item.id, quantity = amount });
            requirements = new InventoryDocument { items = rows.ToArray() };
            return true;
        }

        private Dictionary<string, int> InitialStock()
        {
            var result = new Dictionary<string, int>();
            foreach (var item in catalog.items) result.Add(item.id, item.initialQuantity);
            return result;
        }

        private Dictionary<string, int> ParseStock(string json)
        {
            var document = JsonUtility.FromJson<InventoryDocument>(json);
            if (document == null || document.version != 1 || document.items == null || document.items.Length != definitions.Count)
                throw new InvalidDataException("Invalid inventory file version or entries; the original file will not be overwritten automatically.");
            var result = new Dictionary<string, int>();
            foreach (var entry in document.items)
            {
                if (entry == null || !definitions.ContainsKey(entry.itemId ?? "") || entry.quantity < 0 || result.ContainsKey(entry.itemId))
                    throw new InvalidDataException("Inventory file contains invalid, duplicate or negative entries; it will not be reset automatically.");
                result.Add(entry.itemId, entry.quantity);
            }
            return result;
        }

        private InventoryDocument ToDocument(Dictionary<string, int> stock)
        {
            var rows = new InventoryQuantity[catalog.items.Length];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new InventoryQuantity { itemId = catalog.items[i].id, quantity = stock[catalog.items[i].id] };
            return new InventoryDocument { items = rows };
        }
        private string Serialize(Dictionary<string, int> stock) => JsonUtility.ToJson(ToDocument(stock), true);
    }
}
