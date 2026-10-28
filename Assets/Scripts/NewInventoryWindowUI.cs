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

        // One solid pixel is stretched only across the inner opening of the frame.
        // No mask image is needed; the frame itself remains the unchanged top layer.
        _blueBackgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "NewInventoryDarkBlueBackground",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _blueBackgroundTexture.SetPixel(0, 0, Color.white);
        _blueBackgroundTexture.Apply(false, true);
        Debug.Log("NewInventoryWindowUI: initialized image frame and translucent dark-blue background.", this);
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

        // The frame is 640x360. This inset matches its visible inner opening;
        // the blue is a plain rectangle, not a separate polygon or mask image.
        Rect backgroundRect = new Rect(
            frameRect.x + frameRect.width * (31f / 640f),
            frameRect.y + frameRect.height * (44f / 360f),
            frameRect.width * (578f / 640f),
            frameRect.height * (280f / 360f));

        if (_blueBackgroundTexture != null)
        {
            GUI.color = new Color(0.012f, 0.045f, 0.15f, 0.80f);
            GUI.DrawTexture(backgroundRect, _blueBackgroundTexture, ScaleMode.StretchToFill, true);

            // The inventory grid occupies the right two-thirds of the inner opening.
            DrawInventoryGrid(frameRect);
        }

        if (_frameTexture != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(frameRect, _frameTexture, ScaleMode.StretchToFill, true);
        }

        GUI.color = previousColor;
    }

    private void DrawInventoryGrid(Rect frameRect)
    {
        // The grid occupies the right two-thirds of the frame's inner opening.
        // Cells are flat and separated only by thin lines; the whole grid has
        // one shared outer border rather than individual beveled cell borders.
        const float innerLeft = 31f;
        const float innerWidth = 578f;
        const float gridTop = 66f;
        const float gridHeight = 232f;
        const int columns = 10;
        const int rows = 7;

        float scaleX = frameRect.width / 640f;
        float scaleY = frameRect.height / 360f;
        float areaX = innerLeft + innerWidth / 3f;
        float areaWidth = innerWidth * 2f / 3f;

        Rect gridRect = new Rect(
            frameRect.x + areaX * scaleX,
            frameRect.y + gridTop * scaleY,
            areaWidth * scaleX,
            gridHeight * scaleY);

        if (_blueBackgroundTexture == null || gridRect.width <= 0f || gridRect.height <= 0f)
            return;

        Color previousColor = GUI.color;
        float scale = Mathf.Min(scaleX, scaleY);
        float outerBorder = 2f * scale;
        float lineWidth = 1f * scale;

        // One shared outer frame and a flat, slightly darker interior.
        DrawSolidRect(gridRect, new Color(0.25f, 0.17f, 0.095f, 0.96f));
        Rect innerRect = new Rect(
            gridRect.x + outerBorder,
            gridRect.y + outerBorder,
            gridRect.width - 2f * outerBorder,
            gridRect.height - 2f * outerBorder);
        DrawSolidRect(innerRect, new Color(0.018f, 0.045f, 0.105f, 0.60f));

        // Simple uniform dividers; no shadows, raised edges or highlighted corners.
        Color dividerColor = new Color(0.27f, 0.20f, 0.13f, 0.92f);
        float cellWidth = innerRect.width / columns;
        float cellHeight = innerRect.height / rows;

        for (int column = 1; column < columns; column++)
        {
            float x = innerRect.x + cellWidth * column;
            DrawSolidRect(
                new Rect(x - lineWidth * 0.5f, innerRect.y, lineWidth, innerRect.height),
                dividerColor);
        }

        for (int row = 1; row < rows; row++)
        {
            float y = innerRect.y + cellHeight * row;
            DrawSolidRect(
                new Rect(innerRect.x, y - lineWidth * 0.5f, innerRect.width, lineWidth),
                dividerColor);
        }

        GUI.color = previousColor;
    }

    private void DrawSolidRect(Rect rect, Color color)
    {
        if (_blueBackgroundTexture == null || rect.width <= 0f || rect.height <= 0f) return;

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _blueBackgroundTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
            IsOpen = false;
        }

        // Both textures are loaded from Resources; Unity owns their asset lifetimes.
    }
}
