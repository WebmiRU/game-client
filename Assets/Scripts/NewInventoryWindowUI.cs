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
    private Color32[] _frameAlphaPixels;
    private Texture2D _blueBackgroundTexture;
    private Vector2 _windowPosition;
    private Vector2 _dragOffset;
    private bool _hasWindowPosition;
    private bool _isDragging;

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
        else
        {
            // Cache the source alpha once so transparent padding never acts as a hit target.
            _frameAlphaPixels = _frameTexture.GetPixels32();
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
        if (keyboard != null)
        {
            if (keyboard.iKey.wasPressedThisFrame)
            {
                SetOpen(!IsOpen);
            }
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetOpen(false);
            }
        }

        if (!IsOpen)
        {
            _isDragging = false;
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            _isDragging = false;
            return;
        }

        Vector2 pointer = ToGuiPosition(mouse.position.ReadValue());
        Rect frameRect = GetFrameRect();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (CanBeginWindowDrag(frameRect, pointer))
            {
                _isDragging = true;
                _dragOffset = pointer - frameRect.position;
            }
            else
            {
                _isDragging = false;
            }
        }

        if (_isDragging && mouse.leftButton.isPressed)
        {
            _windowPosition = ClampWindowPosition(
                pointer - _dragOffset,
                frameRect.width,
                frameRect.height);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            _isDragging = false;
        }
    }

    /// <summary>
    /// Returns true only while the pointer is interacting with the inventory window.
    /// The open inventory does not disable click-to-move elsewhere in the world.
    /// </summary>
    public static bool BlocksWorldInputAt(Vector2 pointerBottomLeft)
    {
        if (!IsOpen || _instance == null)
        {
            return false;
        }

        return _instance._isDragging || _instance.IsPointerInsideWindow(pointerBottomLeft);
    }

    private bool IsPointerInsideWindow(Vector2 pointerBottomLeft)
    {
        Vector2 pointer = ToGuiPosition(pointerBottomLeft);
        Rect frameRect = GetFrameRect();

        // Clicking cells should block world clicks, but it must not start dragging.
        return GetBackgroundRect(frameRect).Contains(pointer) ||
            IsPointerOnVisibleFrame(frameRect, pointer);
    }

    private bool CanBeginWindowDrag(Rect frameRect, Vector2 pointer)
    {
        // Only opaque pixels of the decorative frame are draggable; its transparent
        // texture padding is not part of the physical window.
        if (IsPointerOnVisibleFrame(frameRect, pointer))
        {
            return true;
        }

        // The blue backing is draggable except for the inventory cell grid itself.
        Rect backgroundRect = GetBackgroundRect(frameRect);
        return backgroundRect.Contains(pointer) &&
            !GetInventoryGridRect(frameRect).Contains(pointer);
    }

    private bool IsPointerOnVisibleFrame(Rect frameRect, Vector2 pointer)
    {
        if (_frameTexture == null || _frameAlphaPixels == null ||
            _frameTexture.width <= 0 || _frameTexture.height <= 0 ||
            !frameRect.Contains(pointer))
        {
            return false;
        }

        float u = (pointer.x - frameRect.x) / frameRect.width;
        float vFromTop = (pointer.y - frameRect.y) / frameRect.height;
        int pixelX = Mathf.Clamp(Mathf.FloorToInt(u * _frameTexture.width), 0, _frameTexture.width - 1);
        int pixelY = Mathf.Clamp(
            Mathf.FloorToInt((1f - vFromTop) * _frameTexture.height),
            0,
            _frameTexture.height - 1);

        return _frameAlphaPixels[pixelY * _frameTexture.width + pixelX].a > 12;
    }

    private Rect GetBackgroundRect(Rect frameRect)
    {
        // Match the exact blue rectangle drawn beneath the frame.
        return new Rect(
            frameRect.x + frameRect.width * (31f / 640f),
            frameRect.y + frameRect.height * (44f / 360f),
            frameRect.width * (578f / 640f),
            frameRect.height * (280f / 360f));
    }

    private static Vector2 ToGuiPosition(Vector2 pointerBottomLeft)
    {
        return new Vector2(pointerBottomLeft.x, Screen.height - pointerBottomLeft.y);
    }

    private Rect GetFrameRect()
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

        if (!_hasWindowPosition)
        {
            _windowPosition = new Vector2(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f);
            _hasWindowPosition = true;
        }

        _windowPosition = ClampWindowPosition(_windowPosition, width, height);
        return new Rect(_windowPosition.x, _windowPosition.y, width, height);
    }

    private static Vector2 ClampWindowPosition(Vector2 position, float width, float height)
    {
        float maxX = Mathf.Max(0f, Screen.width - width);
        float maxY = Mathf.Max(0f, Screen.height - height);
        return new Vector2(
            Mathf.Clamp(position.x, 0f, maxX),
            Mathf.Clamp(position.y, 0f, maxY));
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
        if (!open)
        {
            _isDragging = false;
        }
    }

    private void DrawWindow()
    {
        Rect frameRect = GetFrameRect();
        Color previousColor = GUI.color;

        // The frame is 640x360. This matches the exact backing used by hit testing.
        Rect backgroundRect = GetBackgroundRect(frameRect);

        if (_blueBackgroundTexture != null)
        {
            GUI.color = new Color(0.012f, 0.045f, 0.15f, 0.80f);
            GUI.DrawTexture(backgroundRect, _blueBackgroundTexture, ScaleMode.StretchToFill, true);

            // The grid uses the same blue background as the rest of the window.
            DrawInventoryGrid(frameRect);
        }

        if (_frameTexture != null)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(frameRect, _frameTexture, ScaleMode.StretchToFill, true);
        }

        GUI.color = previousColor;
    }

    private Rect GetInventoryGridRect(Rect frameRect)
    {
        // Keep the requested 7 columns x 9 rows, but size each cell as a square.
        const float innerLeft = 31f;
        const float innerWidth = 578f;
        const float gridTop = 66f;
        const float availableGridHeight = 232f;
        const int columns = 9;
        const int rows = 7;

        float scaleX = frameRect.width / 640f;
        float scaleY = frameRect.height / 360f;
        float areaX = innerLeft + innerWidth / 3f;
        float areaWidth = innerWidth * 2f / 3f;

        // Use the largest square that fits inside the intended grid area.
        float cellSize = Mathf.Min(areaWidth / columns, availableGridHeight / rows);
        float gridWidth = cellSize * columns;
        float gridHeight = cellSize * rows;
        float gridX = areaX + (areaWidth - gridWidth) * 0.5f;
        float gridY = gridTop + (availableGridHeight - gridHeight) * 0.5f;

        return new Rect(
            frameRect.x + gridX * scaleX,
            frameRect.y + gridY * scaleY,
            gridWidth * scaleX,
            gridHeight * scaleY);
    }

    private void DrawInventoryGrid(Rect frameRect)
    {
        // Cells share the plain blue backing; only a common border and slim dividers are drawn.
        const int columns = 9;
        const int rows = 7;
        Rect gridRect = GetInventoryGridRect(frameRect);

        if (_blueBackgroundTexture == null || gridRect.width <= 0f || gridRect.height <= 0f)
        {
            return;
        }

        Color previousColor = GUI.color;
        float scaleX = frameRect.width / 640f;
        float scaleY = frameRect.height / 360f;
        float lineWidth = Mathf.Max(1f, Mathf.Min(scaleX, scaleY));
        Color dividerColor = new Color(1.00f, 0.78f, 0.22f, 0.98f);

        // One common outer border. Individual cells have no separate fills, shadows or bevels.
        DrawSolidRect(new Rect(gridRect.x, gridRect.y, gridRect.width, lineWidth), dividerColor);
        DrawSolidRect(new Rect(gridRect.x, gridRect.yMax - lineWidth, gridRect.width, lineWidth), dividerColor);
        DrawSolidRect(new Rect(gridRect.x, gridRect.y, lineWidth, gridRect.height), dividerColor);
        DrawSolidRect(new Rect(gridRect.xMax - lineWidth, gridRect.y, lineWidth, gridRect.height), dividerColor);

        float cellWidth = gridRect.width / columns;
        float cellHeight = gridRect.height / rows;

        for (int column = 1; column < columns; column++)
        {
            float x = gridRect.x + cellWidth * column;
            DrawSolidRect(
                new Rect(x - lineWidth * 0.5f, gridRect.y, lineWidth, gridRect.height),
                dividerColor);
        }

        for (int row = 1; row < rows; row++)
        {
            float y = gridRect.y + cellHeight * row;
            DrawSolidRect(
                new Rect(gridRect.x, y - lineWidth * 0.5f, gridRect.width, lineWidth),
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
