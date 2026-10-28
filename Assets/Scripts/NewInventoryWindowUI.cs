using System.Collections.Generic;
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

    [System.Serializable]
    public struct InventoryItemData
    {
        public string itemTemplate;
        public string name;
        public int quantity;
    }

    private readonly List<InventoryItemData> _inventoryItems = new List<InventoryItemData>();
    private Texture2D _soulshotIconTexture;
    private Texture2D _genericItemIconTexture;
    private GUIStyle _inventoryQuantityStyle;
    private GUIStyle _inventoryTooltipStyle;

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

        _soulshotIconTexture = CreateSoulshotInventoryIcon();
        _genericItemIconTexture = CreateGenericInventoryIcon();
        Debug.Log("NewInventoryWindowUI: initialized image frame, inventory icons and translucent dark-blue background.", this);
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

    /// <summary>Receives the authoritative item stacks from the game server.</summary>
    public static void SetInventory(InventoryItemData[] items)
    {
        NewInventoryWindowUI window = GetOrCreate();
        window._inventoryItems.Clear();

        if (items == null)
        {
            return;
        }

        foreach (InventoryItemData item in items)
        {
            if (item.quantity <= 0)
            {
                continue;
            }

            window._inventoryItems.Add(new InventoryItemData
            {
                itemTemplate = item.itemTemplate ?? string.Empty,
                name = string.IsNullOrWhiteSpace(item.name)
                    ? (string.IsNullOrWhiteSpace(item.itemTemplate) ? "Предмет" : item.itemTemplate)
                    : item.name,
                quantity = item.quantity
            });
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

        DrawInventoryItems(gridRect, columns, rows);
        GUI.color = previousColor;
    }

    private void DrawInventoryItems(Rect gridRect, int columns, int rows)
    {
        if (_inventoryItems.Count == 0)
        {
            return;
        }

        EnsureInventoryItemStyles();
        float cellWidth = gridRect.width / columns;
        float cellHeight = gridRect.height / rows;
        int visibleCount = Mathf.Min(_inventoryItems.Count, columns * rows);
        Event currentEvent = Event.current;
        Color previousColor = GUI.color;
        GUI.color = Color.white;

        for (int index = 0; index < visibleCount; index++)
        {
            InventoryItemData item = _inventoryItems[index];
            int row = index / columns;
            int column = index % columns;
            Rect cell = new Rect(
                gridRect.x + column * cellWidth,
                gridRect.y + row * cellHeight,
                cellWidth,
                cellHeight);

            float padding = Mathf.Max(2f, Mathf.Min(cellWidth, cellHeight) * 0.08f);
            Rect iconRect = new Rect(
                cell.x + padding,
                cell.y + padding,
                cell.width - padding * 2f,
                cell.height - padding * 2f);

            bool isSoulshot = item.itemTemplate.IndexOf("soulshot", System.StringComparison.OrdinalIgnoreCase) >= 0;
            Texture2D icon = isSoulshot ? _soulshotIconTexture : _genericItemIconTexture;
            if (icon != null)
            {
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            }

            if (item.quantity > 1)
            {
                float badgeWidth = Mathf.Min(22f, cell.width * 0.62f);
                float badgeHeight = Mathf.Min(13f, cell.height * 0.42f);
                Rect quantityRect = new Rect(
                    cell.xMax - badgeWidth - 1f,
                    cell.yMax - badgeHeight - 1f,
                    badgeWidth,
                    badgeHeight);
                DrawSolidRect(quantityRect, new Color(0.015f, 0.02f, 0.055f, 0.92f));
                GUI.Label(new Rect(quantityRect.x - 1f, quantityRect.y - 1f,
                    quantityRect.width + 2f, quantityRect.height + 2f),
                    item.quantity.ToString(), _inventoryQuantityStyle);
            }

            if (cell.Contains(currentEvent.mousePosition))
            {
                GUIContent tooltipContent = new GUIContent(item.name);
                Vector2 tooltipSize = _inventoryTooltipStyle.CalcSize(tooltipContent);
                float tooltipWidth = Mathf.Min(
                    Mathf.Max(tooltipSize.x + 14f, 100f),
                    Screen.width * 0.5f);
                Rect tooltipRect = new Rect(
                    Mathf.Clamp(currentEvent.mousePosition.x + 14f, 4f, Screen.width - tooltipWidth - 4f),
                    Mathf.Clamp(currentEvent.mousePosition.y + 16f, 4f, Screen.height - 28f),
                    tooltipWidth,
                    24f);
                GUI.Box(tooltipRect, GUIContent.none);
                GUI.Label(new Rect(tooltipRect.x + 6f, tooltipRect.y + 1f,
                    tooltipRect.width - 12f, tooltipRect.height - 2f),
                    item.name, _inventoryTooltipStyle);
            }
        }

        GUI.color = previousColor;
    }

    private void EnsureInventoryItemStyles()
    {
        if (_inventoryQuantityStyle != null && _inventoryTooltipStyle != null)
        {
            return;
        }

        _inventoryQuantityStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerRight,
            fontStyle = FontStyle.Bold,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * 15f), 8, 13),
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(1, 2, 0, 0)
        };
        _inventoryQuantityStyle.normal.textColor = Color.white;

        _inventoryTooltipStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * 14f), 9, 14),
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(2, 2, 0, 0)
        };
        _inventoryTooltipStyle.normal.textColor = new Color(1f, 0.84f, 0.24f, 1f);
    }

    private static Texture2D CreateSoulshotInventoryIcon()
    {
        const int size = 64;
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float py = ((y + 0.5f) / size) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float px = ((x + 0.5f) / size) * 2f - 1f;
                float dx = Mathf.Max(0f, Mathf.Abs(px) - 0.40f);
                float distance = Mathf.Sqrt(dx * dx + py * py);
                int index = y * size + x;

                if (distance >= 0.315f)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float edge = Mathf.Clamp01((0.315f - distance) / 0.022f);
                float broadReflection = Mathf.Exp(-0.5f * Mathf.Pow((py + 0.10f) / 0.36f, 2f));
                float narrowReflection = Mathf.Exp(-0.5f * Mathf.Pow((py + 0.17f) / 0.085f, 2f));
                float darkReflection = Mathf.Exp(-0.5f * Mathf.Pow(py / 0.095f, 2f));

                Color color = new Color(0.004f, 0.070f, 0.24f, edge);
                color = Color.Lerp(color, new Color(0.008f, 0.26f, 0.62f, edge), broadReflection * 0.8f);
                color = Color.Lerp(color, new Color(0.02f, 0.48f, 0.85f, edge), narrowReflection * 0.55f);
                color = Color.Lerp(color, new Color(0.001f, 0.025f, 0.105f, edge), darkReflection * 0.52f);
                color = Color.Lerp(color, new Color(0.28f, 0.62f, 0.96f, edge), narrowReflection * 0.22f);

                // Thin aluminium seams stay silver; the enamel itself is smooth and glossy,
                // with no coarse diamond braid that would read as scratches at inventory size.
                bool collar = Mathf.Abs(Mathf.Abs(px) - 0.395f) < 0.025f && Mathf.Abs(py) < 0.25f;
                if (collar)
                {
                    float silver = 0.64f + 0.11f * Mathf.Clamp01(narrowReflection);
                    color = new Color(silver * 0.92f, silver * 0.96f, silver, edge);
                }

                pixels[index] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255));
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "InventorySoulshotGlossyPepsiBlueIcon",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateGenericInventoryIcon()
    {
        const int size = 32;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float py = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                bool diamond = px + py < 0.78f;
                bool rim = px + py < 0.9f && !diamond;
                Color32 color = diamond
                    ? new Color32(174, 128, 48, 255)
                    : rim ? new Color32(78, 58, 31, 220) : new Color32(0, 0, 0, 0);
                pixels[y * size + x] = color;
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "InventoryGenericItemIcon",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
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
        if (_soulshotIconTexture != null) Destroy(_soulshotIconTexture);
        if (_genericItemIconTexture != null) Destroy(_genericItemIconTexture);
    }
}
