using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation.Editor
{
    public static class CafeRecommendationSetup
    {
        [MenuItem("Virtual Cafe/LLM/Create Member C Development Scene")]
        public static void CreateScene()
        {
            const string target = "Assets/Scenes/Cafe_Recommendation.unity";
            if (File.Exists(target)) throw new InvalidOperationException("Development scene already exists. Open it directly to preserve your work.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.CopyAsset("Assets/Scenes/CafeMain.unity", target)) throw new IOException("Failed to copy CafeMain.");
            EditorSceneManager.OpenScene(target);
            var reception = UnityEngine.Object.FindObjectOfType<CafeReceptionController>(true);
            if (!reception || !reception.recommendationPanel) throw new InvalidOperationException("Reception controller or recommendation panel missing.");
            reception.gameObject.AddComponent<CafeRecommendationWelcome>().reception = reception;
            var panel = reception.recommendationPanel;
            var controller = panel.AddComponent<CafeRecommendationController>();
            controller.menuAdapter = reception.menuAdapter;
            controller.highlighter = reception.highlighter;
            var debug = panel.AddComponent<CafeRecommendationDebugPanel>();
            debug.reception = reception;
            EditorSceneManager.MarkSceneDirty(panel.scene);
            EditorSceneManager.SaveScene(panel.scene);
            Selection.activeGameObject = panel;
        }

        [MenuItem("Virtual Cafe/LLM/Run Protocol Checks")]
        public static void RunChecks()
        {
            string folder = Path.Combine(Path.GetTempPath(), "CafeLLMTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                // Isolated inventory: never modify the real persistent inventory.
                var inventory = new InventoryService(
                    Resources.Load<TextAsset>("Cafe/inventory-catalog").text,
                    Resources.Load<TextAsset>("Cafe/drink-recipes").text,
                    new JsonInventoryStore(Path.Combine(folder, "inventory.json")));
                var menu = new CafeMenuService(inventory);
                int count = 0;
                foreach (var offer in menu.GetMenuOffers())
                {
                    string json = JsonUtility.ToJson(new RecommendationReply { status = "recommendation", reply = "Check", selection = offer.selection });
                    Check(CafeRecommendationProtocol.TryParse(json, menu, out _, out _) == offer.available, "Check");
                    count++;
                }
                Check(!CafeRecommendationProtocol.TryParse("not json", menu, out _, out _), "CheckJSON");
                Check(!CafeRecommendationProtocol.TryParse("{}", menu, out _, out _), "Check");
                Check(CafeRecommendationProtocol.TryParse("{\"status\":\"no_match\",\"reply\":\"Check\",\"selection\":null}", menu, out _, out _), "Check");
                Check(CafeRecommendationProtocol.TryParse("{\"status\":\"follow_up\",\"reply\":\"Check\",\"selection\":null}", menu, out _, out _), "Check");
                var selection = menu.GetMenuOffers()[0].selection;
                string before = menu.GetRecommendationSnapshotJson();
                var catalog = JsonUtility.FromJson<CafeCatalog>(Resources.Load<TextAsset>("Cafe/inventory-catalog").text);
                foreach (var item in catalog.items) item.initialQuantity = item.id == selection.BeanId ? 18 : 0;
                var lastCupInventory = new InventoryService(JsonUtility.ToJson(catalog),
                    Resources.Load<TextAsset>("Cafe/drink-recipes").text,
                    new JsonInventoryStore(Path.Combine(folder, "last-cup.json")));
                var lastCupMenu = new CafeMenuService(lastCupInventory);
                string recommendation = JsonUtility.ToJson(new RecommendationReply { status = "recommendation", reply = "Recommended", selection = selection });
                Check(CafeRecommendationProtocol.TryParse(recommendation, lastCupMenu, out _, out _), "last cup available");
                string snapshotBefore = lastCupMenu.GetRecommendationSnapshotJson();
                Check(lastCupMenu.TryConfirmOrder(new[] { selection }, out _), "last cup confirmation");
                Check(snapshotBefore != lastCupMenu.GetRecommendationSnapshotJson(), "fresh snapshot changes after purchase");
                Check(!CafeRecommendationProtocol.TryParse(recommendation, lastCupMenu, out _, out _), "reject stale sold-out recommendation");
                Check(before == menu.GetRecommendationSnapshotJson(), "read-only parsing never changes inventory");
                selection.Servings = 2;
                Check(!CafeRecommendationProtocol.TryParse(JsonUtility.ToJson(new RecommendationReply { status = "recommendation", reply = "Recommended", selection = selection }), menu, out _, out _), "reject multiple servings");
                selection.Servings = 1;
                selection.CupSizeMl = 123;
                Check(!CafeRecommendationProtocol.TryParse(JsonUtility.ToJson(new RecommendationReply { status = "recommendation", reply = "Recommended", selection = selection }), menu, out _, out _), "reject illegal size");
                selection.CupSizeMl = 400;
                selection.MenuItemId = "invented";
                Check(!CafeRecommendationProtocol.TryParse(JsonUtility.ToJson(new RecommendationReply { status = "recommendation", reply = "Check", selection = selection }), menu, out _, out _), "Check");
                Debug.Log("LLM protocol checks passed: " + count + " menu offers + invalid/follow-up/no-match/last-cup/size/quantity/read-only checks. No real model request was made.");
            }
            finally { Directory.Delete(folder, true); }
        }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("LLM check failed: " + name);
        }
    }
}
