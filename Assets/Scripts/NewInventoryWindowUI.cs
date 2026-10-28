using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The new minimal inventory window: a detailed transparent frame image and a
/// translucent dark-blue background. No slots, labels, character preview or panels.
/// </summary>
[DisallowMultipleComponent]
public sealed class NewInventoryWindowUI : MonoBehaviour
{
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
        _frameTexture = Resources.Load<Texture2D>("NewInventoryFrame");
        if (_frameTexture == null)
        {
            Debug.LogError("NewInventoryWindowUI: cannot load Assets/Resources/NewInventoryFrame.png.", this);
        }

        _blueBackgroundTexture = CreateInteriorBackgroundTexture();
        Debug.Log("NewInventoryWindowUI: initialized image frame and translucent dark-blue background.", this);
    }

    private Texture2D CreateInteriorBackgroundTexture()
    {
        const int referenceWidth = 640;
        const int referenceHeight = 360;
        int width = _frameTexture != null ? _frameTexture.width : referenceWidth;
        int height = _frameTexture != null ? _frameTexture.height : referenceHeight;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "NewInventoryDarkBlueBackground",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        // This polygon follows the inside edge of the existing frame.
        // Everything outside it remains transparent, so blue cannot show
        // through the transparent margins around the frame image.
        Vector2[] opening =
        {
            new Vector2(43, 63),
            new Vector2(50, 54),
            new Vector2(66, 49),
            new Vector2(265, 49),
            new Vector2(284, 54),
            new Vector2(299, 60),
            new Vector2(341, 60),
            new Vector2(356, 54),
            new Vector2(375, 49),
            new Vector2(574, 49),
            new Vector2(590, 55),
            new Vector2(597, 64),
            new Vector2(597, 291),
            new Vector2(589, 303),
            new Vector2(573, 309),
            new Vector2(375, 309),
            new Vector2(355, 303),
            new Vector2(341, 298),
            new Vector2(299, 298),
            new Vector2(284, 303),
            new Vector2(265, 309),
            new Vector2(66, 309),
            new Vector2(50, 303),
            new Vector2(43, 291)
        };

        float scaleX = width / (float)referenceWidth;
        float scaleY = height / (float)referenceHeight;
        for (int i = 0; i < opening.Length; i++)
        {
            opening[i] = new Vector2(opening[i].x * scaleX, opening[i].y * scaleY);
        }

        var pixels = new Color32[width * height];
        var blue = new Color32(3, 12, 38, 204);

        // Texture2D pixel rows start at the bottom; the polygon is authored
        // in the same top-left coordinate system as the displayed PNG.
        for (int y = 0; y < height; y++)
        {
            float imageY = height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                if (IsPointInsidePolygon(x + 0.5f, imageY + 0.5f, opening))
                {
                    pixels[y * width + x] = blue;
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static bool IsPointInsidePolygon(float x, float y, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            bool crossesY = (a.y > y) != (b.y > y);
            if (crossesY && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
            {
                inside = !inside;
            }
        }

        return inside;
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

        float width = Mathf.Min(1360f * scale, Screen.width - 24f);
        float height = width * 9f / 16f;
        if (height > Screen.height - 24f)
        {
            height = Screen.height - 24f;
            width = height * 16f / 9f;
        }

        Rect frameRect = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        Color previousColor = GUI.color;
        GUI.color = Color.white;

        // The blue layer is visible only through the transparent areas of the frame.
        GUI.DrawTexture(frameRect, _blueBackgroundTexture, ScaleMode.StretchToFill, true);

        if (_frameTexture != null)
        {
            GUI.DrawTexture(frameRect, _frameTexture, ScaleMode.StretchToFill, true);
        }

        GUI.color = previousColor;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
            IsOpen = false;
        }

        if (_blueBackgroundTexture != null) Destroy(_blueBackgroundTexture);
        // _frameTexture is imported from Resources; Unity owns the asset's lifetime.
    }
}
