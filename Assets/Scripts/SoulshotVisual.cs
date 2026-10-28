using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedural low-poly model for the blue D-grade Soulshot reference.
/// The item itself is a glass capsule, blue glass core and metallic diamond braid.
/// No particles, bloom, aura or billboard glow are created.
/// </summary>
[DisallowMultipleComponent]
public sealed class SoulshotVisual : MonoBehaviour
{
    private static Mesh glassMesh;
    private static Mesh coreMesh;
    private static Mesh ringMesh;
    private static Mesh braidMesh;
    private static Material glassMaterial;
    private static Material coreMaterial;
    private static Material silverMaterial;
    private static bool built;

    private GameObject modelRoot;

    public void Configure(string itemTemplate)
    {
        if (modelRoot != null) return;
        EnsureResources();

        modelRoot = new GameObject("Soulshot_D_3D_Model");
        modelRoot.transform.SetParent(transform, false);
        modelRoot.transform.localScale = Vector3.one * 0.82f;

        AddPart("BlueGlassCore", coreMesh, coreMaterial, Vector3.zero, false);
        AddPart("OuterGlassCapsule", glassMesh, glassMaterial, Vector3.zero, false);
        AddPart("CrossedDiamondMetalWeave", braidMesh, silverMaterial, Vector3.zero, true);
        AddPart("LowerMetalCollar", ringMesh, silverMaterial, new Vector3(0f, -0.255f, 0f), true);
        AddPart("UpperMetalCollar", ringMesh, silverMaterial, new Vector3(0f, 0.255f, 0f), true);
    }

    private void AddPart(string partName, Mesh mesh, Material material, Vector3 localPosition, bool castShadows)
    {
        GameObject part = new GameObject(partName);
        part.transform.SetParent(modelRoot.transform, false);
        part.transform.localPosition = localPosition;
        MeshFilter filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
    }

    private static void EnsureResources()
    {
        if (built) return;

        glassMesh = BuildCapsule(0.30f, 0.25f, 40, 12, 6);
        coreMesh = BuildCapsule(0.278f, 0.22f, 40, 12, 6);
        ringMesh = BuildTorus(0.292f, 0.022f, 48, 10);
        braidMesh = BuildBraid(0.311f, 0.248f, 6, 1.12f, 0.012f, 112, 6);

        coreMaterial = MakeMaterial("Soulshot D Blue Glass", new Color(0.055f, 0.19f, 0.48f, 1f),
            BuildBlueGlassTexture(), 0.24f, 0.91f, false);
        glassMaterial = MakeMaterial("Soulshot Clear Blue Glass", new Color(0.42f, 0.69f, 0.93f, 0.17f),
            null, 0.18f, 0.96f, true);
        silverMaterial = MakeMaterial("Soulshot Silver Diamond Braid", new Color(0.82f, 0.84f, 0.81f, 1f),
            BuildSilverTexture(), 0.82f, 0.79f, false);
        built = true;
    }

    private static Material MakeMaterial(string materialName, Color tint, Texture2D albedo,
        float metallic, float smoothness, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material material = new Material(shader) { name = materialName };

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (albedo != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
        }
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        // The reference halo is deliberately omitted; the blue stays inside the glass.
        material.DisableKeyword("_EMISSION");
        if (transparent)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        return material;
    }

