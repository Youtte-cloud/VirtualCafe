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

    private int occupiedCount;
    private bool placing;

    public int AvailableSlots
    {
        get
        {
            return Mathf.Max(
                0,
                GetValidSlots().Count - occupiedCount
            );
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
    public bool TryPlace(GameObject[] cups)
    {
        if (!isActiveAndEnabled ||
            placing ||
            cups == null ||
            cups.Length == 0)
        {
            return false;
        }

        var uniqueCups = new HashSet<GameObject>();

        foreach (var cup in cups)
        {
            if (!cup || !uniqueCups.Add(cup))
                return false;
        }

        var slots = GetValidSlots();

        if (slots.Count - occupiedCount < cups.Length)
            return false;

        var targets = new Vector3[cups.Length];

        for (int i = 0; i < cups.Length; i++)
        {
            Transform cup = cups[i].transform;

            // 根据杯底对齐桌面定位点。
            Vector3 bottom = FindBottom(cup);

            targets[i] =
                slots[occupiedCount + i].position +
                (cup.position - bottom);
        }

        // 接收后由桌子持有，站起不会重复生成。
        occupiedCount += cups.Length;
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