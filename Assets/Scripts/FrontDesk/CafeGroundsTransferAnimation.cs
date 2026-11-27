using System.Collections;
using UnityEngine;

public sealed class CafeGroundsTransferAnimation : MonoBehaviour
{
    [Header("Scene references")]
    public Transform drawer;
    public Transform groundsSource;
    public Transform groundsCup;
    public Transform powderFill;

    [Header("Drawer")]
    public Vector3 drawerOpenOffset =
        new Vector3(0f, 0f, 0.01356f);

    public float drawerMoveDuration = 0.5f;

    [Header("Flying grounds")]
    public Material groundsMaterial;

    [Header("Drawer sounds")]
    public AudioSource drawerAudio;
    public AudioClip drawerOpenSound;
    public AudioClip drawerCloseSound;

    [Header("Grounds sound")]
    public AudioSource groundsAudio;
    public AudioClip groundsSound;

    [Range(8, 80)]
    public int particleCount = 36;

    public float particleSize = 0.0035f;
    public float launchInterval = 0.035f;
    public float flightDuration = 0.65f;
    public float arcHeight = 0.12f;
    public float sourceSpread = 0.008f;

    public bool IsPlaying { get; private set; }

    private bool initialized;
    private Transform savedDrawer;
    private Transform savedPowder;

    private Vector3 closedDrawerPosition;
    private Vector3 fullPowderPosition;
    private Vector3 fullPowderScale;

    private GameObject particleRoot;
    private int version;

    private void PlayGroundsSound()
    {
        if (!groundsAudio || !groundsSound)
            return;

        groundsAudio.clip = groundsSound;
        groundsAudio.loop = true;
        groundsAudio.Play();
    }

    private void PlayDrawerSound(AudioClip clip)
    {
        if (!drawerAudio || !clip)
            return;

        drawerAudio.Stop();
        drawerAudio.loop = false;
        drawerAudio.PlayOneShot(clip);
    }

    private bool Initialize()
    {
        if (!drawer || !groundsSource || !groundsCup || !powderFill)
        {
            Debug.LogWarning(
                "请绑定抽屉、出粉点、接粉杯和粉面。",
                this);

            return false;
        }

        if (initialized)
            return true;

        savedDrawer = drawer;
        savedPowder = powderFill;

        closedDrawerPosition = drawer.localPosition;
        fullPowderPosition = powderFill.localPosition;
        fullPowderScale = powderFill.localScale;

        initialized = true;
        SetFill(0f);

        return true;
    }

