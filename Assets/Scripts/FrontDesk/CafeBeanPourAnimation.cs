using System.Collections;
using UnityEngine;

public sealed class CafeBeanPourAnimation : MonoBehaviour
{
    [Header("Bean bags")]
    public Transform blueMountainBag;
    public Transform geishaBag;
    public Transform konaBag;

    [Header("Pour target")]
    public Transform beanPourTarget;
    public Transform beanBagPourPose;

    [Tooltip("倒豆时，袋口高于豆子落点的距离。")]
    public float mouthHeight = 0.22f;

    [Tooltip("袋口向豆袋原位置一侧偏移的距离。")]
    public float mouthSideOffset = 0.06f;

    [Header("Movement")]
    public float moveDuration = 1f;
    public float tiltDuration = 0.5f;
    public float tiltAngle = 110f;
    public float movementArcHeight = 0.12f;

    [Header("Flying beans")]
    public Material beanMaterial;

    [Header("Bean pouring sound")]
    public AudioSource beansAudio;
    public AudioClip beansPourSound;

    [Range(8, 80)]
    public int beanCount = 32;

    public float beanSize = 0.012f;
    public float launchInterval = 0.03f;
    public float flightDuration = 0.5f;

    public bool IsPlaying { get; private set; }

    private Transform activeBag;
    private Vector3 homePosition;
    private Quaternion homeRotation;

    // 相对于豆袋旋转方向的袋口偏移，使用世界尺寸。
    private Vector3 mouthOffset;

    private GameObject particleRoot;
    private Material generatedMaterial;
    private int version;

    private Transform GetBag(string beanId)
    {
        switch (beanId)
        {
            case "bean-blue-mountain":
                return blueMountainBag;

            case "bean-geisha":
                return geishaBag;

            case "bean-kona":
                return konaBag;

            default:
                return null;
        }
    }

