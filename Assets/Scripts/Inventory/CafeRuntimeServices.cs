using UnityEngine;

namespace VirtualCafe.Inventory
{
    /// <summary>Main-thread, lazy, shared services for all scene adapters in one Play session.</summary>
    public static class CafeRuntimeServices
    {
        private static InventoryService inventory;
        private static CafeMenuService menu;

        public static InventoryService Inventory
        {
            get
            {
                if (inventory == null) inventory = InventoryService.CreateDefault();
                return inventory;
            }
        }
        public static CafeMenuService Menu
        {
            get
            {
                if (menu == null) menu = new CafeMenuService(Inventory);
                return menu;
            }
        }

        // Also runs when Enter Play Mode has domain reload disabled. Never resets the file.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            menu = null;
            inventory = null;
        }
    }
}
