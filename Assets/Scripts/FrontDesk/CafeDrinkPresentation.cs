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

    private Vector3[] originalScales;
    private GameObject activeModel;
    private string activeId;
    private Coroutine popRoutine;

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

        string mode = frontDesk.InteractionState;

        bool canShow = frontDesk.IsInterfaceVisible &&
            (mode == "order" ||
             (mode == "recommendation" &&
              frontDesk.recommendation &&
              frontDesk.recommendation.CurrentRecommendation != null));

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

        cupAnchor.rotation = Quaternion.Euler(
            0, customerCamera.transform.eulerAngles.y, 0);

        string id = menuAdapter.CurrentSelection.MenuItemId;

        if (id != activeId)
            ShowDrink(id);
    }

    private void ShowDrink(string productId)
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
            activeModel.SetActive(true);

            Vector3 targetScale =
                originalScales[i] * Mathf.Max(0.01f, displayScale);

            popRoutine = StartCoroutine(
                Pop(activeModel.transform, targetScale));

            return;
        }

        Debug.LogWarning("没有配置展示模型：" + productId, this);
    }

    private IEnumerator Pop(Transform target, Vector3 targetScale)
    {
        float duration = Mathf.Max(0.01f, popDuration);
        float elapsed = 0;

        target.localScale = targetScale * 0.1f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            t = 1 - Mathf.Pow(1 - t, 3);

            target.localScale = Vector3.Lerp(
                targetScale * 0.1f, targetScale, t);

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
    }

    private void OnDisable()
    {
        HideDrink();
    }
}