using System.Collections;
using UnityEngine;

public sealed class CafeGroundsPourAnimation : MonoBehaviour
{
    [Header("Scene references")]
    public Transform groundsCup;
    public Transform powderFill;
    public Transform pourTarget;
    public Transform cupPourPose;
    public Material groundsMaterial;

    [Header("Cup movement")]
    public float moveDuration = 1f;
    public float tiltDuration = 0.5f;
    public float tiltAngle = 70f;

    [Tooltip("接粉杯停在落粉点外侧的距离，单位为米。")]
    public float approachDistance = 0.14f;

    [Tooltip("接粉杯杯底高于落粉点的距离。")]
    public float heightAboveTarget = 0.08f;

    public float movementArcHeight = 0.08f;

    [Header("Pouring grounds")]
    [Range(8, 80)]
    public int particleCount = 48;

    public float particleSize = 0.008f;
    public float launchInterval = 0.025f;
    public float flightDuration = 0.45f;
    public float powderArcHeight = 0.025f;

    public bool IsPlaying { get; private set; }

    private Transform savedCup;
    private Transform savedPowder;

    private Vector3 homePosition;
    private Quaternion homeRotation;

    private Vector3 fullPowderPosition;
    private Vector3 fullPowderScale;

    private GameObject particleRoot;
    private int version;

