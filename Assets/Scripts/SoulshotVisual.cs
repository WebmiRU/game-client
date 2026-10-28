using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Procedural 3D Soulshot D model based on the original spiritshot reference:
/// a long, faceted blue crystal held by two dark, engraved metal end caps.
/// The object is built along local Y and the world-item view rotates it onto its side.
/// </summary>
[DisallowMultipleComponent]
public sealed class SoulshotVisual : MonoBehaviour
{
    private static Mesh crystalMesh;
    private static Mesh endCapMesh;
    private static Mesh accentRingMesh;
    private static Mesh badgeFrameMesh;
    private static Mesh badgeInsetMesh;
    private static Mesh badgeGemMesh;

    private static Material[] crystalMaterials;
    private static Material[] endCapMaterials;
    private static Material bronzeAccentMaterial;
    private static Material darkEngravingMaterial;
    private static Material badgeGemMaterial;
    private static bool built;

    // Keep these dimensions aligned with the item's collider and ground placement.
    // The silhouette stays inside the existing 0.278 radius / 0.778 half-length.
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
        modelRoot = new GameObject("Soulshot_D_FacetedCrystal_Model");
        modelRoot.transform.SetParent(transform, false);
        modelRoot.transform.localScale = Vector3.one * ModelScale;

        AddMeshPart("BlueFacetedCrystal", crystalMesh, crystalMaterials,
            Vector3.zero, Quaternion.identity, false);

        const float capBase = 0.455f;
        AddMeshPart("ForgedMetalCap_Right", endCapMesh, endCapMaterials,
            new Vector3(0f, capBase, 0f), Quaternion.identity, false);
        AddMeshPart("ForgedMetalCap_Left", endCapMesh, endCapMaterials,
            new Vector3(0f, -capBase, 0f), Quaternion.Euler(0f, 0f, 180f), false);