    public IEnumerator PlayPour(string beanId)
    {
        if (!isActiveAndEnabled || IsPlaying)
            yield break;

        Transform bag = GetBag(beanId);

        if (!bag || !beanPourTarget || !beanBagPourPose)
        {
            Debug.LogWarning(
                "倒豆动画缺少对应豆袋或 Bean Pour Target：" + beanId,
                this);

            yield break;
        }

        if (!TryGetBounds(bag, out Bounds bounds))
        {
            Debug.LogWarning("所选豆袋没有可见模型。", bag);
            yield break;
        }

        activeBag = bag;
        homePosition = bag.position;
        homeRotation = bag.rotation;

        // 用模型顶部中心作为袋口。
        Vector3 mouthWorld = new Vector3(
            bounds.center.x,
            bounds.max.y,
            bounds.center.z);

        mouthOffset =
            Quaternion.Inverse(homeRotation) *
            (mouthWorld - homePosition);

        int currentVersion = ++version;
        IsPlaying = true;

        try
        {
            // 直接使用手动设置的袋口停靠位置。
            Vector3 targetMouth = beanBagPourPose.position;

            // 袋口的倾倒方向朝向碗内落点。
            Vector3 outward =
                targetMouth - beanPourTarget.position;

            outward.y = 0f;

            // 两个点在俯视图中重合时，使用停靠点的朝向。
            if (outward.sqrMagnitude < 0.0001f)
            {
                outward = -beanBagPourPose.forward;
                outward.y = 0f;
            }

            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.back;

            outward.Normalize();

            // 保持原朝向，把袋口移到磨豆机上方。
            yield return MoveMouth(
                targetMouth,
                homeRotation,
                moveDuration,
                movementArcHeight,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 围绕袋口倾斜，避免袋口随旋转大幅偏移。
            Vector3 towardTarget = -outward;
            Vector3 tiltAxis =
                Vector3.Cross(Vector3.up, towardTarget).normalized;

            Quaternion tiltedRotation =
                Quaternion.AngleAxis(
                    Mathf.Clamp(tiltAngle, 0f, 160f),
                    tiltAxis) *
                homeRotation;

            yield return MoveMouth(
                targetMouth,
                tiltedRotation,
                tiltDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            yield return PourBeans(
                targetMouth,
                beanPourTarget.position,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 豆袋扶正。
            yield return MoveMouth(
                targetMouth,
                homeRotation,
                tiltDuration,
                0f,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            // 回到原来的陈列位置。
            Vector3 homeMouth =
                homePosition + homeRotation * mouthOffset;

            yield return MoveMouth(
                homeMouth,
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
                RestoreBag();
                IsPlaying = false;
            }
        }
    }

    private bool CanContinue(int currentVersion)
    {
        return currentVersion == version &&
               isActiveAndEnabled &&
               activeBag &&
               beanPourTarget;
    }

    private IEnumerator MoveMouth(
        Vector3 destination,
        Quaternion rotation,
        float duration,
        float arcHeight,
        int currentVersion)
    {
        Vector3 startMouth =
            activeBag.position + activeBag.rotation * mouthOffset;

        Quaternion startRotation = activeBag.rotation;

        float elapsed = 0f;
        float seconds = Mathf.Max(0.1f, duration);

        while (elapsed < seconds)
        {
            if (!CanContinue(currentVersion))
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            Vector3 mouth =
                Vector3.Lerp(startMouth, destination, smooth) +
                Vector3.up *
                Mathf.Sin(t * Mathf.PI) *
                Mathf.Max(0f, arcHeight);

            Quaternion currentRotation =
                Quaternion.Slerp(startRotation, rotation, smooth);

            SetBagPose(mouth, currentRotation);

            yield return null;
        }

        if (CanContinue(currentVersion))
            SetBagPose(destination, rotation);
    }

    private void SetBagPose(Vector3 mouth, Quaternion rotation)
    {
        activeBag.SetPositionAndRotation(
            mouth - rotation * mouthOffset,
            rotation);
    }

    private IEnumerator PourBeans(
        Vector3 source,
        Vector3 destination,
        int currentVersion)
    {
        ClearParticles();

        particleRoot = new GameObject("PouringCoffeeBeans");

        if (beansAudio && beansPourSound)
        {
            beansAudio.clip = beansPourSound;
            beansAudio.loop = true;
            beansAudio.Play();
        }

        int count = Mathf.Clamp(beanCount, 8, 80);
        var particles = new Transform[count];
        var starts = new Vector3[count];

        Material material = GetBeanMaterial();

        for (int i = 0; i < count; i++)
        {
            var bean =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            bean.name = "Bean_" + i;
            bean.transform.SetParent(particleRoot.transform, false);

            float size = Mathf.Max(0.003f, beanSize);

            // 椭圆形，区别于后面的咖啡粉颗粒。
            bean.transform.localScale =
                new Vector3(size * 0.8f, size * 0.55f, size * 1.2f);

            bean.transform.rotation = Random.rotation;

            var collider = bean.GetComponent<Collider>();

            if (collider)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            if (material)
                bean.GetComponent<Renderer>().sharedMaterial = material;

            Vector2 spread = Random.insideUnitCircle * 0.008f;

            starts[i] =
                source + new Vector3(spread.x, 0f, spread.y);

            bean.transform.position = starts[i];
            bean.SetActive(false);

            particles[i] = bean.transform;
        }

        float interval = Mathf.Max(0.005f, launchInterval);
        float seconds = Mathf.Max(0.1f, flightDuration);
        float totalDuration = (count - 1) * interval + seconds;
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

                // 逐渐加速下落。
                float progress = t * t;

                particles[i].position =
                    Vector3.Lerp(starts[i], destination, progress);

                particles[i].Rotate(
                    new Vector3(120f, 80f, 60f) *
                    Time.unscaledDeltaTime,
                    Space.Self);
            }

            yield return null;
        }

        ClearParticles();
    }

    private Material GetBeanMaterial()
    {
        if (beanMaterial)
            return beanMaterial;

        if (!generatedMaterial)
        {
            Shader shader = Shader.Find("Standard");

            if (shader)
            {
                generatedMaterial = new Material(shader);
                generatedMaterial.name = "Animated Coffee Beans";
                generatedMaterial.color =
                    new Color(0.28f, 0.14f, 0.07f);

                generatedMaterial.SetFloat("_Glossiness", 0.2f);
            }
        }

        return generatedMaterial;
    }

    private static bool TryGetBounds(
        Transform target,
        out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;

        foreach (var renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
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

        return found;
    }

    private void RestoreBag()
    {
        if (activeBag)
            activeBag.SetPositionAndRotation(homePosition, homeRotation);
    }

    private void ClearParticles()
    {
        if (beansAudio)
        {
            beansAudio.Stop();
            beansAudio.loop = false;
        }

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
        RestoreBag();

        IsPlaying = false;
    }

    private void OnDisable()
    {
        ResetAnimation();
        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        if (generatedMaterial)
            Destroy(generatedMaterial);
    }
}