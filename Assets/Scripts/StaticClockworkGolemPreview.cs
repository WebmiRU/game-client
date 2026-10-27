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

    private const float PreviewX = 396f;
    private const float PreviewZ = 474f;
    private const float PreviewYaw = 18f;

    private static readonly Dictionary<string, Material> PreviewMaterials =
        new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

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
            Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
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

        GameObject instance = Object.Instantiate(
            model,
            position,
            Quaternion.Euler(0f, PreviewYaw, 0f));
        instance.name = PreviewName;

        ApplyPreviewMaterials(instance);

        // The model is deliberately not given ClockworkGolemAnimator: it should stand
        // still as a scene prop while the user inspects its mesh, materials and silhouette.
        Debug.Log(
            "Clockwork Golem preview spawned near the test enemies at " + position +
            " (static visual prop; not a server entity).");
    }

    private static void ApplyPreviewMaterials(GameObject root)
    {
        // Some Unity render pipelines don't preserve the COLLADA Phong materials.
        // Explicitly assign readable runtime materials so the preview isn't plain white.
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            string partName = renderer.gameObject.name;
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
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

        // The model's moving parts and fasteners are predominantly metal.
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
        if (shader == null) shader = Shader.Find("Unlit/Color");
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
                color = new Color(0.39f, 0.205f, 0.09f, 1f);
                break;
            case "wood-dark":
                color = new Color(0.18f, 0.068f, 0.025f, 1f);
                break;
            case "iron":
                color = new Color(0.28f, 0.31f, 0.32f, 1f);
                metallic = 0.72f;
                smoothness = 0.62f;
                break;
            case "weapon":
                color = new Color(0.42f, 0.45f, 0.46f, 1f);
                metallic = 0.82f;
                smoothness = 0.72f;
                break;
            case "brass":
                color = new Color(0.68f, 0.39f, 0.095f, 1f);
                metallic = 0.76f;
                smoothness = 0.7f;
                break;
            case "leather":
                color = new Color(0.20f, 0.075f, 0.035f, 1f);
                break;
            case "rubber":
                color = new Color(0.055f, 0.06f, 0.062f, 1f);
                smoothness = 0.18f;
                break;
            case "glow":
                color = new Color(1f, 0.42f, 0.055f, 1f);
                smoothness = 0.85f;
                break;
            default:
                color = new Color(0.28f, 0.31f, 0.32f, 1f);
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

        if (key == "glow" && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.6f);
        }

        PreviewMaterials[key] = material;
        return material;
    }
}
