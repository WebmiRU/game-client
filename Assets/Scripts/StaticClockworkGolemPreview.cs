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
        instance.isStatic = true;

        // The model is deliberately not given ClockworkGolemAnimator: it should stand
        // still as a scene prop while the user inspects its mesh, materials and silhouette.
        Debug.Log(
            "Clockwork Golem preview spawned near the test enemies at " + position +
            " (static visual prop; not a server entity).");
    }
}