    public IEnumerator PlayPour()
    {
        if (!isActiveAndEnabled || IsPlaying)
            yield break;

        if (!groundsCup || !powderFill || !pourTarget || !cupPourPose)
        {
            Debug.LogWarning(
                "请绑定接粉杯、粉面、倒粉目标点和杯子停靠点。",
                this);

            yield break;
        }

        savedCup = groundsCup;
        savedPowder = powderFill;

        homePosition = savedCup.position;
        homeRotation = savedCup.rotation;

        // 此时接粉已完成，记录满杯粉面的尺寸。
        fullPowderPosition = savedPowder.localPosition;
        fullPowderScale = savedPowder.localScale;

        int currentVersion = ++version;
        IsPlaying = true;

        try
        {
            // 从目标点朝杯子的原位置取一个水平外侧方向。
            // 直接使用手动设置的杯底停靠位置。
            Vector3 pourPosition = cupPourPose.position;

            // 杯子朝滤篮方向倾斜。
            Vector3 outward = pourPosition - pourTarget.position;
            outward.y = 0f;

            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.back;

            outward.Normalize();

            Quaternion uprightRotation =
                Quaternion.FromToRotation(savedCup.up, Vector3.up) *
                homeRotation;

            // 满杯移动到咖啡机前。
            yield return MovePose(
                pourPosition,
                uprightRotation,
                moveDuration,
                movementArcHeight,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            Vector3 towardTarget = -outward;
            Vector3 tiltAxis =
                Vector3.Cross(Vector3.up, towardTarget).normalized;

            Quaternion tiltedRotation =
                Quaternion.AngleAxis(
                    Mathf.Clamp(tiltAngle, 0f, 100f),
                    tiltAxis) *
                uprightRotation;

            // 向滤篮方向倾斜。
            yield return MovePose(
                pourPosition,
                tiltedRotation,
                tiltDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 求出靠近滤篮一侧的杯口位置。
            float cupHeight = 0.07f;
            float cupRadius = 0.04f;

            var cupModel = savedCup.GetComponent<CafeGroundsCup>();

            if (cupModel)
            {
                cupHeight = cupModel.height;
                cupRadius = cupModel.radius;
            }

            Vector3 rimDirection =
                Quaternion.Inverse(uprightRotation) * towardTarget;

            rimDirection.y = 0f;
            rimDirection.Normalize();

            Vector3 rimLocal =
                Vector3.up * cupHeight +
                rimDirection * cupRadius;

            Vector3 source = savedCup.TransformPoint(rimLocal);
            Vector3 destination = pourTarget.position;

            yield return PourParticles(
                source,
                destination,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 空杯扶正。
            yield return MovePose(
                pourPosition,
                uprightRotation,
                tiltDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 空杯回到原位。
            yield return MovePose(
                homePosition,
                homeRotation,
                moveDuration,
                movementArcHeight,
                currentVersion);
        }
        finally
        {
            if (currentVersion == version)
            {
                ClearParticles();
                RestoreCup();
                SetRemaining(0f);
                IsPlaying = false;
            }
        }
    }

    private bool CanContinue(int currentVersion)
    {
        return currentVersion == version &&
               isActiveAndEnabled &&
               savedCup &&
               savedPowder &&
               pourTarget;
    }

    private IEnumerator MovePose(
        Vector3 destination,
        Quaternion rotation,
        float duration,
        float arcHeight,
        int currentVersion)
    {
        Vector3 startPosition = savedCup.position;
        Quaternion startRotation = savedCup.rotation;

        float elapsed = 0f;
        float seconds = Mathf.Max(0.1f, duration);

        while (elapsed < seconds)
        {
            if (!CanContinue(currentVersion))
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            savedCup.position =
                Vector3.Lerp(startPosition, destination, smooth) +
                Vector3.up *
                Mathf.Sin(t * Mathf.PI) *
                Mathf.Max(0f, arcHeight);

            savedCup.rotation =
                Quaternion.Slerp(startRotation, rotation, smooth);

            yield return null;
        }

        if (CanContinue(currentVersion))
            savedCup.SetPositionAndRotation(destination, rotation);
    }

    private IEnumerator PourParticles(
        Vector3 source,
        Vector3 destination,
        int currentVersion)
    {
        ClearParticles();

        particleRoot = new GameObject("PouringCoffeeGrounds");

        int count = Mathf.Clamp(particleCount, 8, 80);
        var particles = new Transform[count];
        var starts = new Vector3[count];

        Material material = groundsMaterial;

        if (!material)
        {
            var renderer = savedPowder.GetComponent<Renderer>();

            if (renderer)
                material = renderer.sharedMaterial;
        }

        for (int i = 0; i < count; i++)
        {
            var particle =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            particle.name = "PourGround_" + i;
            particle.transform.SetParent(particleRoot.transform, false);

            particle.transform.localScale =
                Vector3.one * Mathf.Max(0.001f, particleSize);

            var collider = particle.GetComponent<Collider>();

            if (collider)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            if (material)
                particle.GetComponent<Renderer>().sharedMaterial = material;

            Vector2 spread = Random.insideUnitCircle * 0.004f;

            starts[i] =
                source + new Vector3(spread.x, 0f, spread.y);

            particle.transform.position = starts[i];
            particle.SetActive(false);

            particles[i] = particle.transform;
        }

        float interval = Mathf.Max(0.005f, launchInterval);
        float seconds = Mathf.Max(0.1f, flightDuration);

        float launchSpan = (count - 1) * interval;
        float totalDuration = launchSpan + seconds;
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            if (!CanContinue(currentVersion))
                yield break;

            elapsed += Time.unscaledDeltaTime;

            for (int i = 0; i < count; i++)
            {
                if (!particles[i])
                    continue;

                float t = (elapsed - i * interval) / seconds;
                bool visible = t >= 0f && t < 1f;

                particles[i].gameObject.SetActive(visible);

                if (!visible)
                    continue;

                particles[i].position =
                    Vector3.Lerp(starts[i], destination, t) +
                    Vector3.up *
                    Mathf.Sin(t * Mathf.PI) *
                    Mathf.Max(0f, powderArcHeight);
            }

            // 颗粒持续离开杯子，杯内粉面逐渐降低。
            SetRemaining(
                1f - Mathf.Clamp01(
                    elapsed / Mathf.Max(0.001f, launchSpan)));

            yield return null;
        }

        SetRemaining(0f);
        ClearParticles();
    }

    private void SetRemaining(float amount)
    {
        if (!savedPowder)
            return;

        amount = Mathf.Clamp01(amount);
        savedPowder.gameObject.SetActive(amount > 0f);

        Vector3 scale = fullPowderScale;
        scale.y *= Mathf.Max(0.001f, amount);

        float bottomY =
            fullPowderPosition.y - fullPowderScale.y;

        Vector3 position = fullPowderPosition;
        position.y = bottomY + scale.y;

        savedPowder.localScale = scale;
        savedPowder.localPosition = position;
    }

    private void RestoreCup()
    {
        if (savedCup)
            savedCup.SetPositionAndRotation(homePosition, homeRotation);
    }

    private void ClearParticles()
    {
        if (!particleRoot)
            return;

        particleRoot.SetActive(false);
        Destroy(particleRoot);
        particleRoot = null;
    }

    public void ResetAnimation()
    {
        version++;

        ClearParticles();
        RestoreCup();
        SetRemaining(0f);

        IsPlaying = false;
    }

    private void OnDisable()
    {
        ResetAnimation();
        StopAllCoroutines();
    }
}