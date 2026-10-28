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

        // No self-lit point light: it washed the enamel into pale blue and made it read like glass.
        // Gloss comes from the clear-coat-like material and painted studio reflections instead.
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
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
    }

    private static void EnsureResources()
    {
        if (built) return;

        // One smooth capsule only: all braid and collar details are painted into its albedo texture.
        const float capsuleRadius = CapsuleRadius;
        const float halfCylinder = CapsuleHalfCylinder;
        coreMesh = BuildCapsule(capsuleRadius, halfCylinder, 64, 24, 64);

        coreMaterial = MakeMaterial("Soulshot D Glossy Pepsi Blue Aluminum Enamel",
            Color.white, BuildBlueMetalTexture(), 0.42f, 0.94f, false);
        // Deliberately no emission: the blue should come from the enamel pigment, not from glow.
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
        const float fullLength = 2f * (CapsuleHalfCylinder + CapsuleRadius);
        const float collarCenter = 0.255f;
        const float collarHalfWidth = 0.018f;

        float lowerCollarV = 0.5f - collarCenter / fullLength;
        float upperCollarV = 0.5f + collarCenter / fullLength;
        float collarHalfWidthV = collarHalfWidth / fullLength;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = "Soulshot_D_Glossy_Pepsi_Blue_Aluminum_Enamel",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };
        Color32[] pixels = new Color32[width * height];

        // The reference is a glossy cobalt-blue aluminium can: saturated blue enamel,
        // broad reflected light, and one narrow bright vertical glint. It is not glass
        // and should not have pale/cyan translucency or a coarse hammered texture.
        Color deepBlue = new Color(0.004f, 0.055f, 0.245f, 1f);
        Color bodyBlue = new Color(0.006f, 0.115f, 0.405f, 1f);
        Color reflectedBlue = new Color(0.012f, 0.245f, 0.655f, 1f);
        Color highlightBlue = new Color(0.045f, 0.385f, 0.835f, 1f);
        Color glintBlue = new Color(0.36f, 0.62f, 0.94f, 1f);
        Color darkReflection = new Color(0.001f, 0.018f, 0.085f, 1f);

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            float axialMicrotexture = Mathf.Sin(v * Mathf.PI * 2f * 84f) * 0.002f;

            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);

                // Paint two soft studio reflections into the circumferential UV.
                // The broad gradients stay blue; only the thin specular stripe goes
                // silver-blue, like a clean lacquered can under a softbox.
                float primaryDistance = Mathf.Abs(u - 0.245f);
                primaryDistance = Mathf.Min(primaryDistance, 1f - primaryDistance);
                float broadReflection = Mathf.Exp(-0.5f * Mathf.Pow(primaryDistance / 0.145f, 2f));
                float narrowReflection = Mathf.Exp(-0.5f * Mathf.Pow(primaryDistance / 0.030f, 2f));

                float secondaryDistance = Mathf.Abs(u - 0.755f);
                secondaryDistance = Mathf.Min(secondaryDistance, 1f - secondaryDistance);
                float secondaryReflection = Mathf.Exp(-0.5f * Mathf.Pow(secondaryDistance / 0.075f, 2f));

                float darkDistance = Mathf.Abs(u - 0.505f);
                darkDistance = Mathf.Min(darkDistance, 1f - darkDistance);
                float darkStrip = Mathf.Exp(-0.5f * Mathf.Pow(darkDistance / 0.038f, 2f));

                Color color = Color.Lerp(deepBlue, bodyBlue, 0.78f);
                color = Color.Lerp(color, reflectedBlue, broadReflection * 0.82f);
                color = Color.Lerp(color, highlightBlue, secondaryReflection * 0.52f);
                color = Color.Lerp(color, darkReflection, darkStrip * 0.74f);
                color = Color.Lerp(color, highlightBlue, narrowReflection * 0.52f);
                color = Color.Lerp(color, glintBlue, Mathf.Clamp01(narrowReflection * 0.74f));

                // Almost imperceptible fine aluminium grain, not visible scratches or noise.
                color.r += axialMicrotexture * 0.18f;
                color.g += axialMicrotexture * 0.36f;
                color.b += axialMicrotexture;
                color.r = Mathf.Clamp01(color.r);
                color.g = Mathf.Clamp01(color.g);
                color.b = Mathf.Clamp01(color.b);

                // Keep the capsule's two slim silver seams; make them smooth and brushed,
                // rather than the thick white rings that would make the object look plastic.
                float collarDistance = Mathf.Min(
                    Mathf.Abs(v - lowerCollarV),
                    Mathf.Abs(v - upperCollarV));
                if (collarDistance < collarHalfWidthV)
                {
                    float collarShine = 1f - collarDistance / collarHalfWidthV;
                    float silver = Mathf.Clamp01(0.60f + collarShine * 0.22f +
                        0.035f * Mathf.Sin(u * Mathf.PI * 2f * 18f));
                    Color steel = new Color(silver * 0.92f, silver * 0.96f, silver, 1f);
                    color = Color.Lerp(color, steel, Mathf.Clamp01(0.82f + collarShine * 0.16f));
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
