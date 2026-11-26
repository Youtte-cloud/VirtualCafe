using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class CafeGroundsCup : MonoBehaviour
{
    [Header("Cup dimensions (metres)")]
    [Min(0.01f)]
    public float height = 0.07f;

    [Min(0.01f)]
    public float radius = 0.04f;

    [Min(0.001f)]
    public float wallThickness = 0.003f;

    [Min(0.001f)]
    public float bottomThickness = 0.004f;

    [Range(12, 64)]
    public int segments = 40;

    [Header("Appearance")]
    public Material cupMaterial;

    private Mesh generatedMesh;
    private Material generatedMaterial;

    private void OnEnable()
    {
        BuildCup();
    }

    [ContextMenu("Rebuild Cup")]
    public void BuildCup()
    {
        float h = Mathf.Max(0.01f, height);
        float r = Mathf.Max(0.01f, radius);
        float wall = Mathf.Clamp(wallThickness, 0.001f, r * 0.4f);
        float bottom = Mathf.Clamp(bottomThickness, 0.001f, h * 0.4f);
        int count = Mathf.Clamp(segments, 12, 64);

        // 从底部中心沿截面经过外壁、杯口、内壁，
        // 最后回到杯内底部中心。
        Vector2[] profile =
        {
            new Vector2(0f, 0f),
            new Vector2(r * 0.9f, 0f),
            new Vector2(r, h),
            new Vector2(r - wall, h),
            new Vector2(r * 0.9f - wall, bottom),
            new Vector2(0f, bottom)
        };

        // 每段截面独立，保留杯口和杯底的清晰边缘。
        int surfaceCount = profile.Length - 1;
        int ringLength = count + 1;

        var vertices = new Vector3[surfaceCount * ringLength * 2];
        var triangles = new int[surfaceCount * count * 6];

        int triangleIndex = 0;

        for (int surface = 0; surface < surfaceCount; surface++)
        {
            int start = surface * ringLength * 2;

            for (int i = 0; i <= count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);

                Vector2 lower = profile[surface];
                Vector2 upper = profile[surface + 1];

                vertices[start + i] = new Vector3(
                    x * lower.x, lower.y, z * lower.x);

                vertices[start + ringLength + i] = new Vector3(
                    x * upper.x, upper.y, z * upper.x);
            }

            for (int i = 0; i < count; i++)
            {
                int a = start + i;
                int b = a + 1;
                int d = start + ringLength + i;
                int c = d + 1;

                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = d;
                triangles[triangleIndex++] = c;

                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
            }
        }

        Mesh oldMesh = generatedMesh;

        generatedMesh = new Mesh
        {
            name = "Generated Grounds Cup",
            hideFlags = HideFlags.DontSave
        };

        generatedMesh.vertices = vertices;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = generatedMesh;

        if (oldMesh)
            Release(oldMesh);

        if (cupMaterial)
        {
            GetComponent<MeshRenderer>().sharedMaterial = cupMaterial;
        }
        else
        {
            if (!generatedMaterial)
            {
                Shader shader = Shader.Find("Standard");

                if (shader)
                {
                    generatedMaterial = new Material(shader)
                    {
                        name = "Grounds Cup Metal",
                        hideFlags = HideFlags.DontSave
                    };

                    generatedMaterial.color =
                        new Color(0.58f, 0.61f, 0.62f);

                    generatedMaterial.SetFloat("_Metallic", 0.65f);
                    generatedMaterial.SetFloat("_Glossiness", 0.45f);
                }
            }

            if (generatedMaterial)
            {
                GetComponent<MeshRenderer>().sharedMaterial =
                    generatedMaterial;
            }
        }
    }

    private static void Release(Object target)
    {
        if (!target)
            return;

        if (Application.isPlaying)
            Object.Destroy(target);
        else
            Object.DestroyImmediate(target);
    }

    private void OnDestroy()
    {
        Release(generatedMesh);
        Release(generatedMaterial);
    }
}