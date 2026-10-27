#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a playable starter landscape without editing the original TerrainData asset.
/// Run from Tools > Game Client > Generate Mountain Ring Terrain.
/// </summary>
public static class WorldTerrainGenerator
{
    private const string OutputFolder = "Assets/GeneratedWorld";
    private const string OriginalTerrainDataPath = "Assets/New Terrain.asset";
    private const string GrassTexturePath = OutputFolder + "/Grass_Procedural.png";
    private const string SoilTexturePath = OutputFolder + "/Soil_Procedural.png";
    private const string RockTexturePath = OutputFolder + "/Rock_Procedural.png";
    private const string GrassLayerPath = OutputFolder + "/Grass.terrainlayer";
    private const string SoilLayerPath = OutputFolder + "/Soil.terrainlayer";
    private const string RockLayerPath = OutputFolder + "/Rock.terrainlayer";
    private const string BoundaryRootName = "WorldBoundaryWalls_Generated";
    private const int TextureResolution = 256;

    private static readonly Hill[] Hills =
    {
        new Hill(0.22f, 0.28f, 0.085f, 0.085f),
        new Hill(0.76f, 0.24f, 0.070f, 0.115f),
        new Hill(0.79f, 0.62f, 0.095f, 0.095f),
        new Hill(0.25f, 0.75f, 0.085f, 0.110f),
        new Hill(0.58f, 0.40f, 0.060f, 0.075f),
        new Hill(0.43f, 0.67f, 0.075f, 0.095f),
        new Hill(0.61f, 0.79f, 0.060f, 0.085f)
    };

    private struct Hill
    {
        public readonly float x;
        public readonly float z;
        public readonly float radius;
        public readonly float height;

        public Hill(float x, float z, float radius, float height)
        {
            this.x = x;
            this.z = z;
            this.radius = radius;
            this.height = height;
        }
    }

    private enum SurfaceKind
    {
        Grass,
        Soil,
        Rock
    }

    [MenuItem("Tools/Game Client/Generate Mountain Ring Terrain")]
    private static void Generate()
    {
        // Work on the active scene only. Camera scripts are deliberately not required:
        // terrain generation must never depend on which object owns the orbit-camera controller.
        Scene activeScene = SceneManager.GetActiveScene();
        PlayerController player = FindInScene<PlayerController>(activeScene);
        CharacterController characterController = FindInScene<CharacterController>(activeScene);

        // Some setups have the CharacterController on the capsule but no PlayerController yet.
        // Preserve/reposition that character too, rather than rejecting the whole scene.
        Transform playerTransform = player != null
            ? player.transform
            : characterController != null ? characterController.transform : null;

        Terrain terrain = null;
        if (Selection.activeGameObject != null &&
            Selection.activeGameObject.scene == activeScene)
        {
            terrain = Selection.activeGameObject.GetComponent<Terrain>();
        }

        if (terrain == null)
        {
            terrain = FindInScene<Terrain>(activeScene);
        }

        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog(
                "Terrain generator",
                "В текущей сцене не найден Terrain с назначенным TerrainData. Открой сцену с ландшафтом и повтори команду.",
                "OK");
            return;
        }

        if (playerTransform == null)
        {
            bool continueWithoutPlayer = EditorUtility.DisplayDialog(
                "Персонаж не найден",
                "Не найден ни PlayerController, ни CharacterController. Ландшафт будет создан, но позицию героя генератор не сможет скорректировать. Камера и остальные объекты сцены не изменятся.",
                "Продолжить",
                "Отмена");

            if (!continueWithoutPlayer)
            {
                return;
            }
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Generate starter world",
            "Будет создан отдельный TerrainData именно для этой сцены. Размер исходного ландшафта сохранится; " +
            "персонаж останется на карте и будет установлен на поверхность. " +
            "После генерации сохрани сцену сочетанием Ctrl+S.",
            "Создать ландшафт",
            "Отмена");

        if (!confirmed)
        {
            return;
        }

