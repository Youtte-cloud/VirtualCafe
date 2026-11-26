using System.Collections;
using UnityEngine;

public sealed class CafeGrinderAnimation : MonoBehaviour
{
    [Header("Rotating parts")]
    public Transform spinPivot;

    [Header("Grinding")]
    [Min(0.1f)]
    public float duration = 2f;

    [Min(1)]
    public int turns = 3;

    public bool IsPlaying { get; private set; }

    private Quaternion startRotation;
    private Transform activePivot;

    public IEnumerator Grind()
    {
        if (!isActiveAndEnabled || !spinPivot || IsPlaying)
            yield break;

        activePivot = spinPivot;
        startRotation = activePivot.localRotation;
        IsPlaying = true;

        try
        {
            float elapsed = 0f;
            float seconds = Mathf.Max(0.1f, duration);

            while (elapsed < seconds &&
                   isActiveAndEnabled &&
                   activePivot)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / seconds);

                // 平滑启动和停止，完成指定圈数。
                float progress = Mathf.SmoothStep(0f, 1f, t);
                float angle = 360f * Mathf.Max(1, turns) * progress;

                activePivot.localRotation =
                    startRotation *
                    Quaternion.AngleAxis(angle, Vector3.up);

                yield return null;
            }
        }
        finally
        {
            RestoreRotation();
        }
    }

    private void RestoreRotation()
    {
        if (activePivot)
            activePivot.localRotation = startRotation;

        activePivot = null;
        IsPlaying = false;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        RestoreRotation();
    }

    [ContextMenu("Test Grind (Play Mode)")]
    private void TestGrind()
    {
        if (Application.isPlaying && isActiveAndEnabled && !IsPlaying)
            StartCoroutine(Grind());
    }
}