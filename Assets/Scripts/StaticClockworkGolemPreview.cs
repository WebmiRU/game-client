using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one non-animated, client-only preview of the Clockwork Golem near the seeded test enemies.
/// This is a visual prop only: it has no server entity ID, combat logic or gameplay collider.
/// </summary>
public static class StaticClockworkGolemPreview
{
    private const string PreviewName = "ClockworkGolem_StaticPreview";
    private const string ResourceName = "ClockworkGolem";
    private const int TextureResolution = 128;

    private const float PreviewX = 396f;
    private const float PreviewZ = 474f;
    private const float PreviewYaw = 18f;

    private static readonly Dictionary<string, Material> PreviewMaterials =
        new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D> PreviewTextures =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Mesh, Mesh> PreviewMeshes =
        new Dictionary<Mesh, Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SpawnPreview()
    {
        if (GameObject.Find(PreviewName) != null)
        {
            return;
        }

        GameObject model = Resources.Load<GameObject>(ResourceName);
        if (model == null)
        {
            Debug.LogWarning(
                "Clockwork Golem preview: Resources/" + ResourceName +
                ".dae was not imported as a GameObject; preview skipped.");
            return;
        }

        Vector3 position = new Vector3(PreviewX, 0f, PreviewZ);
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Terrain[] terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (terrains.Length > 0)
            {
                terrain = terrains[0];
            }
        }

