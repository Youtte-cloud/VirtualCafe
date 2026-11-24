using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CafeOrderAnimation : MonoBehaviour
{
    public CafeIngredientPresentation ingredientPresentation;
    public Transform cupAnchor;
    public Camera customerCamera;

    [Header("Animation")]
    public float duration = 1.8f;
    public float gatheringHeight = 0.18f;
    public float swirlRadius = 0.06f;

    private sealed class MovingIngredient
    {
        public Transform target;
        public Vector3 startWorldPosition;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public bool active;
    }

    private void Awake()
    {
        if (!ingredientPresentation)
            ingredientPresentation =
                GetComponent<CafeIngredientPresentation>();
    }

    public IEnumerator Play()
    {
        if (!ingredientPresentation || !cupAnchor)
            yield break;

        if (!customerCamera)
            customerCamera = ingredientPresentation.customerCamera;

        var moving = new List<MovingIngredient>();
        var labels = new List<Renderer>();
        var labelStates = new List<bool>();

        var views = ingredientPresentation.ingredients;

        if (views == null)
            yield break;

        // Only animate ingredients currently being displayed.
        foreach (var view in views)
        {
            if (view == null || !view.model ||
                !view.model.activeInHierarchy)
                continue;

            Transform target = view.model.transform;

            moving.Add(new MovingIngredient
            {
                target = target,
                startWorldPosition = target.position,
                localPosition = target.localPosition,
                localRotation = target.localRotation,
                localScale = target.localScale,
                active = view.model.activeSelf
            });

            foreach (var text in
                view.model.GetComponentsInChildren<TextMesh>(true))
            {
                var renderer = text.GetComponent<Renderer>();

                if (!renderer)
                    continue;

                labels.Add(renderer);
                labelStates.Add(renderer.enabled);
            }
        }

        if (moving.Count == 0)
            yield break;

        bool previouslyEnabled = ingredientPresentation.enabled;

        // Pause the normal layout so it cannot overwrite animation.
        ingredientPresentation.enabled = false;

        foreach (var item in moving)
            item.target.gameObject.SetActive(true);

        foreach (var label in labels)
            label.enabled = false;

        try
        {
            float elapsed = 0f;
            float seconds = Mathf.Max(0.1f, duration);

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / seconds);

                Vector3 up = customerCamera
                    ? customerCamera.transform.up
                    : Vector3.up;

                Vector3 right = customerCamera
                    ? customerCamera.transform.right
                    : Vector3.right;

                Vector3 mouth = FindCupMouth();
                Vector3 gatheringPoint =
                    mouth + up * gatheringHeight;

                for (int i = 0; i < moving.Count; i++)
                {
                    var item = moving[i];

                    if (!item.target)
                        continue;

                    Vector3 position;
                    float scale;

                    if (t < 0.55f)
                    {
                        float u = Mathf.SmoothStep(
                            0f, 1f, t / 0.55f
                        );

                        // First bring the ingredients together.
                        position = Vector3.Lerp(
                            item.startWorldPosition,
                            gatheringPoint,
                            u
                        );

                        position += right *
                            Mathf.Sin(t * Mathf.PI * 6f + i * Mathf.PI) *
                            swirlRadius * Mathf.Sin(u * Mathf.PI);

                        scale = Mathf.Lerp(1f, 0.6f, u);
                    }
                    else
                    {
                        float u = Mathf.SmoothStep(
                            0f, 1f, (t - 0.55f) / 0.45f
                        );

                        // Then move down into the cup and disappear.
                        position = Vector3.Lerp(
                            gatheringPoint,
                            mouth - up * 0.06f,
                            u
                        );

                        position += right *
                            Mathf.Sin(u * Mathf.PI * 4f + i * Mathf.PI) *
                            swirlRadius * (1f - u);

                        scale = Mathf.Lerp(0.6f, 0f, u);
                    }

                    item.target.position = position;
                    item.target.localScale =
                        item.localScale * scale;
                }

                yield return null;
            }

            foreach (var item in moving)
            {
                if (item.target)
                    item.target.gameObject.SetActive(false);
            }

            yield return new WaitForSecondsRealtime(0.15f);
        }
        finally
        {
            // Restore transforms so the next order starts normally.
            foreach (var item in moving)
            {
                if (!item.target)
                    continue;

                item.target.localPosition = item.localPosition;
                item.target.localRotation = item.localRotation;
                item.target.localScale = item.localScale;
                item.target.gameObject.SetActive(item.active);
            }

            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i])
                    labels[i].enabled = labelStates[i];
            }

            if (ingredientPresentation)
                ingredientPresentation.enabled = previouslyEnabled;
        }
    }

    private Vector3 FindCupMouth()
    {
        bool found = false;
        Bounds bounds = new Bounds();

        foreach (var renderer in
            cupAnchor.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                renderer.GetComponent<TextMesh>())
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found)
            return new Vector3(
                bounds.center.x,
                bounds.max.y,
                bounds.center.z
            );

        return cupAnchor.position + Vector3.up * 0.2f;
    }
}