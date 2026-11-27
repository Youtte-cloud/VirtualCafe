using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CafeTableDrinkArea : MonoBehaviour
{
    [Header("Table positions")]
    public Transform[] cupSlots = new Transform[0];

    [Header("Placement animation")]
    public float dropHeight = 0.18f;
    public float dropDuration = 0.45f;
    public float interval = 0.15f;

    [Header("Optional sound")]
    public AudioSource audioSource;
    public AudioClip placeSound;
    public AudioClip sipSound;
    private AudioSource sipSource;

    [Header("Drinking")]
    [Min(0.1f)] public float drinkingDuration = 2.4f;
    private readonly Dictionary<Transform, GameObject> placedCups = new Dictionary<Transform, GameObject>();
    private readonly Dictionary<GameObject, CafeCarriedDrinks> cupOwners = new Dictionary<GameObject, CafeCarriedDrinks>();
    private Coroutine drinkingRoutine;
    public bool IsDrinking { get; private set; }
    public float DrinkProgress { get; private set; }
    private GameObject selectedCup;
    private GameObject hoveredCup;
    private LineRenderer selectionRing;
    private Material selectionMaterial;
    public string SelectedDrinkName
    {
        get { var slot = SelectedDrinkSlot(); return slot ? CupName(placedCups[slot]) : ""; }
    }
    public string HoveredDrinkName => hoveredCup ? CupName(hoveredCup) : "";
    private static string CupName(GameObject cup)
    {
        var label = cup.GetComponent<CafeDrinkLabel>();
        return label ? label.DisplayName : "Coffee";
    }

    private static bool CupBounds(GameObject cup, out Bounds bounds)
    {
        bounds = new Bounds(); bool found = false;
        foreach (var renderer in cup.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.GetComponent<TextMesh>()) continue;
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    public void UpdateDrinkPointer(Ray ray, bool click)
    {
        hoveredCup = null;
        if (!placing && !IsDrinking && isActiveAndEnabled)
        {
            float nearest = float.PositiveInfinity;
            foreach (var slot in DrinkSlots())
            {
                var cup = placedCups[slot];
                if (CupBounds(cup, out var bounds) && bounds.IntersectRay(ray, out float distance) && distance < nearest)
                { hoveredCup = cup; nearest = distance; }
            }
            if (hoveredCup && Physics.Raycast(ray, out var hit, nearest, ~0, QueryTriggerInteraction.Ignore)
                && hit.transform != hoveredCup.transform && !hit.transform.IsChildOf(hoveredCup.transform)) hoveredCup = null;
            if (click && hoveredCup) selectedCup = hoveredCup;
        }
        DrawSelectionRing();
    }

    private void DrawSelectionRing()
    {
        var target = selectedCup ? selectedCup : hoveredCup;
        if (placing || IsDrinking || !target || !CupBounds(target, out var bounds))
        { if (selectionRing) selectionRing.enabled = false; return; }
        if (!selectionRing)
        {
            var marker = new GameObject("Selected coffee ring");
            marker.transform.SetParent(transform, false);
            selectionRing = marker.AddComponent<LineRenderer>();
            var shader = Shader.Find("Sprites/Default");
            if (!shader) shader = Shader.Find("Unlit/Color");
            if (shader) { selectionMaterial = new Material(shader); selectionRing.sharedMaterial = selectionMaterial; }
            selectionRing.useWorldSpace = true; selectionRing.loop = true; selectionRing.positionCount = 40;
            selectionRing.startWidth = selectionRing.endWidth = 0.0035f;
        }
        selectionRing.enabled = true;
        Color color = selectedCup ? new Color(0.95f, 0.74f, 0.35f, 1) : new Color(0.75f, 0.9f, 0.7f, 0.85f);
        selectionRing.startColor = selectionRing.endColor = color;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.008f;
        Vector3 center = new Vector3(bounds.center.x, bounds.min.y + 0.004f, bounds.center.z);
        for (int i = 0; i < 40; i++)
        {
            float angle = i * Mathf.PI * 2f / 40;
            selectionRing.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
        }
    }

    private void OnDestroy() { if (selectionMaterial) Destroy(selectionMaterial); }

    public bool IsPlacing => placing;
    public int DrinkCount => DrinkSlots().Count;
    public bool CanDrink => isActiveAndEnabled && !placing && !IsDrinking && SelectedDrinkSlot() != null;

    private List<Transform> DrinkSlots()
    {
        var result = new List<Transform>();
        foreach (var slot in GetValidSlots())
            if (placedCups.TryGetValue(slot, out var cup) && cup && cup.activeInHierarchy) result.Add(slot);
        return result;
    }

    private Transform SelectedDrinkSlot()
    {
        var slots = DrinkSlots();
        if (slots.Count == 1) return slots[0];
        foreach (var slot in slots) if (placedCups[slot] == selectedCup) return slot;
        return null;
    }

    public bool SelectDrink(int index)
    {
        if (placing || IsDrinking || !isActiveAndEnabled) return false;
        var slots = DrinkSlots();
        if (index < 0 || index >= slots.Count) return false;
        selectedCup = placedCups[slots[index]];
        return true;
    }

    public bool IsDrinkSelected(int index)
    {
        var slots = DrinkSlots();
        return index >= 0 && index < slots.Count && slots[index] == SelectedDrinkSlot();
    }

    public string DrinkName(int index)
    {
        var slots = DrinkSlots();
        if (index < 0 || index >= slots.Count) return "";
        var label = placedCups[slots[index]].GetComponent<CafeDrinkLabel>();
        return label ? label.DisplayName : "Coffee";
    }

    public void ClearDrinkSelection()
    {
        if (!IsDrinking) selectedCup = null;
        hoveredCup = null;
        if (selectionRing) selectionRing.enabled = false;
    }


    public bool TryDrink(Camera drinkCamera = null)
    {
        if (!CanDrink) return false;
        var slot = SelectedDrinkSlot();
        var cup = placedCups[slot];
        IsDrinking = true;
        hoveredCup = null;
        if (selectionRing) selectionRing.enabled = false;
        DrinkProgress = 0;
        drinkingRoutine = StartCoroutine(DrinkRoutine(slot, cup, drinkCamera ? drinkCamera : Camera.main));
        return true;
    }

    private void PlaySipSound()
    {
        if (!sipSound) sipSound = Resources.Load<AudioClip>("Cafe/Audio/CoffeeSip");
        if (!sipSound) return;
        if (!sipSource)
        {
            sipSource = gameObject.AddComponent<AudioSource>();
            sipSource.playOnAwake = false;
            sipSource.loop = false;
            sipSource.spatialBlend = 0f;
        }
        sipSource.clip = sipSound;
        sipSource.Play();
    }

    private IEnumerator DrinkRoutine(Transform slot, GameObject cup, Camera drinkCamera)
    {
        Vector3 startPosition = cup.transform.position;
        Quaternion startRotation = cup.transform.rotation;
        bool completed = false;
        bool soundPlayed = false;
        // A small extra gap prevents the tilted cup/saucer from crossing the near plane.
        float clearance = 0.26f;
        if (CupBounds(cup, out var drinkingBounds))
        {
            float pivotRadius = (drinkingBounds.center - startPosition).magnitude + drinkingBounds.extents.magnitude;
            clearance = Mathf.Max(clearance, pivotRadius + 0.035f);
        }
        float distance = drinkCamera ? Mathf.Max(0.38f, drinkCamera.nearClipPlane + clearance) : 0;
        Vector3 mouthPosition = drinkCamera
            ? drinkCamera.transform.TransformPoint(new Vector3(0.06f, -0.15f, distance)) : startPosition;
        Vector3 lowerPosition = drinkCamera
            ? drinkCamera.transform.TransformPoint(new Vector3(0.09f, -0.65f, distance)) : startPosition;
        Vector3 tiltAxis = drinkCamera ? drinkCamera.transform.right : Vector3.right;
        try
        {
            float elapsed = 0;
            float duration = Mathf.Max(0.1f, drinkingDuration);
            while (elapsed < duration)
            {
                if (!cup || (drinkCamera && !drinkCamera.isActiveAndEnabled)) yield break;
                float t = Mathf.Clamp01(elapsed / duration);
                DrinkProgress = t;
                if (drinkCamera)
                {
                    if (t < 0.45f)
                    {
                        float u = Mathf.SmoothStep(0, 1, t / 0.45f);
                        cup.transform.position = Vector3.Lerp(startPosition, mouthPosition, u)
                            + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.06f;
                        cup.transform.rotation = startRotation;
                    }
                    else if (t < 0.86f)
                    {
                        float u = Mathf.SmoothStep(0, 1, (t - 0.45f) / 0.12f);
                        cup.transform.position = mouthPosition;
                        cup.transform.rotation = Quaternion.AngleAxis(-22f * u, tiltAxis) * startRotation;
                    }
                    else
                    {
                        float u = Mathf.SmoothStep(0, 1, (t - 0.86f) / 0.14f);
                        cup.transform.position = Vector3.Lerp(mouthPosition, lowerPosition, u);
                        cup.transform.rotation = Quaternion.AngleAxis(-22f * (1 - u), tiltAxis) * startRotation;
                    }
                }
                if (!soundPlayed && t >= 0.45f) { PlaySipSound(); soundPlayed = true; }
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            if (placedCups.TryGetValue(slot, out var current) && current == cup)
                placedCups.Remove(slot);
            if (cupOwners.TryGetValue(cup, out var owner))
            {
                if (owner) owner.MarkDrinkFinished(cup);
                cupOwners.Remove(cup);
            }
            selectedCup = null;
            completed = true;
            if (cup) { cup.SetActive(false); Destroy(cup); }
        }
        finally
        {
            // Interrupted drinking restores the cup and does not release order capacity.
            if (!completed && cup) cup.transform.SetPositionAndRotation(startPosition, startRotation);
            if (sipSource) sipSource.Stop();
            IsDrinking = false;
            DrinkProgress = 0;
            drinkingRoutine = null;
        }
    }

    public void CancelDrinking()
    {
        if (sipSource) sipSource.Stop();
        if (drinkingRoutine != null) StopCoroutine(drinkingRoutine);
        drinkingRoutine = null;
        IsDrinking = false;
        DrinkProgress = 0;
    }

    private void OnDisable() { CancelDrinking(); ClearDrinkSelection(); }

    private bool placing;

    public int AvailableSlots
    {
        get
        {
            int count = 0;
            foreach (var slot in GetValidSlots())
                if (!placedCups.TryGetValue(slot, out var cup) || !cup) count++;
            return count;
        }
    }

    private List<Transform> GetValidSlots()
    {
        var result = new List<Transform>();

        if (cupSlots == null)
            return result;

        foreach (var slot in cupSlots)
        {
            if (slot && !result.Contains(slot))
                result.Add(slot);
        }

        return result;
    }

    // 接收已经领取的杯子；不重新下单、不扣库存。
    public bool TryPlace(GameObject[] cups, CafeCarriedDrinks owner = null)
    {
        if (!isActiveAndEnabled ||
            placing || IsDrinking ||
            cups == null ||
            cups.Length == 0)
        {
            return false;
        }

        var uniqueCups = new HashSet<GameObject>();

        foreach (var cup in cups)
        {
            if (!cup || !uniqueCups.Add(cup) || placedCups.ContainsValue(cup))
                return false;
        }

        var slots = GetValidSlots();
        slots.RemoveAll(slot => placedCups.TryGetValue(slot, out var existing) && existing);

        if (slots.Count < cups.Length)
            return false;

        var targets = new Vector3[cups.Length];

        for (int i = 0; i < cups.Length; i++)
        {
            Transform cup = cups[i].transform;

            // 根据杯底对齐桌面定位点。
            Vector3 bottom = FindBottom(cup);

            targets[i] =
                slots[i].position +
                (cup.position - bottom);
        }

        // 接收后由桌子持有，站起不会重复生成。
        for (int i = 0; i < cups.Length; i++)
        {
            placedCups[slots[i]] = cups[i];
            if (owner) cupOwners[cups[i]] = owner;
        }
        placing = true;

        foreach (var cup in cups)
        {
            cup.transform.SetParent(transform, true);
            cup.SetActive(false);
        }

        StartCoroutine(PlaceRoutine(cups, targets));
        return true;
    }

    private IEnumerator PlaceRoutine(
        GameObject[] cups,
        Vector3[] targets)
    {
        try
        {
            for (int i = 0; i < cups.Length; i++)
            {
                if (!cups[i])
                    continue;

                Transform cup = cups[i].transform;

                Vector3 start =
                    targets[i] +
                    Vector3.up * Mathf.Max(0f, dropHeight);

                cup.position = start;
                cups[i].SetActive(true);

                float elapsed = 0f;
                float seconds =
                    Mathf.Max(0.1f, dropDuration);

                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;

                    float t = Mathf.Clamp01(
                        elapsed / seconds
                    );

                    cup.position = Vector3.Lerp(
                        start,
                        targets[i],
                        Mathf.SmoothStep(0f, 1f, t)
                    );

                    yield return null;
                }

                cup.position = targets[i];

                if (audioSource && placeSound)
                    audioSource.PlayOneShot(placeSound);

                yield return new WaitForSecondsRealtime(
                    Mathf.Max(0f, interval)
                );
            }
        }
        finally
        {
            // 动画中断时，也把已接收的杯子放稳。
            for (int i = 0; i < cups.Length; i++)
            {
                if (!cups[i])
                    continue;

                cups[i].transform.position = targets[i];
                cups[i].SetActive(true);
            }

            placing = false;
        }
    }

    private static Vector3 FindBottom(Transform target)
    {
        bool found = false;
        Bounds bounds = new Bounds();

        foreach (var renderer in
                 target.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled ||
                renderer.GetComponent<TextMesh>())
            {
                continue;
            }

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

        return found
            ? new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z
            )
            : target.position;
    }
}