using System;
using UnityEngine;
using VirtualCafe.Inventory;
using VirtualCafe.Recommendation;

public sealed class CafeIngredientPresentation : MonoBehaviour
{
    [Serializable]
    public class IngredientView
    {
        public string ingredientId;
        public string displayName;
        public GameObject model;
    }

    [Header("References")]
    public CafeFrontDeskUI frontDesk;
    public CafeMenuSceneAdapter menuAdapter;
    public Camera customerCamera;
    public Transform cupAnchor;
    public Transform ingredientsAnchor;

    [Header("Ingredient models")]
    public IngredientView[] ingredients;

    [Header("Layout")]
    public float heightAboveCup = 0.24f;
    public float spacing = 0.18f;
    public float modelScale = 1f;

    [Header("Labels")]
    public float labelOffset = -0.10f;
    public float labelSize = 0.012f;
    public Color labelColor = new Color(0.22f, 0.14f, 0.08f);

    private Vector3[] initialScales;
    private Font labelFont;

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

        if (ingredients == null)
            ingredients = new IngredientView[0];

        initialScales = new Vector3[ingredients.Length];

        labelFont = Resources.GetBuiltinResource<Font>(
            "LegacyRuntime.ttf"
        );

        for (int i = 0; i < ingredients.Length; i++)
        {
            IngredientView view = ingredients[i];

            if (view == null || !view.model)
                continue;

            initialScales[i] = view.model.transform.localScale;

            CreateLabel(view);
            view.model.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (!frontDesk || !menuAdapter || !customerCamera ||
            !cupAnchor || !ingredientsAnchor)
        {
            HideAll();
            return;
        }

        string mode = frontDesk.InteractionState;

        bool showingOrder = mode == "order";

        bool showingRecommendation =
            mode == "recommendation" &&
            frontDesk.recommendation &&
            frontDesk.recommendation.CurrentRecommendation != null;

        if (!frontDesk.IsInterfaceVisible ||
            (!showingOrder && !showingRecommendation))
        {
            HideAll();
            return;
        }

        var selection = menuAdapter.CurrentSelection;

        if (selection == null)
        {
            HideAll();
            return;
        }

        // Position ingredients above the drink in camera space.
        ingredientsAnchor.position =
            cupAnchor.position +
            customerCamera.transform.up * heightAboveCup;

        ingredientsAnchor.rotation =
            customerCamera.transform.rotation;

        string beanId = selection.BeanId;

        // Black coffee can use its product's fixed bean.
        if (string.IsNullOrEmpty(beanId) &&
            !string.IsNullOrEmpty(selection.MenuItemId) &&
            selection.MenuItemId.StartsWith("black-"))
        {
            beanId =
                "bean-" +
                selection.MenuItemId.Substring("black-".Length);
        }

        if (!frontDesk.IsBeanChosen)
            beanId = null;

        // Current menu: latte contains selectable milk.
        string milkId =
            selection.MenuItemId == "latte" &&
            frontDesk.IsMilkChosen
                ? selection.MilkId
                : null;

        int beanIndex = FindIngredient(beanId);
        int milkIndex = FindIngredient(milkId);

        bool showBoth = beanIndex >= 0 && milkIndex >= 0;

        for (int i = 0; i < ingredients.Length; i++)
        {
            IngredientView view = ingredients[i];

            if (view == null || !view.model)
                continue;

            bool visible = i == beanIndex || i == milkIndex;

            if (view.model.activeSelf != visible)
                view.model.SetActive(visible);

            if (!visible)
                continue;

            float x = 0f;

            if (showBoth)
                x = i == beanIndex ? -spacing * 0.5f
                                   : spacing * 0.5f;

            view.model.transform.localPosition =
                new Vector3(x, 0f, 0f);

            view.model.transform.localRotation =
                Quaternion.identity;

            view.model.transform.localScale =
                initialScales[i] * Mathf.Max(0.01f, modelScale);
        }
    }

    private int FindIngredient(string id)
    {
        if (string.IsNullOrEmpty(id))
            return -1;

        for (int i = 0; i < ingredients.Length; i++)
        {
            IngredientView view = ingredients[i];

            if (view != null && view.model &&
                view.ingredientId == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void CreateLabel(IngredientView view)
    {
        GameObject labelObject =
            new GameObject("IngredientLabel");

        labelObject.transform.SetParent(
            view.model.transform, false
        );

        labelObject.transform.localPosition =
            new Vector3(0f, labelOffset, 0f);

        TextMesh text = labelObject.AddComponent<TextMesh>();

        text.text = view.displayName;
        text.font = labelFont;
        text.fontSize = 48;
        text.characterSize = labelSize;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = labelColor;

        if (labelFont)
        {
            labelObject.GetComponent<MeshRenderer>()
                .sharedMaterial = labelFont.material;
        }
    }

    public void HideAll()
    {
        if (ingredients == null)
            return;

        foreach (IngredientView view in ingredients)
        {
            if (view != null && view.model &&
                view.model.activeSelf)
            {
                view.model.SetActive(false);
            }
        }
    }

    private void OnDisable()
    {
        HideAll();
    }
}