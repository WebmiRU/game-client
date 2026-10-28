using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal new inventory window: ornate metal frame and a translucent dark-blue fill only.
/// This deliberately does not load or modify the old inventory screenshot.
/// </summary>
[DisallowMultipleComponent]
public sealed class NewInventoryWindowUI : MonoBehaviour
{
    private const int TextureWidth = 1600;
    private const int TextureHeight = 900;

    private static readonly Color32 FrameShadow = new Color32(16, 12, 9, 250);
    private static readonly Color32 FrameIron = new Color32(48, 43, 36, 255);
    private static readonly Color32 FrameBronze = new Color32(105, 77, 43, 255);
    private static readonly Color32 FrameGold = new Color32(177, 133, 69, 255);
    private static readonly Color32 FrameGlint = new Color32(225, 187, 120, 240);
    private static readonly Color32 FrameDarkInset = new Color32(24, 21, 18, 255);

    public static bool IsOpen { get; private set; }

    private static NewInventoryWindowUI _instance;
    private Texture2D _frameTexture;
    private Texture2D _blueBackgroundTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GetOrCreate();
    }

    private static NewInventoryWindowUI GetOrCreate()
    {
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<NewInventoryWindowUI>();
        if (_instance != null) return _instance;

        GameObject root = new GameObject(nameof(NewInventoryWindowUI));
        DontDestroyOnLoad(root);
        _instance = root.AddComponent<NewInventoryWindowUI>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _frameTexture = CreateOrnateFrameTexture();
        _blueBackgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "NewInventoryDarkBlueBackground",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _blueBackgroundTexture.SetPixel(0, 0, new Color(0.012f, 0.045f, 0.15f, 0.80f));
        _blueBackgroundTexture.Apply(false, true);
        Debug.Log("NewInventoryWindowUI: initialized new frame + translucent blue background.", this);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.iKey.wasPressedThisFrame)
        {
            SetOpen(!IsOpen);
        }
        else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
        }
    }

    public static void ToggleFromHUD()
    {
        NewInventoryWindowUI window = GetOrCreate();
        window.SetOpen(!IsOpen);
    }

    public static void DrawOverlayFromHUD()
    {
        if (_instance != null && IsOpen)
        {
            _instance.DrawWindow();
        }
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
    }

    private void DrawWindow()
    {
        float scale = Mathf.Clamp(
            Mathf.Min(Screen.width / 1920f, Screen.height / 1080f),
            0.55f, 1.35f);

        float width = 1360f * scale;
        float height = width * 9f / 16f;
        float maxWidth = Screen.width - 24f;
        float maxHeight = Screen.height - 24f;
        if (width > maxWidth)
        {
            width = maxWidth;
            height = width * 9f / 16f;
        }
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * 16f / 9f;
        }

        Rect frameRect = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        // The only two rendered layers: the tinted translucent fill and the frame.
        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(frameRect, _blueBackgroundTexture, ScaleMode.StretchToFill, true);
        GUI.DrawTexture(frameRect, _frameTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private static Texture2D CreateOrnateFrameTexture()
    {
        int width = TextureWidth;
        int height = TextureHeight;
        Color32[] pixels = new Color32[width * height];
        Color32 transparent = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;



        // Beveled double rails. The centre remains fully transparent.
        DrawRectOutline(pixels, width, height, 17, 12, FrameShadow, 12);
        DrawRectOutline(pixels, width, height, 23, 18, FrameBronze, 5);
        DrawRectOutline(pixels, width, height, 30, 25, FrameIron, 7);
        DrawRectOutline(pixels, width, height, 35, 30, FrameGold, 2);
        DrawRectOutline(pixels, width, height, 42, 37, FrameDarkInset, 3);
        DrawRectOutline(pixels, width, height, 47, 42, FrameGlint, 1);

        // Ornamental corners are mirrored into all four corners.
        DrawCorner(pixels, width, height, 54, 54, 1, 1);
        DrawCorner(pixels, width, height, width - 54, 54, -1, 1);
        DrawCorner(pixels, width, height, 54, height - 54, 1, -1);
        DrawCorner(pixels, width, height, width - 54, height - 54, -1, -1);

        // Crest centered on the upper rail, with a matching small lower ornament.
        DrawCentralCrest(pixels, width, height, width / 2, height - 34, 1);
        DrawCentralCrest(pixels, width, height, width / 2, 34, -1);

        // Small diamond studs along the side rails.
        DrawDiamond(pixels, width, height, 34, height / 2, 15, FrameBronze, 5);
        DrawDiamond(pixels, width, height, width - 34, height / 2, 15, FrameBronze, 5);
        DrawDiamond(pixels, width, height, 34, height / 2, 8, FrameGlint, 2);
        DrawDiamond(pixels, width, height, width - 34, height / 2, 8, FrameGlint, 2);

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "NewInventoryOrnateFrame",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static void DrawCorner(Color32[] pixels, int width, int height,
        int originX, int originY, int inwardX, int inwardY)
    {
        Vector2 P(float x, float y) => new Vector2(originX + inwardX * x, originY + inwardY * y);

        Vector2[][] curves =
        {
            new[] { P(0, 100), P(0, 190), P(75, 190), P(128, 132) },
            new[] { P(20, 0), P(105, 0), P(180, 55), P(142, 120) },
            new[] { P(37, 28), P(105, 32), P(117, 87), P(78, 112) },
            new[] { P(11, 52), P(52, 51), P(70, 89), P(46, 111) },
            new[] { P(74, 10), P(115, 42), P(142, 77), P(126, 102) }
        };

        foreach (Vector2[] curve in curves)
        {
            DrawBezier(pixels, width, height, curve, FrameShadow, 18);
            DrawBezier(pixels, width, height, curve, FrameBronze, 10);
            DrawBezier(pixels, width, height, curve, FrameGold, 4);
            DrawBezier(pixels, width, height, curve, FrameGlint, 1);
        }

        Vector2 stud = P(83, 75);
        DrawDiamond(pixels, width, height, Mathf.RoundToInt(stud.x), Mathf.RoundToInt(stud.y), 20, FrameShadow, 7);
        DrawDiamond(pixels, width, height, Mathf.RoundToInt(stud.x), Mathf.RoundToInt(stud.y), 13, FrameGold, 3);
        DrawDiamond(pixels, width, height, Mathf.RoundToInt(stud.x), Mathf.RoundToInt(stud.y), 6, FrameGlint, 2);
    }

    private static void DrawCentralCrest(Color32[] pixels, int width, int height, int centerX, int railY, int inwardY)
    {
        int cy = railY - inwardY * 15;
        DrawDiamond(pixels, width, height, centerX, cy, 72, FrameShadow, 18);
        DrawDiamond(pixels, width, height, centerX, cy, 58, FrameBronze, 11);
        DrawDiamond(pixels, width, height, centerX, cy, 43, FrameGold, 6);
        DrawDiamond(pixels, width, height, centerX, cy, 28, FrameDarkInset, 5);
        DrawDiamond(pixels, width, height, centerX, cy, 15, FrameGlint, 3);

        Vector2 left = new Vector2(centerX - 110, railY - inwardY * 7);
        Vector2 right = new Vector2(centerX + 110, railY - inwardY * 7);
        Vector2 top = new Vector2(centerX, railY + inwardY * 30);
        DrawLine(pixels, width, height, left, top, FrameShadow, 15);
        DrawLine(pixels, width, height, top, right, FrameShadow, 15);
        DrawLine(pixels, width, height, left, top, FrameGold, 4);
        DrawLine(pixels, width, height, top, right, FrameGold, 4);
    }

    private static void DrawRectOutline(Color32[] pixels, int width, int height,
        int inset, int unused, Color32 color, int thickness)
    {
        // 'unused' keeps the border helper signature explicit at call sites for future inset tuning.
        float left = inset;
        float right = width - 1 - inset;
        float bottom = inset;
        float top = height - 1 - inset;
        DrawLine(pixels, width, height, new Vector2(left, bottom), new Vector2(right, bottom), color, thickness);
        DrawLine(pixels, width, height, new Vector2(right, bottom), new Vector2(right, top), color, thickness);
        DrawLine(pixels, width, height, new Vector2(right, top), new Vector2(left, top), color, thickness);
        DrawLine(pixels, width, height, new Vector2(left, top), new Vector2(left, bottom), color, thickness);
    }

    private static void DrawDiamond(Color32[] pixels, int width, int height,
        int centerX, int centerY, int size, Color32 color, int thickness)
    {
        Vector2 top = new Vector2(centerX, centerY + size);
        Vector2 right = new Vector2(centerX + size, centerY);
        Vector2 bottom = new Vector2(centerX, centerY - size);
        Vector2 left = new Vector2(centerX - size, centerY);
        DrawLine(pixels, width, height, top, right, color, thickness);
        DrawLine(pixels, width, height, right, bottom, color, thickness);
        DrawLine(pixels, width, height, bottom, left, color, thickness);
        DrawLine(pixels, width, height, left, top, color, thickness);
    }

    private static void DrawBezier(Color32[] pixels, int width, int height,
        Vector2[] p, Color32 color, int thickness)
    {
        Vector2 previous = p[0];
        const int segments = 56;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1f - t;
            Vector2 current =
                u * u * u * p[0] +
                3f * u * u * t * p[1] +
                3f * u * t * t * p[2] +
                t * t * t * p[3];
            DrawLine(pixels, width, height, previous, current, color, thickness);
            previous = current;
        }
    }

    private static void DrawLine(Color32[] pixels, int width, int height,
        Vector2 from, Vector2 to, Color32 color, int thickness)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) * 1.4f));
        int radius = Mathf.Max(0, thickness / 2);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int cx = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
            int cy = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        pixels[y * width + x] = color;
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
            IsOpen = false;
        }

        if (_frameTexture != null) Destroy(_frameTexture);
        if (_blueBackgroundTexture != null) Destroy(_blueBackgroundTexture);
    }
}
