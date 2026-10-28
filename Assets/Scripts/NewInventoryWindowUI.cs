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
        // These coordinates are relative to the 640x360 frame texture.
        // Keep the grid clear of the top/bottom ornament and use the right 2/3
        // of the inner opening as its layout area.
        const float innerLeft = 31f;
        const float innerWidth = 578f;
        const float gridTop = 66f;
        const float gridHeight = 232f;
        const int columns = 7;
        const int rows = 8;
        const float gap = 4f;

        float scaleX = frameRect.width / 640f;
        float scaleY = frameRect.height / 360f;
        float areaX = innerLeft + innerWidth / 3f;
        float areaWidth = innerWidth * 2f / 3f;

        float scaledGapX = gap * scaleX;
        float scaledGapY = gap * scaleY;
        float scaledAreaWidth = areaWidth * scaleX;
        float scaledAreaHeight = gridHeight * scaleY;
        float cellSize = Mathf.Min(
            (scaledAreaWidth - scaledGapX * (columns - 1)) / columns,
            (scaledAreaHeight - scaledGapY * (rows - 1)) / rows);

        if (cellSize <= 0f || _blueBackgroundTexture == null) return;

        float gridWidth = columns * cellSize + (columns - 1) * scaledGapX;
        float gridPixelHeight = rows * cellSize + (rows - 1) * scaledGapY;
        float gridX = frameRect.x + areaX * scaleX + (scaledAreaWidth - gridWidth) * 0.5f;
        float gridY = frameRect.y + gridTop * scaleY + (scaledAreaHeight - gridPixelHeight) * 0.5f;

        Color previousColor = GUI.color;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Rect cell = new Rect(
                    gridX + column * (cellSize + scaledGapX),
                    gridY + row * (cellSize + scaledGapY),
                    cellSize,
                    cellSize);

                // Soft shadow beneath each slot.
                DrawSolidRect(
                    new Rect(cell.x + 1f * scaleX, cell.y + 1.5f * scaleY, cell.width, cell.height),
                    new Color(0.005f, 0.008f, 0.02f, 0.72f));

                // Bronze outer edge and dark inset.
                DrawSolidRect(cell, new Color(0.30f, 0.21f, 0.12f, 0.96f));
                Rect inset = new Rect(
                    cell.x + 1.2f * scaleX,
                    cell.y + 1.2f * scaleY,
                    cell.width - 2.4f * scaleX,
                    cell.height - 2.4f * scaleY);
                DrawSolidRect(inset, new Color(0.025f, 0.055f, 0.12f, 0.94f));

                // A restrained bevel makes the cells read as individual slots.
                DrawSolidRect(
                    new Rect(inset.x, inset.y, inset.width, 1f * scaleY),
                    new Color(0.58f, 0.43f, 0.25f, 0.74f));
                DrawSolidRect(
                    new Rect(inset.x, inset.y, 1f * scaleX, inset.height),
                    new Color(0.42f, 0.34f, 0.25f, 0.58f));
                DrawSolidRect(
                    new Rect(inset.x, inset.yMax - 1f * scaleY, inset.width, 1f * scaleY),
                    new Color(0.003f, 0.008f, 0.022f, 0.82f));
                DrawSolidRect(
                    new Rect(inset.xMax - 1f * scaleX, inset.y, 1f * scaleX, inset.height),
                    new Color(0.003f, 0.008f, 0.022f, 0.82f));
            }
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