    private static Texture2D BuildBlueGlassTexture()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "Soulshot_D_Blue_Glass_Albedo",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float noise = (Noise(x, y) - 0.5f) * 0.18f;
                float streak = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI * 14f + 0.8f)), 12f) * 0.3f;
                float glint = Mathf.Pow(Mathf.Max(0f, Mathf.Sin((u * 1.7f + v * 0.25f) * Mathf.PI * 2f)), 24f) * 0.18f;
                float edgeShade = 0.68f + 0.32f * Mathf.Sin(v * Mathf.PI);
                float value = Mathf.Clamp01((0.78f + noise + streak + glint) * edgeShade);
                pixels[y * size + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(24f * value), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(82f * value), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(188f * value), 0, 255), 255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static Texture2D BuildSilverTexture()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "Soulshot_D_Brushed_Silver_Albedo",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float noise = (Noise(x, y) - 0.5f) * 0.13f;
                float brush = Mathf.Sin(y * 2.4f + Mathf.Sin(x * 0.11f) * 1.3f) * 0.045f;
                byte channel = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.88f + noise + brush));
                pixels[y * size + x] = new Color32(channel, channel, channel, 255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static float Noise(int x, int y)
    {
        float value = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    private static Mesh BuildCapsule(float radius, float halfCylinder, int radial, int hemisphere, int bodySegments)
    {
        List<Vector2> profile = new List<Vector2>();
        for (int i = 1; i <= hemisphere; i++)
        {
            float a = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * i / hemisphere;
            profile.Add(new Vector2(radius * Mathf.Cos(a), -halfCylinder + radius * Mathf.Sin(a)));
        }
        for (int i = 1; i <= bodySegments; i++)
            profile.Add(new Vector2(radius, Mathf.Lerp(-halfCylinder, halfCylinder, i / (float)bodySegments)));
        for (int i = 1; i < hemisphere; i++)
        {
            float a = Mathf.PI * 0.5f * i / hemisphere;
            profile.Add(new Vector2(radius * Mathf.Cos(a), halfCylinder + radius * Mathf.Sin(a)));
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int row = 0; row < profile.Count; row++)
        {
            float r = Mathf.Max(0.0001f, profile[row].x);
            float y = profile[row].y;
            float v = Mathf.InverseLerp(-halfCylinder - radius, halfCylinder + radius, y);
            for (int segment = 0; segment < radial; segment++)
            {
                float a = segment / (float)radial * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                uv.Add(new Vector2(segment / (float)radial, v));
            }
        }
        for (int row = 0; row < profile.Count - 1; row++)
        {
            int current = row * radial;
            int nextRow = (row + 1) * radial;
            for (int segment = 0; segment < radial; segment++)
            {
                int next = (segment + 1) % radial;
                triangles.Add(current + segment);
                triangles.Add(nextRow + segment);
                triangles.Add(current + next);
                triangles.Add(current + next);
                triangles.Add(nextRow + segment);
                triangles.Add(nextRow + next);
            }
        }
        int bottomPole = vertices.Count;
        vertices.Add(new Vector3(0f, -halfCylinder - radius, 0f));
        uv.Add(new Vector2(0.5f, 0f));
        int topPole = vertices.Count;
        vertices.Add(new Vector3(0f, halfCylinder + radius, 0f));
        uv.Add(new Vector2(0.5f, 1f));
        int lastRow = (profile.Count - 1) * radial;
        for (int segment = 0; segment < radial; segment++)
        {
            int next = (segment + 1) % radial;
            triangles.Add(bottomPole);
            triangles.Add(segment);
            triangles.Add(next);
            triangles.Add(topPole);
            triangles.Add(lastRow + next);
            triangles.Add(lastRow + segment);
        }

        Mesh mesh = new Mesh { name = "Soulshot_Capsule_Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildTorus(float majorRadius, float tubeRadius, int majorSegments, int tubeSegments)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int major = 0; major <= majorSegments; major++)
        {
            float u = major / (float)majorSegments;
            float a = u * Mathf.PI * 2f;
            for (int tube = 0; tube <= tubeSegments; tube++)
            {
                float v = tube / (float)tubeSegments;
                float b = v * Mathf.PI * 2f;
                float r = majorRadius + tubeRadius * Mathf.Cos(b);
                vertices.Add(new Vector3(r * Mathf.Cos(a), tubeRadius * Mathf.Sin(b), r * Mathf.Sin(a)));
                uv.Add(new Vector2(u, v));
            }
        }
        int rowWidth = tubeSegments + 1;
        for (int major = 0; major < majorSegments; major++)
        {
            for (int tube = 0; tube < tubeSegments; tube++)
            {
                int a = major * rowWidth + tube;
                int b = (major + 1) * rowWidth + tube;
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(a + 1);
                triangles.Add(b + 1);
                triangles.Add(b);
            }
        }
        Mesh mesh = new Mesh { name = "Soulshot_Metal_Collar_Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildBraid(float radius, float halfLength, int strands, float turns,
        float tubeRadius, int pathSegments, int tubeSides)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int direction = 0; direction < 2; direction++)
        {
            for (int strand = 0; strand < strands; strand++)
            {
                float phase = strand / (float)strands * Mathf.PI * 2f;
                for (int i = 0; i <= pathSegments; i++)
                {
                    float t = i / (float)pathSegments;
                    Vector3 center = BraidPoint(t, phase, direction, radius, halfLength, turns);
                    float t0 = Mathf.Max(0f, t - 0.002f);
                    float t1 = Mathf.Min(1f, t + 0.002f);
                    Vector3 tangent = (BraidPoint(t1, phase, direction, radius, halfLength, turns)
                        - BraidPoint(t0, phase, direction, radius, halfLength, turns)).normalized;
                    Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
                    if (side.sqrMagnitude < 0.001f) side = Vector3.right;
                    Vector3 up = Vector3.Cross(tangent, side).normalized;
                    int rowStart = vertices.Count;
                    for (int tube = 0; tube < tubeSides; tube++)
                    {
                        float a = tube / (float)tubeSides * Mathf.PI * 2f;
                        Vector3 normal = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        vertices.Add(center + normal * tubeRadius);
                        uv.Add(new Vector2(t, tube / (float)tubeSides));
                    }
                    if (i == 0) continue;
                    int prev = rowStart - tubeSides;
                    for (int tube = 0; tube < tubeSides; tube++)
                    {
                        int next = (tube + 1) % tubeSides;
                        triangles.Add(prev + tube);
                        triangles.Add(prev + next);
                        triangles.Add(rowStart + tube);
                        triangles.Add(prev + next);
                        triangles.Add(rowStart + next);
                        triangles.Add(rowStart + tube);
                    }
                }
            }
        }
        Mesh mesh = new Mesh { name = "Soulshot_Crossed_Diamond_Braid_Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 BraidPoint(float t, float phase, int direction, float radius,
        float halfLength, float turns)
    {
        float a = phase + (direction == 0 ? 1f : -1f) * t * turns * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(a) * radius, Mathf.Lerp(-halfLength, halfLength, t), Mathf.Sin(a) * radius);
    }
}
