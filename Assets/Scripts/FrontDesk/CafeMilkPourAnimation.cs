using System.Collections;
using UnityEngine;

public sealed class CafeMilkPourAnimation : MonoBehaviour
{
    [Header("Scene references")]
    public Transform pitcher;
    public Transform milkSpout;
    public Transform milkPourTarget;
    public Transform milkPitcherPourPose;

    [Header("Movement")]
    public float moveDuration = 1f;
    public float tiltDuration = 0.5f;
    public float tiltAngle = 60f;
    public float movementArcHeight = 0.08f;

    [Header("Milk stream")]
    public float pourDuration = 1.8f;
    public float streamWidth = 0.006f;
    public float streamArcHeight = 0.015f;

    [Range(8, 40)]
    public int streamSegments = 20;

    public Material milkMaterial;

    public bool IsPlaying { get; private set; }

    public Transform milkTravelPoint;
    private Transform savedPitcher;
    private Vector3 homePosition;
    private Quaternion homeRotation;

    private GameObject streamObject;
    private LineRenderer milkStream;
    private Material generatedMaterial;

    private int version;

    public IEnumerator Pour()
    {
        if (!isActiveAndEnabled || IsPlaying)
            yield break;

        if (!pitcher ||
            !milkSpout ||
            !milkPourTarget ||
            !milkPitcherPourPose)
        {
            Debug.LogWarning(
                "请绑定奶缸、奶嘴、奶流落点和奶嘴停靠点。",
                this);

            yield break;
        }

        if (!milkSpout.IsChildOf(pitcher))
        {
            Debug.LogWarning(
                "MilkSpout 必须放在奶缸 pitcher 的层级里面。",
                this);

            yield break;
        }

        savedPitcher = pitcher;
        homePosition = pitcher.position;
        homeRotation = pitcher.rotation;

        int currentVersion = ++version;
        IsPlaying = true;

        try
        {
            // 记录奶嘴相对于奶缸原始姿态的偏移。
            Vector3 spoutOffset =
                Quaternion.Inverse(homeRotation) *
                (milkSpout.position - savedPitcher.position);

            // 副本指定的是整个奶缸最终的位置和旋转。
            Quaternion tiltedRotation = milkPitcherPourPose.rotation;

            Vector3 targetSpout =
                milkPitcherPourPose.position +
                tiltedRotation * spoutOffset;

            // 将最终姿态扶正，保留对应的出奶方向。
            Vector3 modelUp =
                Quaternion.Inverse(homeRotation) * Vector3.up;

            Quaternion uprightRotation =
                Quaternion.FromToRotation(
                    tiltedRotation * modelUp,
                    Vector3.up) *
                tiltedRotation;

            // 先保持奶缸原来的朝向，移动奶嘴到停靠点。
            // 先移动到机器外侧，再在空处调整朝向。
            if (milkTravelPoint)
            {
                yield return MoveSpout(
                    milkTravelPoint.position,
                    homeRotation,
                    moveDuration,
                    0f,
                    currentVersion);

                if (!CanContinue(currentVersion))
                    yield break;

                yield return MoveSpout(
                    milkTravelPoint.position,
                    uprightRotation,
                    tiltDuration,
                    0f,
                    currentVersion);

                if (!CanContinue(currentVersion))
                    yield break;
            }

            // 从外侧接近倒奶位置，取消向上拱起的弧线。
            yield return MoveSpout(
                targetSpout,
                uprightRotation,
                moveDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 围绕奶嘴倾斜，奶嘴保持在停靠位置。
            yield return MoveSpout(
                targetSpout,
                tiltedRotation,
                tiltDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            EnsureStream();

            float elapsed = 0f;
            float seconds = Mathf.Max(0.2f, pourDuration);

            while (elapsed < seconds)
            {
                if (!CanContinue(currentVersion))
                    yield break;

                elapsed += Time.unscaledDeltaTime;

                UpdateStream(
                    Mathf.Clamp01(elapsed / 0.15f));

                yield return null;
            }

            HideStream();

            // 奶缸扶正。
            yield return MoveSpout(
                targetSpout,
                uprightRotation,
                tiltDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 计算原始姿态下奶嘴应该回到的位置。
            Vector3 homeSpout =
                homePosition + homeRotation * spoutOffset;

            // 倒奶结束、扶正后，先退出到机器外侧。
            if (milkTravelPoint)
            {
                yield return MoveSpout(
                    milkTravelPoint.position,
                    uprightRotation,
                    moveDuration,
                    0f,
                    currentVersion);

                if (!CanContinue(currentVersion))
                    yield break;

                // 在外侧恢复原来的朝向。
                yield return MoveSpout(
                    milkTravelPoint.position,
                    homeRotation,
                    tiltDuration,
                    0f,
                    currentVersion);

                if (!CanContinue(currentVersion))
                    yield break;
            }

            yield return MoveSpout(
                homeSpout,
                homeRotation,
                moveDuration,
                0f,
                currentVersion);
        }
        finally
        {
            if (currentVersion == version)
            {
                HideStream();
                RestorePitcher();
                IsPlaying = false;
            }
        }
    }

    private bool CanContinue(int currentVersion)
    {
        return currentVersion == version &&
               isActiveAndEnabled &&
               savedPitcher &&
               milkSpout &&
               milkPourTarget &&
               milkPitcherPourPose;
    }

    private IEnumerator MoveSpout(
        Vector3 destination,
        Quaternion rotation,
        float duration,
        float arcHeight,
        int currentVersion)
    {
        Vector3 startSpout = milkSpout.position;
        Quaternion startRotation = savedPitcher.rotation;

        float elapsed = 0f;
        float seconds = Mathf.Max(0.1f, duration);

        while (elapsed < seconds)
        {
            if (!CanContinue(currentVersion))
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            Vector3 spoutPosition =
                Vector3.Lerp(startSpout, destination, smooth) +
                Vector3.up *
                Mathf.Sin(t * Mathf.PI) *
                Mathf.Max(0f, arcHeight);

            Quaternion currentRotation =
                Quaternion.Slerp(startRotation, rotation, smooth);

            SetPitcherPose(spoutPosition, currentRotation);

            yield return null;
        }

        if (CanContinue(currentVersion))
            SetPitcherPose(destination, rotation);
    }

    private void SetPitcherPose(
        Vector3 spoutPosition,
        Quaternion rotation)
    {
        // 先旋转，再根据真实奶嘴的位置补偿平移。
        savedPitcher.rotation = rotation;
        savedPitcher.position += spoutPosition - milkSpout.position;
    }

    private void EnsureStream()
    {
        if (!milkStream)
        {
            streamObject = new GameObject("AnimatedMilkStream");

            milkStream = streamObject.AddComponent<LineRenderer>();
            milkStream.useWorldSpace = true;

            milkStream.numCapVertices = 4;
            milkStream.numCornerVertices = 4;

            milkStream.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            milkStream.receiveShadows = false;
        }

        Material material = milkMaterial;

        if (!material)
        {
            if (!generatedMaterial)
            {
                Shader shader = Shader.Find("Sprites/Default");

                if (shader)
                {
                    generatedMaterial = new Material(shader);
                    generatedMaterial.name = "Animated Milk";

                    generatedMaterial.color =
                        new Color(1f, 0.97f, 0.88f);
                }
            }

            material = generatedMaterial;
        }

        if (material)
            milkStream.sharedMaterial = material;

        milkStream.startColor = Color.white;
        milkStream.endColor = Color.white;

        milkStream.positionCount =
            Mathf.Clamp(streamSegments, 8, 40);

        // 第一次更新位置后再显示，避免闪到世界原点。
        milkStream.enabled = false;
        streamObject.SetActive(true);
    }

    private void UpdateStream(float visibleFraction)
    {
        if (!milkStream)
            return;

        Vector3 start = milkSpout.position;
        Vector3 end = milkPourTarget.position;

        Vector3 control =
            (start + end) * 0.5f +
            Vector3.up * Mathf.Max(0f, streamArcHeight);

        int count = milkStream.positionCount;

        float width = Mathf.Max(0.001f, streamWidth);

        milkStream.startWidth = width;
        milkStream.endWidth = width * 0.85f;

        for (int i = 0; i < count; i++)
        {
            float t =
                (float)i / (count - 1) * visibleFraction;

            float inverse = 1f - t;

            Vector3 position =
                inverse * inverse * start +
                2f * inverse * t * control +
                t * t * end;

            milkStream.SetPosition(i, position);
        }

        milkStream.enabled = true;
    }

    private void HideStream()
    {
        if (milkStream)
            milkStream.enabled = false;
    }

    private void RestorePitcher()
    {
        if (savedPitcher)
        {
            savedPitcher.SetPositionAndRotation(
                homePosition,
                homeRotation);
        }
    }

    public void ResetAnimation()
    {
        version++;

        HideStream();
        RestorePitcher();

        IsPlaying = false;
    }

    private void OnDisable()
    {
        ResetAnimation();
        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        if (streamObject)
            Destroy(streamObject);

        if (generatedMaterial)
            Destroy(generatedMaterial);
    }
}