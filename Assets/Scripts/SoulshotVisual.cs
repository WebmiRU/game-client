using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedural low-poly model for the blue D-grade Soulshot reference.
/// A single smooth capsule mesh with one dark-blue metallic texture containing the silver diamond weave.
/// No particles, bloom, aura or billboard glow are created.
/// </summary>
[DisallowMultipleComponent]
public sealed class SoulshotVisual : MonoBehaviour
{
    private static Mesh coreMesh;
    private static Material coreMaterial;
    private static bool built;

    // Shared visual/collision dimensions. The capsule lies on its side in the world.
    public const float CapsuleRadius = 0.278f;
    public const float CapsuleHalfCylinder = 0.50f;
    public const float ModelScale = 0.41f;
    public const float GroundClearance = 0.005f;
    public const float GroundCenterOffset = CapsuleRadius * ModelScale + GroundClearance;
    public const float CollisionRadius = CapsuleRadius * ModelScale;
    public const float CollisionHeight = 2f * (CapsuleHalfCylinder + CapsuleRadius) * ModelScale;

    private GameObject modelRoot;

    public void Configure(string itemTemplate)
    {
        if (modelRoot != null) return;
        EnsureResources();

        modelRoot = new GameObject("Soulshot_D_3D_Model");
        modelRoot.transform.SetParent(transform, false);
        modelRoot.transform.localScale = Vector3.one * ModelScale;

        AddPart("DGradeBlueMetalCapsule", coreMesh, coreMaterial, Vector3.zero, false);

        GameObject glowObject = new GameObject("SubtleBlueLight");
        glowObject.transform.SetParent(modelRoot.transform, false);
        Light blueLight = glowObject.AddComponent<Light>();
        blueLight.type = LightType.Point;
        blueLight.color = new Color(0.08f, 0.28f, 1f, 1f);
        // Keep just a subtle blue tint on nearby surfaces; the capsule should read as matte,
        // not as a glowing or highly polished object.
        blueLight.intensity = 0.22f;
        blueLight.range = 0.425f;
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

        // One smooth capsule only: all braid and collar details are painted into its albedo texture.
        const float capsuleRadius = CapsuleRadius;
        const float halfCylinder = CapsuleHalfCylinder;
        coreMesh = BuildCapsule(capsuleRadius, halfCylinder, 64, 24, 64);

        coreMaterial = MakeMaterial("Soulshot D Matte Deep Navy Blue Capsule",
            Color.white, BuildBlueMetalTexture(), 0.10f, 0.40f, false);
        if (coreMaterial.HasProperty("_EmissionColor"))
        {
            // Subtle blue emission, with the main appearance still coming from the metallic texture.
            coreMaterial.SetColor("_EmissionColor", new Color(0.001f, 0.003f, 0.012f, 1f));
            coreMaterial.EnableKeyword("_EMISSION");
        }
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
        const float capsuleRadius = CapsuleRadius;
        const float halfCylinder = CapsuleHalfCylinder;
        const float fullLength = 2f * (halfCylinder + capsuleRadius);
        float turns = braidHalfLength / (Mathf.PI * capsuleRadius);
        const float collarCenter = 0.255f;
        const float collarHalfWidth = 0.018f;

        // The UV covers the complete capsule length. Restrict the weave to the centre strip;
        // narrow silver bands at its edges are texture details, not separate objects.
        float uvStart = (halfCylinder + capsuleRadius - braidHalfLength) / fullLength;
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
                    Mathf.Max(0f, Mathf.Cos((u - 0.22f) * Mathf.PI * 2f)), 10f) * 0.10f;
                float secondaryHighlight = Mathf.Pow(
                    Mathf.Max(0f, Mathf.Cos((u + 0.24f) * Mathf.PI * 2f)), 18f) * 0.035f;
                float fineBrushing = Mathf.Sin(u * Mathf.PI * 2f * 42f + v * 0.4f) * 0.004f;
                float noise = (Noise(x, y) - 0.5f) * 0.010f;
                float shine = Mathf.Clamp01(0.06f + broadHighlight + secondaryHighlight + fineBrushing + noise);

                // Deep, highly saturated navy/royal blue. Even the highlights stay blue,
                // never drifting into cyan or pale sky-blue.
                Color color = new Color(
                    (3f + 4f * shine) / 255f,
                    (7f + 12f * shine) / 255f,
                    (42f + 86f * shine) / 255f,
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

                // The two original silver collars are also painted, so the capsule stays a single mesh.
                float lowerCollarV = 0.5f - collarCenter / fullLength;
                float upperCollarV = 0.5f + collarCenter / fullLength;
                float collarDistance = Mathf.Min(Mathf.Abs(v - lowerCollarV), Mathf.Abs(v - upperCollarV));
                if (collarDistance < collarHalfWidth / fullLength)
                {
                    float collarShine = 1f - collarDistance / (collarHalfWidth / fullLength);
                    float brushed = 0.84f + 0.12f * shine + 0.04f * Mathf.Sin(u * Mathf.PI * 2f * 12f);
                    Color steel = new Color(brushed, brushed * 0.985f, brushed * 0.94f, 1f);
                    color = Color.Lerp(new Color(0.20f, 0.24f, 0.30f, 1f), steel,
                        Mathf.Clamp01(0.72f + collarShine * 0.28f));
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

    private static float Noise(int x, int y)
    {
        float value = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    private static Mesh BuildCapsule(float radius, float halfCylinder,
        int radial, int hemisphere, int bodySegments)
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
            profile.Add(new Vector2(radius, y));
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

}
