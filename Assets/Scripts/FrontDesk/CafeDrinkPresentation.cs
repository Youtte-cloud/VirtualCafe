using System;
using System.Collections;
using UnityEngine;
using VirtualCafe.Inventory;
using VirtualCafe.Recommendation;

public sealed class CafeDrinkPresentation : MonoBehaviour
{
    [Serializable]
    public class DrinkView
    {
        public string productId;
        public GameObject model;
    }

    public CafeFrontDeskUI frontDesk;
    public CafeMenuSceneAdapter menuAdapter;
    public Camera customerCamera;
    public Transform cupAnchor;
    public DrinkView[] drinks;

    [Header("屏幕中的展示位置")]
    [Range(0, 1)] public float viewportX = 0.82f;
    [Range(0, 1)] public float viewportY = 0.48f;
    public float cameraDistance = 1.2f;

    [Header("弹出动画")]
    public float popDuration = 0.3f;
    public float displayScale = 1.5f;
    [Header("Cup size preview")]
    [Range(1f, 1.2f)] public float largeCupScale = 1.08f;
    public float cupResizeDuration = .22f;

    private Vector3[] originalScales;
    private GameObject activeModel;
    private string activeId;
    private Coroutine popRoutine;
    private Vector3 activeBaseScale;
    private int activeCupSize;

    private void Start()
    {
        if (!frontDesk)
            frontDesk = FindObjectOfType<CafeFrontDeskUI>();

        if (!menuAdapter && frontDesk && frontDesk.reception)
            menuAdapter = frontDesk.reception.menuAdapter;

        if (!customerCamera && frontDesk)
            customerCamera = frontDesk.customerCamera;

        if (!customerCamera)
            customerCamera = Camera.main;

        if (drinks == null)
            drinks = new DrinkView[0];

        originalScales = new Vector3[drinks.Length];

        for (int i = 0; i < drinks.Length; i++)
        {
            if (drinks[i] == null || !drinks[i].model)
                continue;

            originalScales[i] = drinks[i].model.transform.localScale;
            drinks[i].model.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (!frontDesk || !menuAdapter ||
            !customerCamera || !cupAnchor)
        {
            HideDrink();
            return;
        }

        bool canShow = frontDesk.ShouldShowDrinkPreview;

        if (!canShow)
        {
            HideDrink();
            return;
        }

        // 固定在玩家视野右侧，避开当前中央菜单。
        float distance = Mathf.Max(
            cameraDistance, customerCamera.nearClipPlane + 0.2f);

        cupAnchor.position = customerCamera.ViewportToWorldPoint(
            new Vector3(viewportX, viewportY, distance));

        // Match the ingredient preview: both anchors use the complete camera rotation.
        // Individual drink models retain their existing local rotations.
        cupAnchor.rotation = customerCamera.transform.rotation;

        var selection = menuAdapter.CurrentSelection;
        string id = selection.MenuItemId;
        if (id != activeId)
            ShowDrink(id, selection.CupSizeMl);
        else if (activeModel && selection.CupSizeMl != activeCupSize && !frontDesk.IsPreparingOrder)
            ResizeCup(selection.CupSizeMl);
    }

    private void ShowDrink(string productId, int cupSizeMl)
    {
        HideDrink();
        activeId = productId;

        for (int i = 0; i < drinks.Length; i++)
        {
            var view = drinks[i];

            if (view == null || !view.model ||
                view.productId != productId)
                continue;

            activeModel = view.model;
            activeBaseScale = originalScales[i] * Mathf.Max(.01f, displayScale);
            activeCupSize = cupSizeMl;
            Vector3 targetScale = CupScale(cupSizeMl);
            activeModel.transform.localScale = targetScale * .1f;
            activeModel.SetActive(true);
            popRoutine = StartCoroutine(ScaleTo(activeModel.transform, targetScale, popDuration));

            return;
        }

        Debug.LogWarning("没有配置展示模型：" + productId, this);
    }

    private Vector3 CupScale(int cupSizeMl)
    {
        // Always derive from the original 400 ml scale, avoiding growth after repeated toggles.
        return activeBaseScale * (cupSizeMl == 500 ? Mathf.Clamp(largeCupScale, 1f, 1.2f) : 1f);
    }

    private void ResizeCup(int cupSizeMl)
    {
        if (popRoutine != null) StopCoroutine(popRoutine);
        activeCupSize = cupSizeMl;
        popRoutine = StartCoroutine(ScaleTo(activeModel.transform, CupScale(cupSizeMl), cupResizeDuration));
    }

    private IEnumerator ScaleTo(Transform target, Vector3 targetScale, float seconds)
    {
        float duration = Mathf.Max(.01f, seconds);
        float elapsed = 0;
        Vector3 startScale = target.localScale;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = 1 - Mathf.Pow(1 - t, 3);
            target.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        target.localScale = targetScale;
        popRoutine = null;
    }

    public void HideDrink()
    {
        if (popRoutine != null)
        {
            StopCoroutine(popRoutine);
            popRoutine = null;
        }

        if (activeModel)
            activeModel.SetActive(false);

        activeModel = null;
        activeId = null;
        activeCupSize = 0;
    }

    private void OnDisable()
    {
        HideDrink();
    }
}