using System;
using UnityEngine;

public sealed class CafeProductHighlighter : MonoBehaviour
{
    [Serializable]
    public class ProductDisplay
    {
        public string productId;
        public Renderer cupRenderer;

        [Min(0)]
        public int materialIndex = 0;

        [NonSerialized] public Material originalMaterial;
        [NonSerialized] public bool cached;
    }

    public Material glowMaterial;
    public ProductDisplay[] products;

    private void Awake()
    {
        CacheOriginalMaterials();
    }

    private void CacheOriginalMaterials()
    {
        if (products == null) return;

        foreach (var product in products)
        {
            if (product == null || product.cached ||
                product.cupRenderer == null)
                continue;

            var materials = product.cupRenderer.sharedMaterials;
            int index = product.materialIndex;

            if (index < 0 || index >= materials.Length)
                continue;

            product.originalMaterial = materials[index];
            product.cached = true;
        }
    }

    public void HighlightProduct(string productId)
    {
        CacheOriginalMaterials();
        ClearHighlight();

        if (glowMaterial == null || products == null)
        {
            Debug.LogWarning("请配置发光材质和商品列表。", this);
            return;
        }

        foreach (var product in products)
        {
            if (product != null && product.cached &&
                product.productId == productId)
            {
                SetMaterial(product, glowMaterial);
                return;
            }
        }

        Debug.LogWarning("未找到商品高亮配置：" + productId, this);
    }

    public void ClearHighlight()
    {
        if (products == null) return;

        foreach (var product in products)
        {
            if (product != null && product.cached)
                SetMaterial(product, product.originalMaterial);
        }
    }

    private static void SetMaterial(
        ProductDisplay product, Material material)
    {
        if (product.cupRenderer == null) return;

        var materials = product.cupRenderer.sharedMaterials;
        int index = product.materialIndex;

        if (index < 0 || index >= materials.Length) return;

        materials[index] = material;
        product.cupRenderer.sharedMaterials = materials;
    }

    private void OnDisable()
    {
        ClearHighlight();
    }
}