    public IEnumerator PlayTransfer()
    {
        if (!isActiveAndEnabled || IsPlaying || !Initialize())
            yield break;

        int currentVersion = ++version;
        IsPlaying = true;
        bool completed = false;

        try
        {
            SetFill(0f);

            // 水平拉出抽屉。
            PlayDrawerSound(drawerOpenSound);

            yield return MoveDrawer(
                closedDrawerPosition,
                closedDrawerPosition + drawerOpenOffset,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            yield return new WaitForSecondsRealtime(0.2f);

            if (!CanContinue(currentVersion))
                yield break;

            // 颗粒飞入杯中，同时升高杯内粉面。
            yield return FlyGrounds(currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            yield return new WaitForSecondsRealtime(0.2f);

            if (!CanContinue(currentVersion))
                yield break;

            // 接粉完成后关闭抽屉。
            PlayDrawerSound(drawerCloseSound);

            yield return MoveDrawer(
                closedDrawerPosition + drawerOpenOffset,
                closedDrawerPosition,
                currentVersion);

            if (!CanContinue(currentVersion))
                yield break;

            completed = true;
        }
        finally
        {
            if (currentVersion == version)
            {
                ClearParticles();

                if (savedDrawer)
                    savedDrawer.localPosition = closedDrawerPosition;

                SetFill(completed ? 1f : 0f);
                IsPlaying = false;
            }
        }
    }

    private bool CanContinue(int currentVersion)
    {
        return currentVersion == version &&
               isActiveAndEnabled &&
               drawer &&
               groundsSource &&
               groundsCup &&
               powderFill;
    }

    private IEnumerator MoveDrawer(
        Vector3 from,
        Vector3 to,
        int currentVersion)
    {
        float elapsed = 0f;
        float seconds = Mathf.Max(0.1f, drawerMoveDuration);

        while (elapsed < seconds)
        {
            if (!CanContinue(currentVersion))
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);

            drawer.localPosition = Vector3.Lerp(
                from,
                to,
                Mathf.SmoothStep(0f, 1f, t));

            yield return null;
        }

        if (CanContinue(currentVersion))
            drawer.localPosition = to;
    }

    private IEnumerator FlyGrounds(int currentVersion)
    {
        ClearParticles();

        particleRoot = new GameObject("FlyingCoffeeGrounds");
        PlayGroundsSound();

        int count = Mathf.Clamp(particleCount, 8, 80);

        var particles = new Transform[count];
        var starts = new Vector3[count];

        Material material = groundsMaterial;

        if (!material)
        {
            var powderRenderer = powderFill.GetComponent<Renderer>();

            if (powderRenderer)
                material = powderRenderer.sharedMaterial;
        }

        // 粒子的终点位于杯内最终粉面的上表面。
        Vector3 destination = groundsCup.TransformPoint(
            fullPowderPosition +
            Vector3.up * fullPowderScale.y);

        for (int i = 0; i < count; i++)
        {
            GameObject particle =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            particle.name = "Ground_" + i;
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

            Vector2 spread =
                Random.insideUnitCircle * Mathf.Max(0f, sourceSpread);

            starts[i] = groundsSource.position +
                        new Vector3(spread.x, 0f, spread.y);

            particle.transform.position = starts[i];
            particle.SetActive(false);

            particles[i] = particle.transform;
        }

        float seconds = Mathf.Max(0.1f, flightDuration);
        float interval = Mathf.Max(0.005f, launchInterval);
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

                Vector3 position =
                    Vector3.Lerp(starts[i], destination, t);

                position += Vector3.up *
                            Mathf.Sin(t * Mathf.PI) *
                            Mathf.Max(0f, arcHeight);

                particles[i].position = position;
            }

            // 第一批颗粒到达后，粉面开始升高。
            float fill = Mathf.Clamp01(
                (elapsed - seconds) / Mathf.Max(0.001f, launchSpan));

            SetFill(fill);

            yield return null;
        }

        SetFill(1f);
        ClearParticles();
    }

    private void SetFill(float amount)
    {
        if (!savedPowder)
            return;

        amount = Mathf.Clamp01(amount);

        savedPowder.gameObject.SetActive(amount > 0f);

        Vector3 scale = fullPowderScale;
        scale.y *= Mathf.Max(0.001f, amount);

        // Unity Cylinder 的原始高度为 2。
        // 固定底面，只让粉面向上增长。
        float bottomY =
            fullPowderPosition.y - fullPowderScale.y;

        Vector3 position = fullPowderPosition;
        position.y = bottomY + scale.y;

        savedPowder.localScale = scale;
        savedPowder.localPosition = position;
    }

    private void ClearParticles()
    {
        if (groundsAudio)
        {
            groundsAudio.Stop();
            groundsAudio.loop = false;
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

        if (initialized)
        {
            if (savedDrawer)
                savedDrawer.localPosition = closedDrawerPosition;

            SetFill(0f);
        }

        IsPlaying = false;
    }

    private void OnDisable()
    {
        ResetAnimation();
        StopAllCoroutines();
    }
    [ContextMenu("Test Grounds Transfer (Play Mode)")]
    private void TestTransfer()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("请先进入 Play 模式。", this);
            return;
        }

        if (IsPlaying)
        {
            Debug.LogWarning("接粉动画正在播放。", this);
            return;
        }

        Debug.Log("开始单独测试抽屉和飞粉动画。", this);
        StartCoroutine(PlayTransfer());
    }
}