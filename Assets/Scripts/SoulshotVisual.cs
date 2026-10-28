using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedural low-poly model for the blue D-grade Soulshot reference.
/// The item is a bright blue metallic capsule with a recessed center section covered by a flush metal braid.
/// No particles, bloom, aura or billboard glow are created.
/// </summary>
[DisallowMultipleComponent]
public sealed class SoulshotVisual : MonoBehaviour
{
    private static Mesh coreMesh;
    private static Mesh ringMesh;
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

        AddPart("DGradeBlueMetalCapsule", coreMesh, coreMaterial, Vector3.zero, false);
        AddPart("LowerMetalCollar", ringMesh, silverMaterial, new Vector3(0f, -0.255f, 0f), true);
        AddPart("UpperMetalCollar", ringMesh, silverMaterial, new Vector3(0f, 0.255f, 0f), true);

        GameObject glowObject = new GameObject("SubtleBlueLight");
        glowObject.transform.SetParent(modelRoot.transform, false);
        Light blueLight = glowObject.AddComponent<Light>();
        blueLight.type = LightType.Point;
        blueLight.color = new Color(0.08f, 0.28f, 1f, 1f);
        // Intentionally strong enough to read in a scene without a Bloom post-process.
        blueLight.intensity = 1.15f;
        blueLight.range = 1.45f;
        blueLight.shadows = LightShadows.None;
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

        // Opaque blue metallic enamel. The body has a recessed central groove so the braid
        // sits in the groove, with the wire tops level with the capsule's outer radius.
        const float capsuleRadius = 0.278f;
        const float grooveRadius = 0.251f;
        const float grooveHalfLength = 0.275f;
        const float grooveTransition = 0.04f;
        coreMesh = BuildCapsule(capsuleRadius, 0.50f, grooveRadius,
            grooveHalfLength, grooveTransition, 48, 16, 96);
        ringMesh = BuildTorus(0.260f, 0.018f, 48, 10);

        // The diamond braid is painted into the blue enamel texture, not built from thousands
        // of individual wire vertices. It appears on the recessed centre surface.
        coreMaterial = MakeMaterial("Soulshot D Pepsi Blue Metallic Enamel",
            Color.white, BuildBlueMetalTexture(), 0.52f, 0.96f, false);
        if (coreMaterial.HasProperty("_EmissionColor"))
        {
            // A restrained blue self-lit component preserves the luminous blue without looking liquid.
            coreMaterial.SetColor("_EmissionColor", new Color(0.012f, 0.055f, 0.22f, 1f));
            coreMaterial.EnableKeyword("_EMISSION");
        }
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

        // The surface remains opaque; blue emission and lighting provide a subtle metallic glow.
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

    private static Texture2D BuildBlueMetalTexture()
    {
        const int width = 512;
        const int height = 512;
        const int strands = 16;
        const float braidHalfLength = 0.248f;
        const float capsuleRadius = 0.278f;
        const float halfCylinder = 0.50f;
        float turns = braidHalfLength / (Mathf.PI * 0.266f);

        // The capsule UV maps Y from -(halfCylinder + radius) to +(halfCylinder + radius).
        // Restrict the woven pattern to the recessed centre band.
        float uvStart = (halfCylinder + capsuleRadius - braidHalfLength) /
            (2f * (halfCylinder + capsuleRadius));
        float uvEnd = 1f - uvStart;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = "Soulshot_D_Pepsi_Blue_Metal_With_Silver_Diamond_Weave",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };
        Color32[] pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            bool inWeaveBand = v >= uvStart && v <= uvEnd;
            float t = inWeaveBand ? Mathf.InverseLerp(uvStart, uvEnd, v) : 0f;

            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);

                // Dark royal-blue enamel, similar to a Pepsi can; highlights are controlled
                // and narrow, so the surface reads as painted metal rather than liquid.
                float broadHighlight = Mathf.Pow(
                    Mathf.Max(0f, Mathf.Cos((u - 0.22f) * Mathf.PI * 2f)), 14f) * 0.28f;
                float secondaryHighlight = Mathf.Pow(
                    Mathf.Max(0f, Mathf.Cos((u + 0.24f) * Mathf.PI * 2f)), 24f) * 0.11f;
                float fineBrushing = Mathf.Sin(u * Mathf.PI * 2f * 42f + v * 0.4f) * 0.008f;
                float noise = (Noise(x, y) - 0.5f) * 0.018f;
                float shine = Mathf.Clamp01(0.08f + broadHighlight + secondaryHighlight + fineBrushing + noise);

                Color color = new Color(
                    (5f + 10f * shine) / 255f,
                    (24f + 42f * shine) / 255f,
                    (92f + 116f * shine) / 255f,
                    1f);

                if (inWeaveBand)
                {
                    // Two opposing diagonal strand families. Matching their pitch on the
                    // unwrapped cylinder keeps the diamond cells close to square.
                    float phaseA = Mathf.Repeat((u - turns * t) * strands, 1f);
                    float phaseB = Mathf.Repeat((u + turns * t) * strands, 1f);
                    float distA = Mathf.Min(phaseA, 1f - phaseA);
                    float distB = Mathf.Min(phaseB, 1f - phaseB);
                    float lineDistance = Mathf.Min(distA, distB);

                    // A narrow shaded edge and pale centre imitate round polished wires.
                    if (lineDistance < 0.115f)
                    {
                        float edge = Mathf.InverseLerp(0.115f, 0.028f, lineDistance);
                        Color wireEdge = new Color(0.19f, 0.29f, 0.43f, 1f);
                        Color wireMetal = new Color(0.48f, 0.62f, 0.78f, 1f);
                        Color wireHighlight = new Color(0.82f, 0.91f, 1f, 1f);
                        color = Color.Lerp(color, Color.Lerp(wireEdge, wireMetal, edge), 0.96f);

                        if (lineDistance < 0.045f)
                        {
                            color = Color.Lerp(color, wireHighlight,
                                Mathf.InverseLerp(0.045f, 0.0f, lineDistance) * 0.9f);
                        }

                        // Slightly brighter intersections make the crossing pattern legible at game distance.
                        if (distA < 0.075f && distB < 0.075f)
                            color = Color.Lerp(color, new Color(0.88f, 0.94f, 1f, 1f), 0.5f);
                    }
                }

                pixels[y * width + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255),
                    255);
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

    private static Mesh BuildCapsule(float radius, float halfCylinder, float recessedRadius,
        float recessHalfLength, float transitionWidth, int radial, int hemisphere, int bodySegments)
    {
        List<Vector2> profile = new List<Vector2>();
        for (int i = 1; i <= hemisphere; i++)
        {
            float a = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * i / hemisphere;
            profile.Add(new Vector2(radius * Mathf.Cos(a), -halfCylinder + radius * Mathf.Sin(a)));
        }
        for (int i = 1; i <= bodySegments; i++)
        {
            float y = Mathf.Lerp(-halfCylinder, halfCylinder, i / (float)bodySegments);
            float distanceFromCenter = Mathf.Abs(y);
            float localRadius = radius;

            if (distanceFromCenter <= recessHalfLength)
            {
                localRadius = recessedRadius;
            }
            else if (distanceFromCenter < recessHalfLength + transitionWidth)
            {
                float t = (distanceFromCenter - recessHalfLength) / transitionWidth;
                localRadius = Mathf.Lerp(recessedRadius, radius, Mathf.SmoothStep(0f, 1f, t));
            }

            profile.Add(new Vector2(localRadius, y));
        }
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

}