        // Raised rings catch small highlights like the warm metal collars in the reference.
        for (int end = -1; end <= 1; end += 2)
        {
            AddMeshPart("BronzeCollarRim_" + end + "_A", accentRingMesh,
                bronzeAccentMaterial, new Vector3(0f, end * 0.503f, 0f),
                Quaternion.identity, false);
            AddMeshPart("BronzeCollarRim_" + end + "_B", accentRingMesh,
                bronzeAccentMaterial, new Vector3(0f, end * 0.557f, 0f),
                Quaternion.identity, false);

            // Small engraved diamond marks on both visible sides of each end cap.
            AddEndCapBadge(end, 1);
            AddEndCapBadge(end, -1);
        }
    }

    private void AddEndCapBadge(int endSign, int sideSign)
    {
        Quaternion sideRotation = sideSign > 0
            ? Quaternion.identity
            : Quaternion.Euler(0f, 180f, 0f);

        Vector3 center = new Vector3(0f, endSign * 0.550f, sideSign * 0.280f);
        AddMeshPart("EndCapDiamondFrame_" + endSign + "_" + sideSign, badgeFrameMesh,
            bronzeAccentMaterial, center, sideRotation, false);
        AddMeshPart("EndCapDiamondInset_" + endSign + "_" + sideSign, badgeInsetMesh,
            darkEngravingMaterial, center + new Vector3(0f, 0f, sideSign * 0.0018f),
            sideRotation, false);
        AddMeshPart("EndCapDiamondGem_" + endSign + "_" + sideSign, badgeGemMesh,
            badgeGemMaterial, center + new Vector3(0f, 0f, sideSign * 0.0034f),
            sideRotation, false);
    }

    private void AddMeshPart(string partName, Mesh mesh, Material[] materials,
        Vector3 localPosition, Quaternion localRotation, bool castShadows)
    {
        GameObject part = new GameObject(partName);
        part.transform.SetParent(modelRoot.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;

        MeshFilter filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
    }

    private void AddMeshPart(string partName, Mesh mesh, Material material,
        Vector3 localPosition, Quaternion localRotation, bool castShadows)
    {
        AddMeshPart(partName, mesh, new[] { material }, localPosition, localRotation, castShadows);
    }

    private static void EnsureResources()
    {
        if (built) return;

        // Eight separated radial faces make the blue core read as a faceted crystal,
        // not a smooth pill or a blue tube wrapped in wire.
        Vector2[] crystalProfile =
        {
            new Vector2(0.028f, -0.500f),
            new Vector2(0.105f, -0.462f),
            new Vector2(0.170f, -0.390f),
            new Vector2(0.212f, -0.295f),
            new Vector2(0.235f, -0.155f),
            new Vector2(0.241f,  0.000f),
            new Vector2(0.235f,  0.155f),
            new Vector2(0.212f,  0.295f),
            new Vector2(0.170f,  0.390f),
            new Vector2(0.105f,  0.462f),
            new Vector2(0.028f,  0.500f)
        };
        crystalMesh = BuildProfileMesh("Soulshot_D_BlueFacetedCrystal", crystalProfile, 8);

        // One matching cap mesh is mirrored onto each end. The wide collar stays below
        // the existing capsule collider radius; each end narrows to a forged pointed tip.
        Vector2[] capProfile =
        {
            new Vector2(0.155f, 0.000f),
            new Vector2(0.220f, 0.024f),
            new Vector2(0.273f, 0.050f),
            new Vector2(0.276f, 0.080f),
            new Vector2(0.264f, 0.112f),
            new Vector2(0.238f, 0.154f),
            new Vector2(0.203f, 0.198f),
            new Vector2(0.160f, 0.241f),
            new Vector2(0.112f, 0.282f),
            new Vector2(0.062f, 0.313f),
            new Vector2(0.018f, 0.323f)
        };
        endCapMesh = BuildProfileMesh("Soulshot_D_ForgedEndCap", capProfile, 8);
        accentRingMesh = BuildTorus("Soulshot_D_RaisedBronzeCollar", 0.263f, 0.010f, 48, 8);
        badgeFrameMesh = BuildDiamondFrame("Soulshot_D_EngravedDiamondFrame", 0.038f, 0.030f, 0.008f);
        badgeInsetMesh = BuildSolidDiamond("Soulshot_D_EngravedDiamondInset", 0.024f, 0.021f);
        badgeGemMesh = BuildSolidDiamond("Soulshot_D_EngravedDiamondBlueGem", 0.010f, 0.010f);

        Color[] crystalShades =
        {
            new Color(0.006f, 0.028f, 0.115f),
            new Color(0.012f, 0.085f, 0.340f),
            new Color(0.018f, 0.235f, 0.720f),
            new Color(0.045f, 0.390f, 0.920f),
            new Color(0.022f, 0.155f, 0.530f),
            new Color(0.004f, 0.045f, 0.205f),
            new Color(0.105f, 0.475f, 0.985f),
            new Color(0.010f, 0.105f, 0.430f)
        };
        crystalMaterials = new Material[crystalShades.Length];
        for (int i = 0; i < crystalShades.Length; i++)
        {
            float emission = (i == 3 || i == 6) ? 0.055f : ((i == 2) ? 0.025f : 0.004f);
            crystalMaterials[i] = MakeLitMaterial(
                "Soulshot D Crystal Facet " + i, crystalShades[i],
                0.10f, 0.70f, crystalShades[i] * emission);
        }

        Color[] metalShades =
        {
            new Color(0.075f, 0.065f, 0.052f),
            new Color(0.180f, 0.135f, 0.090f),
            new Color(0.335f, 0.252f, 0.165f),
            new Color(0.465f, 0.362f, 0.235f),
            new Color(0.235f, 0.190f, 0.135f),
            new Color(0.100f, 0.085f, 0.068f),
            new Color(0.295f, 0.222f, 0.145f),
            new Color(0.060f, 0.053f, 0.045f)
        };
        endCapMaterials = new Material[metalShades.Length];
        for (int i = 0; i < metalShades.Length; i++)
        {
            endCapMaterials[i] = MakeLitMaterial(
                "Soulshot D Forged Bronze Facet " + i, metalShades[i],
                0.52f, 0.68f, Color.black);
        }

        bronzeAccentMaterial = MakeLitMaterial(
            "Soulshot D Warm Bronze Edges",
            new Color(0.48f, 0.365f, 0.225f), 0.58f, 0.72f,
            Color.black);
        darkEngravingMaterial = MakeLitMaterial(
            "Soulshot D Engraving Shadow",
            new Color(0.028f, 0.025f, 0.021f), 0.55f, 0.64f, Color.black);
        badgeGemMaterial = MakeLitMaterial(
            "Soulshot D Tiny Blue Engraving Gem",
            new Color(0.08f, 0.42f, 0.95f), 0.18f, 0.88f,
            new Color(0.012f, 0.085f, 0.30f));

        built = true;
    }

    private static Material MakeLitMaterial(string materialName, Color tint,
        float metallic, float smoothness, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader) { name = materialName };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        if (material.HasProperty("_EmissionColor") && emission.maxColorComponent > 0.0001f)
        {
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }

        return material;
    }

    private static Mesh BuildProfileMesh(string meshName, Vector2[] profile, int radialSegments)
    {
        List<Vector3> vertices = new List<Vector3>(profile.Length * radialSegments * 2);
        List<Vector2> uvs = new List<Vector2>(profile.Length * radialSegments * 2);
        List<int>[] faceTriangles = new List<int>[radialSegments];

        for (int face = 0; face < radialSegments; face++)
        {
            faceTriangles[face] = new List<int>((profile.Length - 1) * 6);
            int faceStart = vertices.Count;
            float angleA = face / (float)radialSegments * Mathf.PI * 2f;
            float angleB = (face + 1) / (float)radialSegments * Mathf.PI * 2f;

            for (int row = 0; row < profile.Length; row++)
            {
                float radius = profile[row].x;
                float y = profile[row].y;
                vertices.Add(new Vector3(Mathf.Cos(angleA) * radius, y, Mathf.Sin(angleA) * radius));
                vertices.Add(new Vector3(Mathf.Cos(angleB) * radius, y, Mathf.Sin(angleB) * radius));
                float v = row / (float)(profile.Length - 1);
                uvs.Add(new Vector2(face / (float)radialSegments, v));
                uvs.Add(new Vector2((face + 1) / (float)radialSegments, v));
            }

            for (int row = 0; row < profile.Length - 1; row++)
            {
                int lower = faceStart + row * 2;
                int upper = lower + 2;
                faceTriangles[face].Add(lower);
                faceTriangles[face].Add(upper);
                faceTriangles[face].Add(lower + 1);
                faceTriangles[face].Add(lower + 1);
                faceTriangles[face].Add(upper);
                faceTriangles[face].Add(upper + 1);
            }
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = radialSegments;
        for (int face = 0; face < radialSegments; face++)
        {
            mesh.SetTriangles(faceTriangles[face], face, false);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildTorus(string meshName, float majorRadius, float tubeRadius,
        int radialSegments, int tubeSegments)
    {
        List<Vector3> vertices = new List<Vector3>((radialSegments + 1) * (tubeSegments + 1));
        List<Vector2> uvs = new List<Vector2>(vertices.Capacity);
        List<int> triangles = new List<int>(radialSegments * tubeSegments * 6);

        for (int i = 0; i <= radialSegments; i++)
        {
            float u = i / (float)radialSegments * Mathf.PI * 2f;
            for (int j = 0; j <= tubeSegments; j++)
            {
                float v = j / (float)tubeSegments * Mathf.PI * 2f;
                float radius = majorRadius + tubeRadius * Mathf.Cos(v);
                vertices.Add(new Vector3(radius * Mathf.Cos(u), tubeRadius * Mathf.Sin(v),
                    radius * Mathf.Sin(u)));
                uvs.Add(new Vector2(i / (float)radialSegments, j / (float)tubeSegments));
            }
        }

        for (int i = 0; i < radialSegments; i++)
        {
            for (int j = 0; j < tubeSegments; j++)
            {
                int a = i * (tubeSegments + 1) + j;
                int b = a + tubeSegments + 1;
                // Reverse the winding so the torus normals face outward.
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(a + 1);
                triangles.Add(b + 1);
                triangles.Add(b);
            }
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildDiamondFrame(string meshName, float halfWidth,
        float halfHeight, float borderWidth)
    {
        Vector2[] outer =
        {
            new Vector2(0f, halfHeight),
            new Vector2(-halfWidth, 0f),
            new Vector2(0f, -halfHeight),
            new Vector2(halfWidth, 0f)
        };
        float innerWidth = Mathf.Max(0.001f, halfWidth - borderWidth);
        float innerHeight = Mathf.Max(0.001f, halfHeight - borderWidth);
        Vector2[] inner =
        {
            new Vector2(0f, innerHeight),
            new Vector2(-innerWidth, 0f),
            new Vector2(0f, -innerHeight),
            new Vector2(innerWidth, 0f)
        };

        List<Vector3> vertices = new List<Vector3>(16);
        List<int> triangles = new List<int>(24);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            int start = vertices.Count;
            vertices.Add(new Vector3(outer[i].x, outer[i].y, 0f));
            vertices.Add(new Vector3(outer[next].x, outer[next].y, 0f));
            vertices.Add(new Vector3(inner[next].x, inner[next].y, 0f));
            vertices.Add(new Vector3(inner[i].x, inner[i].y, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildSolidDiamond(string meshName, float halfWidth, float halfHeight)
    {
        Vector3[] vertices =
        {
            Vector3.zero,
            new Vector3(0f, halfHeight, 0f),
            new Vector3(-halfWidth, 0f, 0f),
            new Vector3(0f, -halfHeight, 0f),
            new Vector3(halfWidth, 0f, 0f)
        };
        int[] triangles =
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 1
        };

        Mesh mesh = new Mesh { name = meshName };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
