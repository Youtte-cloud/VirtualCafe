using System;
using UnityEngine;
namespace VirtualCafe.Player
{
    public sealed class CafeDesktopSetup : MonoBehaviour
    {
        public Camera customerCamera;
        private void Start()
        {
            if (!customerCamera) customerCamera = Camera.main;
            // Existing primitives already have colliders; add collision to imported furniture and walls.
            foreach (var renderer in FindObjectsOfType<MeshRenderer>())
            {
                if (renderer.GetComponent<Collider>() || renderer.GetComponentInParent<Canvas>()) continue;
                string path = renderer.name.ToLowerInvariant();
                for (Transform parent = renderer.transform.parent; parent; parent = parent.parent) path += "/" + parent.name.ToLowerInvariant();
                if (!(path.Contains("chair") || path.Contains("table") || path.Contains("wall"))) continue;
                var box = renderer.gameObject.AddComponent<BoxCollider>();
                box.center = renderer.localBounds.center; box.size = renderer.localBounds.size;
            }
            var transforms = FindObjectsOfType<Transform>();
            foreach (var t in transforms)
            {
                if (!t.name.StartsWith("chair", StringComparison.OrdinalIgnoreCase)) continue;
                bool nested = false;
                for (var a = t.parent; a; a = a.parent) if (a.name.StartsWith("chair", StringComparison.OrdinalIgnoreCase)) nested = true;
                var renderers = t.GetComponentsInChildren<Renderer>();
                if (nested || renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                Vector3 facing = t.forward; float best = float.MaxValue;
                foreach (var table in transforms)
                    if (table.name.StartsWith("table", StringComparison.OrdinalIgnoreCase))
                    {
                        Vector3 direction = table.position - bounds.center; direction.y = 0;
                        if (direction.sqrMagnitude > .04f && direction.sqrMagnitude < best) { best = direction.sqrMagnitude; facing = direction.normalized; }
                    }
                var seat = t.GetComponent<CafeSeat>(); if (!seat) seat = t.gameObject.AddComponent<CafeSeat>();
                seat.Configure(bounds, facing);
            }
            var go = new GameObject("CustomerPlayer");
            go.transform.position = customerCamera.transform.position - Vector3.up * 1.65f;
            go.transform.rotation = Quaternion.Euler(0, customerCamera.transform.eulerAngles.y, 0);
            var body = GameObject.Find("CafeCustomer");
            if (body)
            {
                body.transform.SetParent(go.transform, true);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
            }
            customerCamera.transform.SetParent(go.transform, true);
            var player = go.AddComponent<CafeDesktopPlayer>();
            player.view = customerCamera;
        }
    }
}