        try
        {
            GenerateFor(terrain, playerTransform);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Terrain generator: ошибка",
                "Генерация не завершилась. Подробности есть в Console. Исходный TerrainData не перезаписывается.",
                "OK");
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    private static string GetGeneratedTerrainPath(Terrain terrain)
    {
        string sceneName = SafeAssetName(terrain.gameObject.scene.name);
        string terrainName = SafeAssetName(terrain.gameObject.name);
        return OutputFolder + "/GeneratedWorldTerrain_" + sceneName + "_" + terrainName + ".asset";
    }

    private static string SafeAssetName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace(' ', '_').Replace('+', '_');
    }

    private static void GenerateFor(Terrain terrain, Transform playerTransform)
    {
        EnsureAssetFolder(OutputFolder);

        // If an earlier generator version assigned a shared/generated TerrainData to the scene,
        // recover the real source asset first. Otherwise the "small generated map" would be cloned again.
        TerrainData sourceData = terrain.terrainData;
        string currentDataPath = AssetDatabase.GetAssetPath(sourceData);
        if (!string.IsNullOrEmpty(currentDataPath) &&
            currentDataPath.StartsWith(OutputFolder + "/", StringComparison.OrdinalIgnoreCase))
        {
            TerrainData originalData = AssetDatabase.LoadAssetAtPath<TerrainData>(OriginalTerrainDataPath);
            if (originalData != null)
            {
                sourceData = originalData;
            }
        }

        string terrainAssetPath = GetGeneratedTerrainPath(terrain);
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainAssetPath);

        if (data == null)
        {
            data = UnityEngine.Object.Instantiate(sourceData);
            data.name = Path.GetFileNameWithoutExtension(terrainAssetPath);
            AssetDatabase.CreateAsset(data, terrainAssetPath);
        }
        else if (data != sourceData)
        {
            Undo.RecordObject(data, "Preserve source terrain dimensions");
            data.size = sourceData.size;

            if (data.heightmapResolution != sourceData.heightmapResolution)
            {
                data.heightmapResolution = sourceData.heightmapResolution;
            }

            if (data.alphamapResolution != sourceData.alphamapResolution)
            {
                data.alphamapResolution = sourceData.alphamapResolution;
            }
        }

        Vector2 spawnUv = playerTransform != null
            ? WorldToTerrainUv(terrain, sourceData, playerTransform.position)
            : new Vector2(0.5f, 0.5f);

        Undo.RecordObject(terrain, "Generate mountain ring terrain");
        terrain.terrainData = data;

        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
        {
            terrainCollider = Undo.AddComponent<TerrainCollider>(terrain.gameObject);
        }

        terrainCollider.terrainData = data;

        Texture2D grassTexture = CreatePatternTexture(GrassTexturePath, SurfaceKind.Grass);
        Texture2D soilTexture = CreatePatternTexture(SoilTexturePath, SurfaceKind.Soil);
        Texture2D rockTexture = CreatePatternTexture(RockTexturePath, SurfaceKind.Rock);

        TerrainLayer grass = CreateOrUpdateLayer(GrassLayerPath, grassTexture, 22f, 0.08f);
        TerrainLayer soil = CreateOrUpdateLayer(SoilLayerPath, soilTexture, 18f, 0.06f);
        TerrainLayer rock = CreateOrUpdateLayer(RockLayerPath, rockTexture, 20f, 0.04f);

        data.terrainLayers = new[] { grass, soil, rock };

        GenerateHeights(data, spawnUv);
        GenerateTextureWeights(data);
        CreateBoundaryWalls(terrain, data);
        if (playerTransform != null)
        {
            PlacePlayerOnTerrain(playerTransform, terrain, data, spawnUv);
        }

        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 5f;

        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(terrainCollider);
        if (playerTransform != null)
        {
            EditorUtility.SetDirty(playerTransform);
        }
        AssetDatabase.SaveAssets();
        terrain.Flush();

        Scene scene = terrain.gameObject.scene;
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        SceneView.RepaintAll();
        Debug.Log(
            "World terrain generated. TerrainData: " + terrainAssetPath +
            ". Source dimensions were preserved; player position was restored to the terrain surface.");
        EditorUtility.DisplayDialog(
            "Terrain generated",
            "Готово: холмы, горный периметр, три процедурные текстуры и невидимые ограничители созданы. " +
            "Размер Terrain сохранён, персонаж установлен на поверхность, исходный TerrainData не перезаписан. " +
            "Нажми Ctrl+S, чтобы сохранить изменения сцены.",
            "OK");
    }

    private static void GenerateHeights(TerrainData data, Vector2 spawnUv)
    {
        int resolution = data.heightmapResolution;
        float[,] heights = new float[resolution, resolution];

        for (int z = 0; z < resolution; z++)
        {
            float v = z / (float)(resolution - 1);

            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                float edgeDistance = Mathf.Min(
                    Mathf.Min(u, 1f - u),
                    Mathf.Min(v, 1f - v));

                float broad = FractalNoise(u * 3.7f + 12.4f, v * 3.7f + 8.1f, 4);
                float medium = FractalNoise(u * 10.5f + 31.8f, v * 10.5f + 19.3f, 3);
                float detail = FractalNoise(u * 27f + 6.2f, v * 27f + 42.7f, 2);

                float height = 0.025f
                    + broad * 0.15f
                    + medium * 0.055f
                    + (detail - 0.5f) * 0.018f;

                // Several broad interior hills create both walkable slopes and steeper faces.
                float hillContribution = 0f;
                for (int i = 0; i < Hills.Length; i++)
                {
                    Hill hill = Hills[i];
                    float dx = u - hill.x;
                    float dz = v - hill.z;
                    float distanceSquared = dx * dx + dz * dz;
                    float sigmaSquared = hill.radius * hill.radius * 2f;
                    hillContribution += hill.height * Mathf.Exp(-distanceSquared / sigmaSquared);
                }

                height += hillContribution;

                // Irregular ridge line around the world and a raised outer lip.
                float ridgeVariation = FractalNoise(u * 4.5f + 70.3f, v * 4.5f + 26.9f, 3);
                float ridgeCenter = 0.067f + (ridgeVariation - 0.5f) * 0.032f;
                float ridgeNoise = FractalNoise(u * 9f + 54.2f, v * 9f + 11.6f, 3);
                float ridgeDistance = (edgeDistance - ridgeCenter) / 0.052f;
                float ridge = Mathf.Exp(-ridgeDistance * ridgeDistance) * (0.27f + ridgeNoise * 0.16f);
                float outerLip = (1f - SmoothStep(0f, 0.045f, edgeDistance)) * 0.19f;
                height += ridge + outerLip;

                // Keep a calm spawn area under the player rather than assuming the player is at map centre.
                float centerDx = u - spawnUv.x;
                float centerDz = v - spawnUv.y;
                float centerDistance = Mathf.Sqrt(centerDx * centerDx + centerDz * centerDz);
                float clearing = 1f - SmoothStep(0.08f, 0.18f, centerDistance);
                float clearingHeight = 0.043f + (detail - 0.5f) * 0.006f;
                height = Mathf.Lerp(height, clearingHeight, clearing);

                heights[z, x] = Mathf.Clamp(height, 0.015f, 0.92f);
            }
        }

        data.SetHeights(0, 0, heights);
    }

    private static void GenerateTextureWeights(TerrainData data)
    {
        const int grassIndex = 0;
        const int soilIndex = 1;
        const int rockIndex = 2;

        int resolution = data.alphamapResolution;
        float[,,] weights = new float[resolution, resolution, 3];

        for (int z = 0; z < resolution; z++)
        {
            float v = z / (float)(resolution - 1);

            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                float altitude = data.GetInterpolatedHeight(u, v) / Mathf.Max(1f, data.size.y);
                float slope = data.GetSteepness(u, v);
                float variation = FractalNoise(u * 22f + 3.1f, v * 22f + 17.4f, 2);

                float rockWeight = Mathf.Clamp01(
                    Mathf.InverseLerp(27f, 48f, slope) * 0.9f +
                    Mathf.InverseLerp(0.43f, 0.68f, altitude) * 0.48f);

                float soilWeight = Mathf.Clamp(
                    0.19f + (variation - 0.5f) * 0.22f + (0.25f - altitude) * 0.10f,
                    0.10f,
                    0.31f) * (1f - rockWeight);

                float grassWeight = Mathf.Max(0.01f, 1f - rockWeight - soilWeight);
                float total = grassWeight + soilWeight + rockWeight;

                weights[z, x, grassIndex] = grassWeight / total;
                weights[z, x, soilIndex] = soilWeight / total;
                weights[z, x, rockIndex] = rockWeight / total;
            }
        }

        data.SetAlphamaps(0, 0, weights);
    }

    private static Texture2D CreatePatternTexture(string assetPath, SurfaceKind kind)
    {
        Color low;
        Color high;
        float contrast;

        switch (kind)
        {
            case SurfaceKind.Grass:
                low = new Color(0.09f, 0.16f, 0.045f);
                high = new Color(0.40f, 0.45f, 0.20f);
                contrast = 0.055f;
                break;
            case SurfaceKind.Soil:
                low = new Color(0.17f, 0.105f, 0.05f);
                high = new Color(0.48f, 0.32f, 0.16f);
                contrast = 0.075f;
                break;
            default:
                low = new Color(0.16f, 0.17f, 0.16f);
                high = new Color(0.57f, 0.55f, 0.48f);
                contrast = 0.07f;
                break;
        }

        Texture2D texture = new Texture2D(
            TextureResolution,
            TextureResolution,
            TextureFormat.RGBA32,
            false,
            false);

        texture.name = Path.GetFileNameWithoutExtension(assetPath);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;

        Color[] pixels = new Color[TextureResolution * TextureResolution];
        float seed = (float)((int)kind + 1) * 23.71f;

        for (int y = 0; y < TextureResolution; y++)
        {
            float v = y / (float)TextureResolution;

            for (int x = 0; x < TextureResolution; x++)
            {
                float u = x / (float)TextureResolution;
                float broad = Mathf.PerlinNoise(u * 5.5f + seed, v * 5.5f + seed * 0.71f);
                float medium = Mathf.PerlinNoise(u * 19f + seed * 1.31f, v * 19f + seed * 1.73f);
                float fine = Mathf.PerlinNoise(u * 61f + seed * 2.11f, v * 61f + seed * 2.83f);
                float grain = Mathf.PerlinNoise(u * 143f + seed * 0.37f, v * 143f + seed * 0.53f);

                float value = broad * 0.44f + medium * 0.32f + fine * 0.19f + grain * 0.05f;
                Color color = Color.Lerp(low, high, value);
                float variation = (grain - 0.5f) * contrast;
                color.r = Mathf.Clamp01(color.r + variation);
                color.g = Mathf.Clamp01(color.g + variation * 0.9f);
                color.b = Mathf.Clamp01(color.b + variation * 0.75f);
                color.a = 1f;
                pixels[y * TextureResolution + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (imported == null)
        {
            throw new InvalidOperationException("Could not import generated texture: " + assetPath);
        }

        return imported;
    }

    private static TerrainLayer CreateOrUpdateLayer(
        string assetPath,
        Texture2D texture,
        float tileSize,
        float smoothness)
    {
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(assetPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            layer.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(layer, assetPath);
        }

        layer.diffuseTexture = texture;
        layer.tileSize = new Vector2(tileSize, tileSize);
        layer.tileOffset = Vector2.zero;
        layer.metallic = 0f;
        layer.smoothness = smoothness;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    private static Vector2 WorldToTerrainUv(Terrain terrain, TerrainData data, Vector3 worldPosition)
    {
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 scale = terrain.transform.lossyScale;
        float worldWidth = Mathf.Max(0.001f, data.size.x * Mathf.Abs(scale.x));
        float worldLength = Mathf.Max(0.001f, data.size.z * Mathf.Abs(scale.z));

        float u = (worldPosition.x - terrainPosition.x) / worldWidth;
        float v = (worldPosition.z - terrainPosition.z) / worldLength;

        // Give the character a small margin from the cliffs and boundary colliders.
        return new Vector2(Mathf.Clamp(u, 0.03f, 0.97f), Mathf.Clamp(v, 0.03f, 0.97f));
    }

    private static void PlacePlayerOnTerrain(
        Transform playerTransform,
        Terrain terrain,
        TerrainData data,
        Vector2 terrainUv)
    {
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 scale = terrain.transform.lossyScale;

        CharacterController controller = playerTransform.GetComponent<CharacterController>();
        Vector3 playerPosition = playerTransform.position;
        playerPosition.x = terrainPosition.x + terrainUv.x * data.size.x * Mathf.Abs(scale.x);
        playerPosition.z = terrainPosition.z + terrainUv.y * data.size.z * Mathf.Abs(scale.z);

        float surfaceHeight = terrainPosition.y
            + data.GetInterpolatedHeight(terrainUv.x, terrainUv.y) * Mathf.Abs(scale.y);

        // CharacterController.center may be zero (as it was in the original capsule scene).
        // Place the capsule root so the controller's feet meet the ground instead of burying half of it.
        float rootOffset = 0.05f;
        if (controller != null)
        {
            rootOffset += controller.height * 0.5f - controller.center.y;
        }

        playerPosition.y = surfaceHeight + rootOffset;

        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
        {
            controller.enabled = false;
        }

        playerTransform.position = playerPosition;

        if (controllerWasEnabled)
        {
            controller.enabled = true;
        }
    }

    private static void CreateBoundaryWalls(Terrain terrain, TerrainData data)
    {
        Transform root = terrain.transform.Find(BoundaryRootName);
        if (root == null)
        {
            GameObject rootObject = new GameObject(BoundaryRootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Create world boundary walls");
            root = rootObject.transform;
            root.SetParent(terrain.transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }

        Vector3 size = data.size;
        const float wallThickness = 2f;
        float wallHeight = Mathf.Max(size.y * 3f, 100f);
        float wallCenterY = size.y * 0.5f;

        ConfigureWall(
            root,
            "Boundary_North",
            new Vector3(size.x * 0.5f, wallCenterY, size.z + wallThickness * 0.5f),
            new Vector3(size.x + wallThickness * 2f, wallHeight, wallThickness));
        ConfigureWall(
            root,
            "Boundary_South",
            new Vector3(size.x * 0.5f, wallCenterY, -wallThickness * 0.5f),
            new Vector3(size.x + wallThickness * 2f, wallHeight, wallThickness));
        ConfigureWall(
            root,
            "Boundary_East",
            new Vector3(size.x + wallThickness * 0.5f, wallCenterY, size.z * 0.5f),
            new Vector3(wallThickness, wallHeight, size.z + wallThickness * 2f));
        ConfigureWall(
            root,
            "Boundary_West",
            new Vector3(-wallThickness * 0.5f, wallCenterY, size.z * 0.5f),
            new Vector3(wallThickness, wallHeight, size.z + wallThickness * 2f));
    }

    private static void ConfigureWall(Transform root, string name, Vector3 localPosition, Vector3 size)
    {
        Transform existing = root.Find(name);
        GameObject wall;

        if (existing == null)
        {
            wall = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(wall, "Create world boundary wall");
            wall.transform.SetParent(root, false);
        }
        else
        {
            wall = existing.gameObject;
            Undo.RecordObject(wall.transform, "Update world boundary wall");
        }

        wall.transform.localPosition = localPosition;
        wall.transform.localRotation = Quaternion.identity;
        wall.transform.localScale = Vector3.one;

        BoxCollider box = wall.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = Undo.AddComponent<BoxCollider>(wall);
        }

        Undo.RecordObject(box, "Update world boundary collider");
        box.center = Vector3.zero;
        box.size = size;
        box.isTrigger = false;
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
        string name = Path.GetFileName(folder);

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureAssetFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, name);
    }

    private static float FractalNoise(float x, float y, int octaves)
    {
        float value = 0f;
        float amplitude = 0.5f;
        float totalAmplitude = 0f;

        for (int i = 0; i < octaves; i++)
        {
            value += Mathf.PerlinNoise(x, y) * amplitude;
            totalAmplitude += amplitude;
            x *= 2.03f;
            y *= 2.03f;
            amplitude *= 0.5f;
        }

        return totalAmplitude > 0f ? value / totalAmplitude : 0.5f;
    }

    private static float SmoothStep(float from, float to, float value)
    {
        float t = Mathf.Clamp01((value - from) / (to - from));
        return t * t * (3f - 2f * t);
    }
}
#endif
