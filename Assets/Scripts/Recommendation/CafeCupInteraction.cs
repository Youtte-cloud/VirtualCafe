using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    // Uses the existing product-to-renderer bindings. No shared scene or cup colliders are changed.
    public sealed class CafeCupInteraction : MonoBehaviour
    {
        private CafeFrontDeskUI frontDesk;
        private CafeProductHighlighter highlighter;
        private Camera view;
        private string hovered;
        private readonly Dictionary<string, string> names = new Dictionary<string, string>();
        public float maximumDistance = 3.5f;
        public string HoverName => hovered != null && names.TryGetValue(hovered, out var name)
            ? CafeUiLocalization.Text(name) : "";
        public void Initialize(CafeFrontDeskUI ui, CafeProductHighlighter displays, Camera camera)
        {
            frontDesk = ui; highlighter = displays; view = camera;
            foreach (var item in CafeRuntimeServices.Menu.GetMenu().items) names[item.id] = item.name;
        }
        private void Update()
        {
            if (!frontDesk || !highlighter || !view || highlighter.products == null || !frontDesk.CanInspectCups ||
                (EventSystem.current && EventSystem.current.IsPointerOverGameObject()))
            { ClearHover(); return; }
            Ray ray = Cursor.lockState == CursorLockMode.Locked
                ? view.ViewportPointToRay(new Vector3(.5f, .5f, 0)) : view.ScreenPointToRay(Input.mousePosition);
            string id = null;
            float nearest = maximumDistance;
            foreach (var product in highlighter.products)
            {
                if (product == null || !product.cupRenderer || !product.cupRenderer.enabled ||
                    !product.cupRenderer.gameObject.activeInHierarchy || !names.ContainsKey(product.productId)) continue;
                if (!product.cupRenderer.bounds.IntersectRay(ray, out var distance) || distance < 0 || distance >= nearest) continue;
                // A wall or other solid object must not permit inspection through it.
                if (Physics.Raycast(ray, out var hit, Mathf.Max(0, distance - .03f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    hit.transform != product.cupRenderer.transform && !hit.transform.IsChildOf(product.cupRenderer.transform)) continue;
                nearest = distance; id = product.productId;
            }
            if (id != hovered)
            {
                ClearHover(); hovered = id;
                if (hovered != null) highlighter.HighlightProduct(hovered);
            }
            if (hovered != null && Input.GetMouseButtonDown(0))
            {
                string selected = hovered; hovered = null;
                frontDesk.InspectCup(selected);
            }
        }
        private void ClearHover()
        {
            if (hovered != null && highlighter) highlighter.ClearHighlight();
            hovered = null;
        }
        private void OnDisable() { ClearHover(); }
    }
}
