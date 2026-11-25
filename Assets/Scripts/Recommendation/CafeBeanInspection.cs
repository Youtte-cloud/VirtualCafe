using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    // Explicit runtime bindings for the three new countertop bags; decorative jars and preview beans are excluded.
    public sealed class CafeBeanInspection : MonoBehaviour
    {
        [Serializable] public sealed class Binding { public string objectName; public string beanId; }
        public Binding[] bindings = {
            new Binding { objectName = "Coffee Bean Bag", beanId = "bean-blue-mountain" },
            new Binding { objectName = "Coffee Bean Bag (1)", beanId = "bean-geisha" },
            new Binding { objectName = "Coffee Bean Bag (2)", beanId = "bean-kona" }
        };
        private sealed class Target { public Transform root; public string id, name; public Renderer[] renderers; }
        private readonly List<Target> targets = new List<Target>();
        private CafeFrontDeskUI frontDesk;
        private int hover = -1, selected = -1;
        private LineRenderer ring;
        private Material ringMaterial;
        public string HoverName => hover >= 0 ? CafeUiLocalization.Text(targets[hover].name) : "";
        public void Initialize(CafeFrontDeskUI ui)
        {
            frontDesk = ui;
            foreach (var binding in bindings)
            {
                var obj = GameObject.Find(binding.objectName);
                if (!obj) continue;
                var definition = CafeRuntimeServices.Inventory.GetItemDefinition(binding.beanId);
                if (definition.category != "bean") continue;
                targets.Add(new Target { root = obj.transform, id = binding.beanId, name = definition.name,
                    renderers = obj.GetComponentsInChildren<Renderer>() });
            }
            var go = new GameObject("Bean selection outline"); go.transform.SetParent(transform, false);
            ring = go.AddComponent<LineRenderer>();
            var shader = Shader.Find("Sprites/Default");
            if (shader)
            {
                ringMaterial = new Material(shader); ring.sharedMaterial = ringMaterial;
                ring.startColor = ring.endColor = new Color(1, .79f, .35f, .95f);
            }
            ring.useWorldSpace = true; ring.positionCount = 5; ring.widthMultiplier = .008f;
            ring.numCornerVertices = 4; ring.enabled = false;
        }
        private static bool BoundsFor(Target target, out Bounds bounds)
        {
            bounds = new Bounds(); bool found = false;
            if (!target.root || !target.root.gameObject.activeInHierarchy) return false;
            foreach (var renderer in target.renderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.GetComponent<TextMesh>()) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }
        public bool FindHit(Ray ray, float maxDistance, out int index, out float distance)
        {
            index = -1; distance = maxDistance;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!BoundsFor(targets[i], out var bounds) || !bounds.IntersectRay(ray, out var hitDistance) || hitDistance < 0 || hitDistance >= distance) continue;
                if (Physics.Raycast(ray, out var hit, Mathf.Max(0, hitDistance - .03f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    hit.transform != targets[i].root && !hit.transform.IsChildOf(targets[i].root)) continue;
                index = i; distance = hitDistance;
            }
            return index >= 0;
        }
        public void Hover(int index) { if (selected < 0) hover = index; }
        public void ClearHover() { if (selected < 0) hover = -1; }
        public void Inspect(int index)
        {
            if (index < 0 || index >= targets.Count || !frontDesk) return;
            selected = index; hover = -1;
            if (!frontDesk.InspectBean(targets[index].id, targets[index].root)) selected = -1;
        }
        public bool GetSelectedBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            return selected >= 0 && BoundsFor(targets[selected], out bounds);
        }
        public void ClearSelection() { selected = hover = -1; if (ring) ring.enabled = false; }
        private void LateUpdate()
        {
            int active = selected >= 0 ? selected : hover;
            if (!ring) return;
            if (active < 0 || !BoundsFor(targets[active], out var bounds)) { ring.enabled = false; return; }
            ring.enabled = true;
            float y = bounds.min.y + .006f;
            float minX = bounds.min.x - .015f, maxX = bounds.max.x + .015f;
            float minZ = bounds.min.z - .015f, maxZ = bounds.max.z + .015f;
            ring.SetPositions(new[] { new Vector3(minX,y,minZ), new Vector3(maxX,y,minZ),
                new Vector3(maxX,y,maxZ), new Vector3(minX,y,maxZ), new Vector3(minX,y,minZ) });
        }
        private void OnDisable() { ClearSelection(); }
        private void OnDestroy() { if (ringMaterial) Destroy(ringMaterial); if (ring) Destroy(ring.gameObject); }
    }
}
