#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Exports an axis-aligned Unity Terrain into the GWM v1 format for the Go game server.
/// Output: WorldExport/<scene>/<terrain>/manifest.json and terrain.u16.
/// </summary>
public static class WorldTerrainExporter
{
    private const string OutputRootName = "WorldExport";
    private const int MaximumSampleValue = 65535;

    [Serializable]
    private sealed class Manifest
    {
        public string format = "GWM";
        public int formatVersion = 1;
        public string sceneName;
        public string terrainName;
        public string coordinateSystem =
            "Unity world coordinates; +Y is up; Terrain axes are unrotated (+X and +Z).";
        public Float3 origin;
        public Float3 size;
        public int widthSamples;
        public int heightSamples;
        public Float2 sampleSpacing;
        public string encoding = "uint16";
        public string byteOrder = "little-endian";
        public string rowOrder = "z-ascending; x-ascending within each row";
        public string firstSample = "(x=0, z=0)";
        public string heightFormula =
            "worldY = origin.y + (sample / 65535.0) * size.y";
        public string dataFile = "terrain.u16";
        public long dataLengthBytes;
        public string sha256;
        public int minimumSample;
        public int maximumSample;
        public float minimumHeightWorld;
        public float maximumHeightWorld;
    }

    [Serializable]
    private sealed class Float3
    {
        public float x;
        public float y;
        public float z;

        public Float3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    [Serializable]
    private sealed class Float2
    {
        public float x;
        public float z;

        public Float2(float x, float z)
        {
            this.x = x;
            this.z = z;
        }
    }

    [MenuItem("Tools/Game Client/Export World for Go Server")]
    private static void ExportFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Terrain export", "Нет активной загруженной сцены.", "OK");
            return;
        }

        Terrain terrain = null;
        if (Selection.activeGameObject != null &&
            Selection.activeGameObject.scene == scene)
        {
            terrain = Selection.activeGameObject.GetComponent<Terrain>();
        }