        if (terrain != null && terrain.terrainData != null)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 scale = terrain.transform.lossyScale;
            float width = data.size.x * Mathf.Abs(scale.x);
            float length = data.size.z * Mathf.Abs(scale.z);
            float nx = width > 0f ? (position.x - origin.x) / width : -1f;
            float nz = length > 0f ? (position.z - origin.z) / length : -1f;
            if (nx >= 0f && nx <= 1f && nz >= 0f && nz <= 1f)
            {
                position.y = origin.y +
                    data.GetInterpolatedHeight(nx, nz) * Mathf.Abs(scale.y);
            }
        }

        GameObject instance = UnityEngine.Object.Instantiate(
            model,
            position,
            Quaternion.Euler(0f, PreviewYaw, 0f));
        instance.name = PreviewName;
        instance.isStatic = true;

        ApplyPreviewMaterials(instance);

        Debug.Log(
            "Clockwork Golem preview spawned near the test enemies at " + position +
            " (static visual prop; not a server entity).");
    }

    private static void ApplyPreviewMaterials(GameObject root)
    {
        // The COLLADA file has Phong material definitions but no image textures or UVs.
        // Generate UVs and small procedural texture maps so wood and metal don't look flat.
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                EnsurePreviewUVs(meshFilter);
            }

            string partName = renderer.gameObject.name;
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                partName += " " + meshFilter.sharedMesh.name;
            }

            Material material = GetOrCreateMaterial(GetMaterialKey(partName));
            if (material == null)
            {
                continue;
            }

            Material[] slots = renderer.sharedMaterials;
            if (slots == null || slots.Length == 0)
            {
                renderer.sharedMaterial = material;
                continue;
            }

            Material[] replacements = new Material[slots.Length];
            for (int i = 0; i < replacements.Length; i++)
            {
                replacements[i] = material;
            }

            renderer.sharedMaterials = replacements;
        }
    }

    private static void EnsurePreviewUVs(MeshFilter meshFilter)
    {
        Mesh source = meshFilter.sharedMesh;
        if (source == null)
        {
            return;
        }

        Vector2[] existingUVs = source.uv;
        if (existingUVs != null && existingUVs.Length == source.vertexCount &&
            HasUsableUVs(existingUVs))
        {
            return;
        }

        if (PreviewMeshes.TryGetValue(source, out Mesh cachedMesh) && cachedMesh != null)
        {
            meshFilter.sharedMesh = cachedMesh;
            return;
        }

        Vector3[] vertices = source.vertices;
        if (vertices == null || vertices.Length == 0)
        {
            return;
        }

        Mesh mappedMesh = UnityEngine.Object.Instantiate(source);
        mappedMesh.name = source.name + "_GolemPreviewUV";

        Vector3 min = vertices[0];
        Vector3 max = vertices[0];
        for (int i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i]);
            max = Vector3.Max(max, vertices[i]);
        }

        Vector3 size = max - min;
        int uAxis;
        int vAxis;

        // Use the two widest dimensions for a simple, UV-independent planar projection.
        if (size.x <= size.y && size.x <= size.z)
        {
            uAxis = 1;
            vAxis = 2;
        }
        else if (size.y <= size.x && size.y <= size.z)
        {
            uAxis = 0;
            vAxis = 2;
        }
        else
        {
            uAxis = 0;
            vAxis = 1;
        }

        float minU = GetAxisValue(min, uAxis);
        float minV = GetAxisValue(min, vAxis);
        float spanU = Mathf.Max(0.0001f, GetAxisValue(max, uAxis) - minU);
        float spanV = Mathf.Max(0.0001f, GetAxisValue(max, vAxis) - minV);

        Vector2[] generatedUVs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            generatedUVs[i] = new Vector2(
                (GetAxisValue(vertices[i], uAxis) - minU) / spanU,
                (GetAxisValue(vertices[i], vAxis) - minV) / spanV);
        }

        mappedMesh.uv = generatedUVs;
        PreviewMeshes[source] = mappedMesh;
        meshFilter.sharedMesh = mappedMesh;
    }

    private static bool HasUsableUVs(Vector2[] uvs)
    {
        if (uvs.Length < 3)
        {
            return false;
        }

        Vector2 first = uvs[0];
        for (int i = 1; i < uvs.Length; i++)
        {
            if ((uvs[i] - first).sqrMagnitude > 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private static float GetAxisValue(Vector3 value, int axis)
    {
        if (axis == 0) return value.x;
        if (axis == 1) return value.y;
        return value.z;
    }

    private static string GetMaterialKey(string partName)
    {
        string name = partName.ToLowerInvariant();

        if (name.Contains("glow") || name.Contains("eye") || name.Contains("optic"))
            return "glow";
        if (name.Contains("leather") || name.Contains("strap"))
            return "leather";
        if (name.Contains("tire") || name.Contains("rubber"))
            return "rubber";
        if (name.Contains("brass") || name.Contains("gear") || name.Contains("copper"))
            return "brass";
        if (name.Contains("maul") || name.Contains("hammer") || name.Contains("blade") ||
            name.Contains("spike") || name.Contains("strikingface"))
            return "weapon";
        if (name.Contains("dark"))
            return "wood-dark";
        if (name.Contains("grain") || name.Contains("stave") || name.Contains("spoke") ||
            name.Contains("barrel") || name.Contains("wood") || name.Contains("body"))
            return "wood";
        if (name.Contains("iron") || name.Contains("rivet") || name.Contains("metal") ||
            name.Contains("wheel") || name.Contains("axle") || name.Contains("pedal") ||
            name.Contains("hoop") || name.Contains("rim"))
            return "iron";

        return "iron";
    }

    private static Material GetOrCreateMaterial(string key)
    {
        if (PreviewMaterials.TryGetValue(key, out Material cached) && cached != null)
        {
            return cached;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            Debug.LogWarning("Clockwork Golem preview: no compatible shader was found.");
            return null;
        }

        Material material = new Material(shader);
        material.name = "GolemPreview_" + key;

        Color color;
        float metallic = 0f;
        float smoothness = 0.28f;
        switch (key)
        {
            case "wood":
                color = new Color(0.48f, 0.26f, 0.115f, 1f);
                break;
            case "wood-dark":
                color = new Color(0.25f, 0.105f, 0.04f, 1f);
                break;
            case "iron":
                color = new Color(0.39f, 0.42f, 0.43f, 1f);
                metallic = 0.72f;
                smoothness = 0.62f;
                break;
            case "weapon":
                color = new Color(0.52f, 0.55f, 0.56f, 1f);
                metallic = 0.82f;
                smoothness = 0.72f;
                break;
            case "brass":
                color = new Color(0.78f, 0.49f, 0.16f, 1f);
                metallic = 0.76f;
                smoothness = 0.7f;
                break;
            case "leather":
                color = new Color(0.30f, 0.12f, 0.055f, 1f);
                break;
            case "rubber":
                color = new Color(0.11f, 0.12f, 0.125f, 1f);
                smoothness = 0.18f;
                break;
            case "glow":
                color = new Color(1f, 0.42f, 0.055f, 1f);
                smoothness = 0.85f;
                break;
            default:
                color = new Color(0.39f, 0.42f, 0.43f, 1f);
                break;
        }

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        else if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);

        Texture2D texture = GetOrCreateTexture(key);
        if (texture != null)
        {
            material.mainTexture = texture;
        }

        if (key == "glow" && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.6f);
        }

        PreviewMaterials[key] = material;
        return material;
    }

    private static Texture2D GetOrCreateTexture(string key)
    {
        if (PreviewTextures.TryGetValue(key, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        Texture2D texture = new Texture2D(
            TextureResolution, TextureResolution, TextureFormat.RGBA32, true);
        texture.name = "GolemPreviewTexture_" + key;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;

        int seed = 173 + key.Length * 37;
        for (int i = 0; i < key.Length; i++)
        {
            seed = seed * 31 + key[i];
        }

        System.Random random = new System.Random(seed);
        Color[] pixels = new Color[TextureResolution * TextureResolution];

        for (int y = 0; y < TextureResolution; y++)
        {
            float v = (float)y / TextureResolution;
            for (int x = 0; x < TextureResolution; x++)
            {
                float u = (float)x / TextureResolution;
                float noise = (float)random.NextDouble();
                float brightness;

                switch (key)
                {
                    case "wood":
                    case "wood-dark":
                        float grain = 0.5f + 0.5f * Mathf.Sin(
                            v * 235f + Mathf.Sin(u * 23f) * 3.1f +
                            Mathf.Sin(u * 61f) * 0.8f);
                        brightness = 0.65f + grain * 0.27f + noise * 0.08f;
                        break;
                    case "leather":
                        float crosshatch = Mathf.Sin(u * 190f) * Mathf.Sin(v * 190f);
                        brightness = 0.78f + crosshatch * 0.08f + noise * 0.14f;
                        break;
                    case "rubber":
                        brightness = 0.72f + noise * 0.2f;
                        break;
                    case "glow":
                        brightness = 0.9f + noise * 0.1f;
                        break;
                    default:
                        float scratch = Mathf.Abs(Mathf.Sin(u * 250f + v * 17f));
                        brightness = 0.68f + noise * 0.22f + scratch * 0.12f;
                        break;
                }

                brightness = Mathf.Clamp(brightness, 0.5f, 1.08f);
                pixels[y * TextureResolution + x] =
                    new Color(brightness, brightness, brightness, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        PreviewTextures[key] = texture;
        return texture;
    }
}