        if (terrain == null)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain != null)
                {
                    break;
                }
            }
        }

        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog(
                "Terrain export",
                "В активной сцене не найден Terrain с назначенным TerrainData. " +
                "Открой рабочую сцену и повтори экспорт.",
                "OK");
            return;
        }

        string problem = GetUnsupportedTransformReason(terrain.transform);
        if (!string.IsNullOrEmpty(problem))
        {
            EditorUtility.DisplayDialog(
                "Terrain cannot be exported",
                "Для GWM v1 Terrain должен быть без поворота и с положительным масштабом.\n\n" + problem,
                "OK");
            return;
        }

        string sceneName = SafeName(scene.name);
        string terrainName = SafeName(terrain.gameObject.name);
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDirectory = Path.Combine(projectRoot, OutputRootName, sceneName, terrainName);

        bool confirmed = EditorUtility.DisplayDialog(
            "Export terrain for Go server",
            "Источник: " + terrain.gameObject.name + "\n" +
            "Heightmap: " + terrain.terrainData.heightmapResolution + " x " +
            terrain.terrainData.heightmapResolution + "\n\n" +
            "Файлы будут записаны в:\n" + outputDirectory +
            "\n\nСуществующий экспорт в этой папке будет перезаписан.",
            "Export",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        try
        {
            ExportTerrain(terrain, scene.name, outputDirectory);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Terrain export failed",
                "Не удалось экспортировать Terrain. Подробности смотри в Console.\n\n" +
                exception.Message,
                "OK");
        }
    }

    private static string GetUnsupportedTransformReason(Transform terrainTransform)
    {
        if (Quaternion.Angle(terrainTransform.rotation, Quaternion.identity) > 0.01f)
        {
            return "Текущий поворот: " + terrainTransform.rotation.eulerAngles +
                   ". Сначала убери поворот Terrain, иначе координаты сервера не совпадут с Unity.";
        }

        Vector3 scale = terrainTransform.lossyScale;
        if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
        {
            return "Масштаб Terrain должен быть положительным по всем осям. Текущий масштаб: " + scale;
        }

        return null;
    }

    private static void ExportTerrain(Terrain terrain, string sceneName, string outputDirectory)
    {
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        if (resolution < 2)
        {
            throw new InvalidOperationException("Heightmap resolution must be at least 2.");
        }

        long sampleCount = (long)resolution * resolution;
        if (sampleCount > int.MaxValue / 2)
        {
            throw new InvalidOperationException("Heightmap is too large for a single GWM v1 file.");
        }

        EditorUtility.DisplayProgressBar("Export terrain", "Reading heightmap...", 0.1f);
        try
        {
            float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);
            byte[] payload = new byte[(int)(sampleCount * 2)];
            int minSample = MaximumSampleValue;
            int maxSample = 0;

            // Unity returns heights indexed [z, x]. The binary file stores z rows in ascending
            // order, with x moving fastest; each normalized sample is quantized to uint16 LE.
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int sample = Mathf.Clamp(
                        Mathf.RoundToInt(Mathf.Clamp01(heights[z, x]) * MaximumSampleValue),
                        0,
                        MaximumSampleValue);

                    int offset = ((z * resolution) + x) * 2;
                    payload[offset] = (byte)(sample & 0xFF);
                    payload[offset + 1] = (byte)((sample >> 8) & 0xFF);

                    if (sample < minSample) minSample = sample;
                    if (sample > maxSample) maxSample = sample;
                }

                if (z % 128 == 0)
                {
                    EditorUtility.DisplayProgressBar(
                        "Export terrain",
                        "Encoding heightmap...",
                        0.1f + 0.65f * z / resolution);
                }
            }

            Vector3 origin = terrain.transform.position;
            Vector3 localSize = terrainData.size;
            Vector3 scale = terrain.transform.lossyScale;
            Vector3 worldSize = new Vector3(
                localSize.x * scale.x,
                localSize.y * scale.y,
                localSize.z * scale.z);

            string checksum;
            using (SHA256 sha = SHA256.Create())
            {
                checksum = ToLowerHex(sha.ComputeHash(payload));
            }

            Manifest manifest = new Manifest
            {
                sceneName = sceneName,
                terrainName = terrain.gameObject.name,
                origin = new Float3(origin.x, origin.y, origin.z),
                size = new Float3(worldSize.x, worldSize.y, worldSize.z),
                widthSamples = resolution,
                heightSamples = resolution,
                sampleSpacing = new Float2(
                    worldSize.x / (resolution - 1),
                    worldSize.z / (resolution - 1)),
                dataLengthBytes = payload.LongLength,
                sha256 = checksum,
                minimumSample = minSample,
                maximumSample = maxSample,
                minimumHeightWorld = origin.y + (minSample / (float)MaximumSampleValue) * worldSize.y,
                maximumHeightWorld = origin.y + (maxSample / (float)MaximumSampleValue) * worldSize.y
            };

            string manifestJson = JsonUtility.ToJson(manifest, true) + Environment.NewLine;
            byte[] manifestBytes = new UTF8Encoding(false).GetBytes(manifestJson);

            Directory.CreateDirectory(outputDirectory);
            EditorUtility.DisplayProgressBar("Export terrain", "Writing files...", 0.85f);

            string dataPath = Path.Combine(outputDirectory, "terrain.u16");
            string manifestPath = Path.Combine(outputDirectory, "manifest.json");
            WriteFile(dataPath, payload);
            WriteFile(manifestPath, manifestBytes);

            AssetDatabase.Refresh();

            string relativePath = MakeProjectRelativePath(outputDirectory);
            string result =
                "Terrain exported for Go server.\n\n" +
                "Folder: " + relativePath + "\n" +
                "Heightmap: " + resolution + " x " + resolution + "\n" +
                "Binary data: " + FormatBytes(payload.LongLength) + "\n" +
                "SHA-256: " + checksum;

            Debug.Log(result);
            EditorUtility.DisplayDialog("Terrain exported", result, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void WriteFile(string finalPath, byte[] bytes)
    {
        string tempPath = finalPath + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        StringBuilder builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                ? character
                : '_');
        }

        return builder.ToString();
    }

    private static string ToLowerHex(byte[] bytes)
    {
        StringBuilder builder = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            builder.Append(bytes[i].ToString("x2"));
        }

        return builder.ToString();
    }

    private static string MakeProjectRelativePath(string fullPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        Uri rootUri = new Uri(projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        Uri fileUri = new Uri(fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.##") + " KB";
        return (bytes / (1024f * 1024f)).ToString("0.##") + " MB";
    }
}
#endif
