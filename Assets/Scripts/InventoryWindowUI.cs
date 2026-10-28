using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// MMORPG-style inventory window inspired by the supplied Lineage II references.
/// Equipment and character preview are presented first; item grids stay empty for now.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryWindowUI : MonoBehaviour
{
    private const int PreviewLayer = 30;
    private const int Columns = 8;
    private const int Rows = 7;

    public static bool IsOpen { get; private set; }
    private static InventoryWindowUI _instance;
    private GameObject window;
    private Camera previewCamera;
    private RenderTexture previewTexture;
    private RawImage characterImage;
    private Transform previewModelRoot;
    private Font uiFont;
    private Text characterNameText;
    private Text capacityText;
    private Text weightText;
    private Text itemsTabText;
    private Text questTabText;
    private Image itemsTabBackground;
    private Image questTabBackground;
    private readonly List<Mesh> previewMeshes = new List<Mesh>();
    private readonly List<Material> previewMaterials = new List<Material>();
    private string playerName = "player1";

    // The inventory is drawn through IMGUI for a reliable overlay, matching the
    // always-visible inventory toggle. The older Canvas layout remains only as a
    // source for the character preview texture.
    private Texture2D _guiWhiteTexture;
    private Texture2D _guiTransparentTexture;
    private GUIStyle _guiTitleStyle;
    private GUIStyle _guiSectionStyle;
    private GUIStyle _guiLabelStyle;
    private GUIStyle _guiSmallLabelStyle;
    private GUIStyle _guiSlotLabelStyle;
    private GUIStyle _guiButtonStyle;
    private GUIStyle _guiTabStyle;
    private bool _showQuestItems;
    private bool _windowPositionInitialized;
    private Vector2 _windowPosition;
    private bool _draggingWindow;
    private Vector2 _dragMouseStart;
    private Vector2 _dragWindowStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<InventoryWindowUI>() != null) return;
        GameObject ui = new GameObject("InventoryWindowUI");
        DontDestroyOnLoad(ui);
        ui.AddComponent<InventoryWindowUI>();
    }

    private void Awake()
    {
        _instance = this;
        uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        EnsureEventSystem();
        BuildCanvasAndWindow();
        window.SetActive(false);
        IsOpen = false;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.iKey.wasPressedThisFrame) Toggle();
        else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        IsOpen = false;
        if (previewCamera != null)
        {
            previewCamera.targetTexture = null;
            Destroy(previewCamera.gameObject);
        }
        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }
        DestroyPreviewResources();
        if (_guiWhiteTexture != null) Destroy(_guiWhiteTexture);
        if (_guiTransparentTexture != null) Destroy(_guiTransparentTexture);
    }

    /// <summary>Entry point used by the always-visible server HUD.</summary>
    public static void ToggleFromHUD()
    {
        InventoryWindowUI inventory = _instance;
        if (inventory == null)
        {
            inventory = FindFirstObjectByType<InventoryWindowUI>();
            if (inventory != null) _instance = inventory;
        }

        if (inventory == null)
        {
            GameObject ui = new GameObject("InventoryWindowUI");
            DontDestroyOnLoad(ui);
            inventory = ui.AddComponent<InventoryWindowUI>();
        }

        inventory.SetOpen(!IsOpen);
        Debug.Log("InventoryWindowUI: HUD toggle -> " + (IsOpen ? "open" : "closed"));
    }

    /// <summary>
    /// Draw from GameServerLoginUI's already-visible IMGUI pass so the toggle and
    /// inventory window share one guaranteed rendering/input path.
    /// </summary>
    public static void DrawOverlayFromHUD()
    {
        InventoryWindowUI inventory = _instance;
        if (inventory == null)
        {
            inventory = FindFirstObjectByType<InventoryWindowUI>();
            if (inventory != null) _instance = inventory;
        }

        if (inventory != null && IsOpen)
        {
            inventory.DrawWindowGUI();
        }
    }

    public void Toggle() => SetOpen(!IsOpen);

    public void SetOpen(bool open)
    {
        IsOpen = open;
        _draggingWindow = false;
        if (window != null) window.SetActive(open);
        if (previewCamera != null) previewCamera.enabled = open;
        if (open)
        {
            try
            {
                RefreshCharacterPreview();
            }
            catch (System.Exception exception)
            {
                // A preview-model issue must never prevent the inventory frame from opening.
                Debug.LogException(exception, this);
            }
        }
    }

    private void DrawWindowGUI()
    {
        if (!IsOpen) return;

        EnsureInventoryGuiStyles();

        float scale = Mathf.Clamp(
            Mathf.Min(Screen.width / 1920f, Screen.height / 1080f),
            0.55f, 1.35f);
        float width = Mathf.Min(1360f * scale, Screen.width - 24f);
        float height = Mathf.Min(760f * scale, Screen.height - 24f);
        if (!_windowPositionInitialized)
        {
            _windowPosition = new Vector2(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f);
            _windowPositionInitialized = true;
        }
        ClampWindowPosition(width, height);
        float x = _windowPosition.x;
        float y = _windowPosition.y;

        _guiTitleStyle.fontSize = Mathf.RoundToInt(23f * scale);
        _guiSectionStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(13f * scale));
        _guiLabelStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(14f * scale));
        _guiSmallLabelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(11f * scale));
        _guiSlotLabelStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(9f * scale));
        _guiButtonStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(15f * scale));
        _guiTabStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(14f * scale));

        Color oldGuiColor = GUI.color;

        Rect frame = new Rect(x, y, width, height);
        DrawOrnateFrame(frame, scale);
        Rect surface = InsetGuiRect(frame, 10f * scale);

        Rect header = new Rect(surface.x + 2f * scale, surface.y + 2f * scale,
            surface.width - 4f * scale, 50f * scale);
        DrawGuiRect(header, new Color(0.105f, 0.085f, 0.058f, 1f));
        DrawGuiRect(new Rect(header.x + 30f * scale, header.y + 1f * scale,
            header.width - 60f * scale, 1f * scale), new Color(0.58f, 0.44f, 0.25f, 1f));
        DrawGuiRect(new Rect(header.x + 18f * scale, header.yMax - 2f * scale,
            header.width - 36f * scale, 1f * scale), new Color(0.25f, 0.19f, 0.12f, 1f));

        Rect titlePlate = new Rect(header.center.x - 166f * scale, header.y + 5f * scale,
            332f * scale, 39f * scale);
        DrawGuiRect(titlePlate, new Color(0.045f, 0.037f, 0.026f, 1f));
        DrawGuiRect(InsetGuiRect(titlePlate, 1f * scale), new Color(0.42f, 0.31f, 0.18f, 1f));
        DrawGuiRect(InsetGuiRect(titlePlate, 2.5f * scale), new Color(0.09f, 0.07f, 0.045f, 1f));
        DrawGuiDiamond(new Vector2(titlePlate.x + 8f * scale, titlePlate.center.y), 9f * scale,
            new Color(0.72f, 0.55f, 0.29f, 1f));
        DrawGuiDiamond(new Vector2(titlePlate.xMax - 8f * scale, titlePlate.center.y), 9f * scale,
            new Color(0.72f, 0.55f, 0.29f, 1f));

        GUI.Label(new Rect(titlePlate.x + 12f * scale, titlePlate.y + 1f * scale,
            titlePlate.width - 24f * scale, titlePlate.height - 2f * scale),
            "Инвентарь", _guiTitleStyle);

        Rect dragRect = new Rect(header.x + 8f * scale, header.y + 1f * scale,
            header.width - 82f * scale, header.height - 2f * scale);

        Rect closeRect = new Rect(frame.xMax - 50f * scale, frame.y + 14f * scale,
            32f * scale, 30f * scale);
        if (DrawInventoryButton(closeRect, "×", new Color(0.13f, 0.115f, 0.095f, 1f)))
        {
            SetOpen(false);
            GUI.color = oldGuiColor;
            return;
        }

        float panelY = y + 70f * scale;
        float panelHeight = height - 102f * scale;
        float leftX = x + 18f * scale;
        float leftWidth = 440f * scale;
        float rightX = x + 468f * scale;
        float rightWidth = width - 486f * scale - 18f * scale;

        Rect leftPanel = new Rect(leftX, panelY, leftWidth, panelHeight);
        Rect rightPanel = new Rect(rightX, panelY, rightWidth, panelHeight);
        DrawOrnatePanel(leftPanel, new Color(0.083f, 0.075f, 0.061f, 1f), scale);
        DrawOrnatePanel(rightPanel, new Color(0.074f, 0.068f, 0.055f, 1f), scale);

        GUI.Label(new Rect(leftPanel.x + 40f * scale, leftPanel.y + 9f * scale,
            leftPanel.width - 80f * scale, 24f * scale), "ПЕРСОНАЖ", _guiSectionStyle);

        Rect viewport = new Rect(leftPanel.x + 112f * scale, leftPanel.y + 65f * scale,
            216f * scale, 424f * scale);
        DrawOrnatePanel(viewport, new Color(0.035f, 0.032f, 0.027f, 1f), scale);
        Rect viewportInner = InsetGuiRect(viewport, 5f * scale);
        DrawGuiRect(viewportInner, new Color(0.025f, 0.024f, 0.021f, 1f));
        if (previewTexture != null)
        {
            GUI.DrawTexture(InsetGuiRect(viewportInner, 2f * scale), previewTexture,
                ScaleMode.ScaleToFit, true);
        }

        string[] leftSlots = { "Голова", "Серьга", "Ожерелье", "Броня", "Перчатки", "Штаны", "Обувь" };
        string[] rightSlots = { "Украш.", "Щит", "Кольцо", "Кольцо", "Пояс", "Оружие", "Костюм" };
        for (int i = 0; i < leftSlots.Length; i++)
        {
            float slotY = leftPanel.y + (52f + i * 69f) * scale;
            DrawEquipmentSlot(new Rect(leftPanel.x + 9f * scale, slotY,
                54f * scale, 54f * scale), leftSlots[i], scale);
            DrawEquipmentSlot(new Rect(leftPanel.xMax - 63f * scale, slotY,
                54f * scale, 54f * scale), rightSlots[i], scale);
        }

        GUI.Label(new Rect(leftPanel.x + 80f * scale, leftPanel.y + 505f * scale,
            leftPanel.width - 160f * scale, 25f * scale), playerName, _guiLabelStyle);
        string[] lowerSlots = { "Пояс", "Рубашка", "Талисман" };
        for (int i = 0; i < lowerSlots.Length; i++)
        {
            DrawEquipmentSlot(new Rect(leftPanel.x + (137f + i * 58f) * scale,
                leftPanel.y + 551f * scale, 44f * scale, 45f * scale), lowerSlots[i], scale);
        }

        Rect itemsTab = new Rect(rightPanel.x + 18f * scale, rightPanel.y + 12f * scale,
            140f * scale, 36f * scale);
        Rect questTab = new Rect(rightPanel.x + 166f * scale, rightPanel.y + 12f * scale,
            132f * scale, 36f * scale);
        if (DrawInventoryTab(itemsTab, "ВЕЩИ", !_showQuestItems, scale)) _showQuestItems = false;
        if (DrawInventoryTab(questTab, "КВЕСТ", _showQuestItems, scale)) _showQuestItems = true;
        GUI.Label(new Rect(rightPanel.xMax - 160f * scale, rightPanel.y + 14f * scale,
            140f * scale, 32f * scale), "0 / 80", _guiLabelStyle);

        float gridWidth = Mathf.Min(632f * scale, rightPanel.width - 30f * scale);
        float gridHeight = Mathf.Min(469f * scale, panelHeight - 150f * scale);
        Rect gridBack = new Rect(rightPanel.center.x - gridWidth * 0.5f,
            rightPanel.y + 58f * scale, gridWidth, gridHeight);
        DrawGuiRect(gridBack, new Color(0.025f, 0.024f, 0.021f, 1f));
        DrawGuiRect(InsetGuiRect(gridBack, 3f * scale), new Color(0.13f, 0.118f, 0.098f, 1f));

        float gap = 6f * scale;
        float cellWidth = Mathf.Min(68f * scale,
            (gridBack.width - 20f * scale - (Columns - 1) * gap) / Columns);
        float cellHeight = Mathf.Min(58f * scale,
            (gridBack.height - 20f * scale - (Rows - 1) * gap) / Rows);
        float cellsWidth = Columns * cellWidth + (Columns - 1) * gap;
        float cellsHeight = Rows * cellHeight + (Rows - 1) * gap;
        float cellsX = gridBack.center.x - cellsWidth * 0.5f;
        float cellsY = gridBack.y + (gridBack.height - cellsHeight) * 0.5f;
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                Rect cell = new Rect(cellsX + column * (cellWidth + gap),
                    cellsY + row * (cellHeight + gap), cellWidth, cellHeight);
                DrawInventorySlot(cell, scale);
            }
        }

        float railX = gridBack.xMax - 15f * scale;
        DrawGuiRect(new Rect(railX, gridBack.y + 8f * scale, 7f * scale,
            gridBack.height - 16f * scale), new Color(0.18f, 0.16f, 0.13f, 1f));
        DrawGuiRect(new Rect(railX - 4f * scale, gridBack.y + 7f * scale,
            15f * scale, 18f * scale), new Color(0.28f, 0.25f, 0.20f, 1f));
        DrawGuiRect(new Rect(railX - 4f * scale, gridBack.yMax - 25f * scale,
            15f * scale, 18f * scale), new Color(0.28f, 0.25f, 0.20f, 1f));

        float footerY = rightPanel.yMax - 76f * scale;
        DrawGuiRect(new Rect(rightPanel.x + 16f * scale, footerY,
            rightPanel.width - 32f * scale, 1f * scale), new Color(0.31f, 0.26f, 0.19f, 1f));
        GUI.Label(new Rect(rightPanel.x + 20f * scale, footerY + 10f * scale,
            190f * scale, 26f * scale), "◉   0", _guiLabelStyle);
        GUI.Label(new Rect(rightPanel.x + 215f * scale, footerY + 10f * scale,
            150f * scale, 26f * scale), "Вес: 0.0%", _guiSmallLabelStyle);
        Rect weightBar = new Rect(rightPanel.x + 350f * scale, footerY + 17f * scale,
            250f * scale, 10f * scale);
        DrawGuiRect(weightBar, new Color(0.025f, 0.024f, 0.02f, 1f));
        DrawGuiRect(new Rect(weightBar.x + 2f * scale, weightBar.y + 2f * scale,
            2f * scale, 6f * scale), new Color(0.62f, 0.47f, 0.16f, 1f));

        Rect trashRect = new Rect(rightPanel.xMax - 70f * scale, footerY + 4f * scale,
            48f * scale, 42f * scale);
        DrawInventoryButton(trashRect, "▣", new Color(0.13f, 0.12f, 0.1f, 1f));

        GUI.color = oldGuiColor;

        // Handle mouse events only after every part of the window has been drawn.
        // Event.Use() during MouseDrag prevented the grid from being repainted and
        // made it flicker while the window was moving.
        HandleWindowDragging(dragRect, width, height);
    }

    private void EnsureInventoryGuiStyles()
    {
        if (_guiWhiteTexture != null && _guiTransparentTexture != null && _guiButtonStyle != null)
            return;

        _guiWhiteTexture = CreateGuiTexture(Color.white, "InventoryGuiWhite");
        _guiTransparentTexture = CreateGuiTexture(Color.clear, "InventoryGuiTransparent");

        _guiTitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        _guiTitleStyle.normal.textColor = new Color(0.91f, 0.84f, 0.69f, 1f);

        _guiSectionStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        _guiSectionStyle.normal.textColor = new Color(0.74f, 0.61f, 0.40f, 1f);

        _guiLabelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Normal,
            wordWrap = false
        };
        _guiLabelStyle.normal.textColor = new Color(0.86f, 0.80f, 0.67f, 1f);

        _guiSmallLabelStyle = new GUIStyle(_guiLabelStyle)
        {
            alignment = TextAnchor.MiddleLeft
        };
        _guiSmallLabelStyle.normal.textColor = new Color(0.72f, 0.65f, 0.52f, 1f);

        _guiSlotLabelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerCenter,
            wordWrap = true,
            clipping = TextClipping.Clip,
            fontStyle = FontStyle.Normal
        };
        _guiSlotLabelStyle.normal.textColor = new Color(0.72f, 0.64f, 0.49f, 1f);

        _guiButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(3, 3, 1, 1),
            border = new RectOffset(0, 0, 0, 0)
        };
        SetGuiButtonBackground(_guiButtonStyle, _guiTransparentTexture);
        _guiButtonStyle.normal.textColor = new Color(0.91f, 0.82f, 0.64f, 1f);
        _guiButtonStyle.hover.textColor = new Color(1f, 0.94f, 0.80f, 1f);
        _guiButtonStyle.active.textColor = Color.white;
        _guiButtonStyle.focused.textColor = Color.white;

        _guiTabStyle = new GUIStyle(_guiButtonStyle);
        _guiTabStyle.fontStyle = FontStyle.Bold;
    }

    private static Texture2D CreateGuiTexture(Color color, string textureName)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = textureName,
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        return texture;
    }

    private static void SetGuiButtonBackground(GUIStyle style, Texture2D texture)
    {
        style.normal.background = texture;
        style.hover.background = texture;
        style.active.background = texture;
        style.focused.background = texture;
        style.onNormal.background = texture;
        style.onHover.background = texture;
        style.onActive.background = texture;
        style.onFocused.background = texture;
    }

    private void DrawGuiRect(Rect rect, Color color)
    {
        if (_guiWhiteTexture == null) return;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, _guiWhiteTexture, ScaleMode.StretchToFill, false);
        GUI.color = previous;
    }

    private bool DrawInventoryButton(Rect rect, string label, Color normalColor)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);
        bool pressed = hovered && Event.current.button == 0 &&
            (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp);
        Color fill = normalColor;
        if (hovered) fill = Color.Lerp(normalColor, new Color(0.65f, 0.50f, 0.28f, 1f), 0.28f);
        if (pressed) fill = Color.Lerp(normalColor, Color.black, 0.30f);

        DrawGuiRect(rect, new Color(0.012f, 0.010f, 0.007f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 1f), new Color(0.62f, 0.46f, 0.26f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 2.5f), new Color(0.055f, 0.043f, 0.027f, 1f));
        Rect face = InsetGuiRect(rect, 3.5f);
        DrawGuiRect(face, fill);
        DrawGuiRect(new Rect(face.x + 2f, face.y + 1f, Mathf.Max(0f, face.width - 4f), 1f),
            new Color(0.88f, 0.69f, 0.38f, hovered ? 0.85f : 0.48f));
        DrawGuiRect(new Rect(face.x + 2f, face.yMax - 2f, Mathf.Max(0f, face.width - 4f), 1f),
            new Color(0.012f, 0.010f, 0.007f, 0.9f));
        return GUI.Button(rect, label, _guiButtonStyle);
    }

    private bool DrawInventoryTab(Rect rect, string label, bool selected, float scale)
    {
        Color color = selected
            ? new Color(0.23f, 0.18f, 0.11f, 1f)
            : new Color(0.065f, 0.055f, 0.04f, 1f);
        bool clicked = DrawInventoryButton(rect, label, color);
        if (selected)
        {
            DrawGuiRect(new Rect(rect.x + 7f * scale, rect.yMax - 2f * scale,
                rect.width - 14f * scale, 2f * scale), new Color(0.87f, 0.66f, 0.34f, 1f));
        }
        else
        {
            DrawGuiRect(new Rect(rect.x + 7f * scale, rect.yMax - 2f * scale,
                rect.width - 14f * scale, 1f * scale), new Color(0.24f, 0.18f, 0.11f, 1f));
        }
        return clicked;
    }

    private void DrawEquipmentSlot(Rect rect, string label, float scale)
    {
        DrawGuiRect(rect, new Color(0.018f, 0.015f, 0.011f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 1f * scale), new Color(0.45f, 0.33f, 0.19f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 2.5f * scale), new Color(0.025f, 0.022f, 0.017f, 1f));
        Rect well = InsetGuiRect(rect, 3.5f * scale);
        DrawGuiRect(well, new Color(0.045f, 0.041f, 0.032f, 1f));
        DrawGuiRect(new Rect(well.x + 1f * scale, well.y + 1f * scale,
            Mathf.Max(1f, well.width - 2f * scale), 1f * scale),
            new Color(0.24f, 0.19f, 0.12f, 0.85f));
        float stud = 2.5f * scale;
        DrawGuiRect(new Rect(rect.x + 3f * scale, rect.y + 3f * scale, stud, stud),
            new Color(0.68f, 0.51f, 0.29f, 0.9f));
        DrawGuiRect(new Rect(rect.xMax - 3f * scale - stud, rect.y + 3f * scale, stud, stud),
            new Color(0.68f, 0.51f, 0.29f, 0.9f));
        GUI.Label(new Rect(rect.x + 1f * scale, rect.yMax - 20f * scale,
            rect.width - 2f * scale, 17f * scale), label, _guiSlotLabelStyle);
    }

    private void DrawInventorySlot(Rect rect, float scale)
    {
        DrawGuiRect(rect, new Color(0.014f, 0.012f, 0.009f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 1f * scale), new Color(0.34f, 0.27f, 0.17f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 2f * scale), new Color(0.02f, 0.018f, 0.014f, 1f));
        Rect well = InsetGuiRect(rect, 3f * scale);
        DrawGuiRect(well, new Color(0.055f, 0.051f, 0.043f, 1f));
        DrawGuiRect(new Rect(well.x + 1f * scale, well.y + 1f * scale,
            Mathf.Max(1f, well.width - 2f * scale), 1f * scale),
            new Color(0.19f, 0.16f, 0.12f, 0.85f));
        DrawGuiRect(new Rect(well.x + 1f * scale, well.y + 1f * scale,
            1f * scale, Mathf.Max(1f, well.height - 2f * scale)),
            new Color(0.14f, 0.12f, 0.09f, 0.65f));
        DrawGuiRect(new Rect(well.x + 2f * scale, well.yMax - 2f * scale,
            Mathf.Max(1f, well.width - 4f * scale), 1f * scale),
            new Color(0.009f, 0.008f, 0.006f, 1f));
    }

    private void DrawOrnatePanel(Rect rect, Color interiorColor, float scale)
    {
        DrawGuiRect(rect, new Color(0.012f, 0.010f, 0.007f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 1f * scale), new Color(0.38f, 0.29f, 0.17f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 2.5f * scale), new Color(0.035f, 0.028f, 0.019f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 4f * scale), new Color(0.20f, 0.15f, 0.09f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 5.5f * scale), interiorColor);
        Rect inner = InsetGuiRect(rect, 7f * scale);
        DrawGuiRect(new Rect(inner.x, inner.y, inner.width, 1f * scale),
            new Color(0.30f, 0.23f, 0.14f, 0.9f));
        DrawGuiRect(new Rect(inner.x, inner.yMax - 1f * scale, inner.width, 1f * scale),
            new Color(0.018f, 0.014f, 0.009f, 1f));
        DrawGuiRect(new Rect(inner.x, inner.y, 1f * scale, inner.height),
            new Color(0.17f, 0.13f, 0.08f, 0.8f));
        DrawGuiRect(new Rect(inner.xMax - 1f * scale, inner.y, 1f * scale, inner.height),
            new Color(0.018f, 0.014f, 0.009f, 1f));
    }

    private void DrawOrnateFrame(Rect rect, float scale)
    {
        DrawGuiRect(rect, new Color(0.008f, 0.007f, 0.005f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 1f * scale), new Color(0.68f, 0.50f, 0.27f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 2.5f * scale), new Color(0.075f, 0.055f, 0.033f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 4f * scale), new Color(0.40f, 0.30f, 0.17f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 5.5f * scale), new Color(0.022f, 0.018f, 0.012f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 7f * scale), new Color(0.20f, 0.15f, 0.09f, 1f));
        DrawGuiRect(InsetGuiRect(rect, 9f * scale), new Color(0.085f, 0.074f, 0.058f, 1f));

        Rect inset = InsetGuiRect(rect, 10f * scale);
        DrawGuiRect(new Rect(inset.x + 24f * scale, inset.y, inset.width - 48f * scale,
            1f * scale), new Color(0.73f, 0.58f, 0.35f, 0.72f));
        DrawGuiRect(new Rect(inset.x + 24f * scale, inset.yMax - 1f * scale,
            inset.width - 48f * scale, 1f * scale), new Color(0.015f, 0.012f, 0.008f, 1f));

        DrawOrnateCorner(new Vector2(rect.x + 12f * scale, rect.y + 12f * scale), scale);
        DrawOrnateCorner(new Vector2(rect.xMax - 12f * scale, rect.y + 12f * scale), scale);
        DrawOrnateCorner(new Vector2(rect.x + 12f * scale, rect.yMax - 12f * scale), scale);
        DrawOrnateCorner(new Vector2(rect.xMax - 12f * scale, rect.yMax - 12f * scale), scale);
    }

    private void DrawOrnateCorner(Vector2 center, float scale)
    {
        DrawGuiDiamond(center, 23f * scale, new Color(0.015f, 0.012f, 0.008f, 1f));
        DrawGuiDiamond(center, 17f * scale, new Color(0.54f, 0.39f, 0.20f, 1f));
        DrawGuiDiamond(center, 11f * scale, new Color(0.10f, 0.075f, 0.043f, 1f));
        DrawGuiDiamond(center, 5f * scale, new Color(0.82f, 0.64f, 0.35f, 1f));
        DrawGuiRect(new Rect(center.x + 8f * scale, center.y - 1f * scale,
            8f * scale, 2f * scale), new Color(0.57f, 0.42f, 0.23f, 0.8f));
        DrawGuiRect(new Rect(center.x - 1f * scale, center.y + 8f * scale,
            2f * scale, 8f * scale), new Color(0.57f, 0.42f, 0.23f, 0.8f));
    }

    private void DrawGuiDiamond(Vector2 center, float size, Color color)
    {
        Rect rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
        Matrix4x4 previous = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, center);
        DrawGuiRect(rect, color);
        GUI.matrix = previous;
    }

    private void HandleWindowDragging(Rect dragRect, float width, float height)
    {
        Event current = Event.current;
        if (current.type == EventType.MouseDown && current.button == 0 &&
            dragRect.Contains(current.mousePosition))
        {
            _draggingWindow = true;
            _dragMouseStart = current.mousePosition;
            _dragWindowStart = _windowPosition;
        }
        else if (_draggingWindow && current.type == EventType.MouseDrag && current.button == 0)
        {
            _windowPosition = _dragWindowStart + current.mousePosition - _dragMouseStart;
            ClampWindowPosition(width, height);
        }
        else if (_draggingWindow && current.type == EventType.MouseUp && current.button == 0)
        {
            _draggingWindow = false;
        }
    }

    private void ClampWindowPosition(float width, float height)
    {
        float maxX = Mathf.Max(12f, Screen.width - width - 12f);
        float maxY = Mathf.Max(12f, Screen.height - height - 12f);
        _windowPosition.x = Mathf.Clamp(_windowPosition.x, 12f, maxX);
        _windowPosition.y = Mathf.Clamp(_windowPosition.y, 12f, maxY);
    }

    private static Rect InsetGuiRect(Rect rect, float inset)
    {
        return new Rect(rect.x + inset, rect.y + inset,
            Mathf.Max(0f, rect.width - 2f * inset),
            Mathf.Max(0f, rect.height - 2f * inset));
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject events = new GameObject("InventoryEventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>();
            DontDestroyOnLoad(events);
        }
        else
        {
            InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            // Avoid competing input modules swallowing pointer events on the HUD button.
            BaseInputModule[] inputModules = eventSystem.GetComponents<BaseInputModule>();
            foreach (BaseInputModule module in inputModules)
            {
                if (module != inputModule) module.enabled = false;
            }
            inputModule.enabled = true;
        }
    }

    private void BuildCanvasAndWindow()
    {
        GameObject canvasObject = new GameObject("InventoryCanvas");
        // Keep this as a genuine root Canvas. The visible inventory itself is drawn
        // in OnGUI so it does not depend on Canvas sorting or scene hierarchy.
        DontDestroyOnLoad(canvasObject);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        window = CreateRect(canvas.transform, "InventoryWindow",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-680f, -380f), new Vector2(680f, 380f));
        window.AddComponent<Image>().color = new Color(0.47f, 0.37f, 0.25f, 0.99f);

        GameObject darkInset = CreateStretchImage(window.transform, "DarkOuterInset",
            Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f),
            new Color(0.055f, 0.049f, 0.041f, 1f));
        GameObject bronzeLine = CreateStretchImage(darkInset.transform, "BronzeInnerLine",
            Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f),
            new Color(0.29f, 0.235f, 0.17f, 1f));
        GameObject interior = CreateStretchImage(bronzeLine.transform, "Interior",
            Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f),
            new Color(0.105f, 0.096f, 0.081f, 0.99f));

        CreateFixedImage(interior.transform, "TopHighlight", new Vector2(1250f, 2f),
            new Vector2(0f, 347f), new Color(0.58f, 0.47f, 0.33f, 0.72f));
        CreateFixedImage(interior.transform, "BottomHighlight", new Vector2(1250f, 1f),
            new Vector2(0f, -347f), new Color(0.025f, 0.022f, 0.018f, 1f));
        CreateFixedImage(interior.transform, "CornerDiamondLeft", new Vector2(14f, 14f),
            new Vector2(-638f, 337f), new Color(0.72f, 0.6f, 0.42f, 1f), 45f);
        CreateFixedImage(interior.transform, "CornerDiamondRight", new Vector2(14f, 14f),
            new Vector2(638f, 337f), new Color(0.72f, 0.6f, 0.42f, 1f), 45f);
        CreateFixedImage(interior.transform, "TitleDiamondLeft", new Vector2(19f, 19f),
            new Vector2(-65f, 337f), new Color(0.72f, 0.6f, 0.42f, 1f), 45f);
        CreateFixedImage(interior.transform, "TitleDiamondRight", new Vector2(19f, 19f),
            new Vector2(65f, 337f), new Color(0.72f, 0.6f, 0.42f, 1f), 45f);
        CreateText(interior.transform, "Title", "Инвентарь", 23, FontStyle.Normal,
            new Color(0.88f, 0.83f, 0.73f, 1f), new Vector2(260f, 44f), new Vector2(0f, 328f));
        CreateButton(interior.transform, "CloseButton", new Vector2(38f, 32f),
            new Vector2(635f, 329f), new Color(0.13f, 0.115f, 0.095f, 1f), "×", 29,
            new Color(0.83f, 0.77f, 0.66f, 1f)).GetComponent<Button>()
            .onClick.AddListener(() => SetOpen(false));

        BuildPaperDoll(interior.transform);
        BuildItemGrid(interior.transform);

        // This Canvas supplied the RenderTexture target for the paper-doll preview,
        // but its visual layout is deliberately disabled: OnGUI draws the inventory
        // through the same reliable path as the working bottom-right toggle.
        canvas.enabled = false;
        GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = false;
    }


    private void BuildPaperDoll(Transform parent)
    {
        GameObject panel = CreateStretchImage(parent, "CharacterEquipmentPanel",
            new Vector2(0f, 0f), new Vector2(0.355f, 1f),
            new Vector2(18f, 32f), new Vector2(-5f, -70f),
            new Color(0.07f, 0.066f, 0.057f, 0.92f));
        CreateStretchImage(panel.transform, "CharacterInset", Vector2.zero, Vector2.one,
            new Vector2(3f, 3f), new Vector2(-3f, -3f), new Color(0.095f, 0.086f, 0.072f, 0.88f));
        CreateText(panel.transform, "CharacterHeading", "ПЕРСОНАЖ", 13, FontStyle.Bold,
            new Color(0.68f, 0.59f, 0.44f, 1f), new Vector2(170f, 24f), new Vector2(0f, 276f));

        Image viewport = CreateFixedImage(panel.transform, "CharacterViewportBackground",
            new Vector2(216f, 424f), new Vector2(0f, -10f), new Color(0.055f, 0.052f, 0.046f, 1f));
        characterImage = CreateRect(viewport.transform, "CharacterRender", Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f)).AddComponent<RawImage>();
        characterImage.raycastTarget = false;
        SetupCharacterCamera();

        string[] leftSlots = { "Голова", "Серьга", "Ожерелье", "Броня", "Перчатки", "Штаны", "Обувь" };
        string[] rightSlots = { "Украш.", "Щит", "Кольцо", "Кольцо", "Пояс", "Оружие", "Костюм" };
        float[] slotY = { 216f, 145f, 74f, 3f, -68f, -139f, -210f };
        for (int i = 0; i < leftSlots.Length; i++)
        {
            CreateEquipmentSlot(panel.transform, leftSlots[i], new Vector2(-177f, slotY[i]));
            CreateEquipmentSlot(panel.transform, rightSlots[i], new Vector2(177f, slotY[i]));
        }
        characterNameText = CreateText(panel.transform, "CharacterName", playerName, 13, FontStyle.Bold,
            new Color(0.84f, 0.77f, 0.63f, 1f), new Vector2(180f, 24f), new Vector2(0f, -252f));
        for (int i = 0; i < 3; i++)
            CreateEquipmentSlot(panel.transform, "Пояс", new Vector2(-52f + i * 52f, -283f), 43f, 14f, false);
    }

    private void BuildItemGrid(Transform parent)
    {
        GameObject panel = CreateStretchImage(parent, "ItemsPanel",
            new Vector2(0.355f, 0f), new Vector2(1f, 1f),
            new Vector2(5f, 32f), new Vector2(-18f, -70f),
            new Color(0.065f, 0.061f, 0.053f, 0.95f));
        CreateStretchImage(panel.transform, "ItemsInnerBorder", Vector2.zero, Vector2.one,
            new Vector2(3f, 3f), new Vector2(-3f, -3f), new Color(0.11f, 0.101f, 0.084f, 0.94f));

        itemsTabBackground = CreateFixedImage(panel.transform, "ItemsTab", new Vector2(156f, 39f),
            new Vector2(-300f, 263f), new Color(0.28f, 0.235f, 0.17f, 1f));
        itemsTabBackground.raycastTarget = true;
        itemsTabText = CreateText(panel.transform, "ItemsTabText", "Обычные", 17, FontStyle.Bold,
            new Color(0.91f, 0.85f, 0.72f, 1f), new Vector2(150f, 32f), new Vector2(-300f, 264f));
        Button itemsButton = itemsTabBackground.gameObject.AddComponent<Button>();
        itemsButton.targetGraphic = itemsTabBackground;
        itemsButton.onClick.AddListener(() => SetTab(false));

        questTabBackground = CreateFixedImage(panel.transform, "QuestTab", new Vector2(142f, 36f),
            new Vector2(-145f, 262f), new Color(0.13f, 0.12f, 0.1f, 1f));
        questTabBackground.raycastTarget = true;
        questTabText = CreateText(panel.transform, "QuestTabText", "Квест", 16, FontStyle.Normal,
            new Color(0.69f, 0.64f, 0.54f, 1f), new Vector2(140f, 30f), new Vector2(-145f, 263f));
        Button questButton = questTabBackground.gameObject.AddComponent<Button>();
        questButton.targetGraphic = questTabBackground;
        questButton.onClick.AddListener(() => SetTab(true));

        capacityText = CreateText(panel.transform, "Capacity", "0 / 80", 16, FontStyle.Normal,
            new Color(0.75f, 0.7f, 0.6f, 1f), new Vector2(120f, 26f), new Vector2(315f, 263f));

        Image gridBack = CreateFixedImage(panel.transform, "InventoryGridBackground",
            new Vector2(632f, 469f), new Vector2(0f, 3f), new Color(0.035f, 0.034f, 0.03f, 1f));
        CreateFixedImage(gridBack.transform, "GridInnerEdge", new Vector2(624f, 461f),
            Vector2.zero, new Color(0.17f, 0.155f, 0.128f, 1f));
        GameObject gridRoot = CreateRect(gridBack.transform, "EmptyItemGrid",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-304f, -222f), new Vector2(304f, 222f));
        GridLayoutGroup grid = gridRoot.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(68f, 58f);
        grid.spacing = new Vector2(6f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                GameObject slot = new GameObject("EmptySlot_" + row + "_" + column,
                    typeof(RectTransform), typeof(Image));
                slot.transform.SetParent(gridRoot.transform, false);
                slot.GetComponent<Image>().color = new Color(0.075f, 0.071f, 0.06f, 1f);
                CreateStretchImage(slot.transform, "SlotInset", Vector2.zero, Vector2.one,
                    new Vector2(1f, 1f), new Vector2(-1f, -1f), new Color(0.045f, 0.043f, 0.037f, 1f));
            }
        }

        CreateFixedImage(panel.transform, "ScrollRail", new Vector2(13f, 469f),
            new Vector2(330f, 3f), new Color(0.19f, 0.17f, 0.14f, 1f));
        CreateButton(panel.transform, "ScrollUp", new Vector2(22f, 22f),
            new Vector2(330f, 238f), new Color(0.22f, 0.2f, 0.16f, 1f), "▲", 10,
            new Color(0.77f, 0.71f, 0.59f, 1f));
        CreateButton(panel.transform, "ScrollDown", new Vector2(22f, 22f),
            new Vector2(330f, -232f), new Color(0.22f, 0.2f, 0.16f, 1f), "▼", 10,
            new Color(0.77f, 0.71f, 0.59f, 1f));

        CreateFixedImage(panel.transform, "FooterRule", new Vector2(796f, 1f),
            new Vector2(0f, -254f), new Color(0.28f, 0.24f, 0.18f, 1f));
        CreateText(panel.transform, "CurrencyMark", "◉", 21, FontStyle.Bold,
            new Color(0.81f, 0.68f, 0.26f, 1f), new Vector2(28f, 25f), new Vector2(180f, -277f));
        CreateText(panel.transform, "CurrencyAmount", "0", 18, FontStyle.Normal,
            new Color(0.9f, 0.87f, 0.79f, 1f), new Vector2(115f, 25f), new Vector2(255f, -277f));
        CreateFixedImage(panel.transform, "WeightBarBackground", new Vector2(216f, 10f),
            new Vector2(30f, -294f), new Color(0.045f, 0.04f, 0.033f, 1f));
        CreateFixedImage(panel.transform, "WeightBarFill", new Vector2(6f, 6f),
            new Vector2(-73f, -294f), new Color(0.64f, 0.48f, 0.15f, 1f));
        weightText = CreateText(panel.transform, "WeightLabel", "Вес: 0.0%", 12, FontStyle.Normal,
            new Color(0.75f, 0.69f, 0.56f, 1f), new Vector2(140f, 20f), new Vector2(30f, -277f));
        CreateButton(panel.transform, "TrashButton", new Vector2(54f, 46f),
            new Vector2(365f, -279f), new Color(0.13f, 0.12f, 0.1f, 1f), "▣", 25,
            new Color(0.67f, 0.62f, 0.53f, 1f));
        SetTab(false);
    }

    private void SetupCharacterCamera()
    {
        GameObject cameraObject = new GameObject("InventoryCharacterCamera");
        DontDestroyOnLoad(cameraObject);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.055f, 0.052f, 0.046f, 1f);
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = 1.28f;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 15f;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.allowHDR = true;

        previewTexture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32);
        previewTexture.name = "InventoryCharacterPreviewTexture";
        previewTexture.antiAliasing = 2;
        previewTexture.Create();
        previewCamera.targetTexture = previewTexture;
        characterImage.texture = previewTexture;
        previewCamera.transform.position = new Vector3(10000f, 10001.05f, 9996f);
        previewCamera.transform.LookAt(new Vector3(10000f, 10001f, 10000f));
    }

    private void RefreshCharacterPreview()
    {
        if (previewModelRoot != null)
        {
            Destroy(previewModelRoot.gameObject);
            previewModelRoot = null;
        }
        DestroyPreviewResources();

        PlayerController player = FindFirstObjectByType<PlayerController>();
        Transform source = null;
        if (player != null)
        {
            playerName = "player1";
            source = player.transform.Find("PlayerVisual");
            if (source == null) source = player.transform;
        }

        GameObject model = new GameObject("InventoryCharacterPreviewModel");
        model.transform.position = new Vector3(10000f, 10000f, 10000f);
        model.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        previewModelRoot = model.transform;
        if (source != null) CopyVisualHierarchy(source, previewModelRoot, true);
        if (previewModelRoot.GetComponentsInChildren<Renderer>(true).Length == 0)
            CreatePlaceholderMannequin(previewModelRoot);
        SetLayerRecursively(previewModelRoot.gameObject, PreviewLayer);
        if (characterNameText != null) characterNameText.text = playerName;
    }

    private void CopyVisualHierarchy(Transform source, Transform parent, bool isRoot)
    {
        GameObject copy = new GameObject(source.name + "_Preview");
        copy.transform.SetParent(parent, false);
        bool isPlayerRoot = isRoot && source.GetComponent<PlayerController>() != null;
        copy.transform.localPosition = isPlayerRoot ? Vector3.zero : source.localPosition;
        copy.transform.localRotation = isPlayerRoot ? Quaternion.identity : source.localRotation;
        copy.transform.localScale = isPlayerRoot ? Vector3.one : source.localScale;

        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        Renderer sourceRenderer = source.GetComponent<Renderer>();
        if (sourceFilter != null && sourceFilter.sharedMesh != null && sourceRenderer is MeshRenderer)
        {
            copy.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            CopyRendererSettings(sourceRenderer, copy.AddComponent<MeshRenderer>());
        }
        else if (sourceRenderer is SkinnedMeshRenderer skinned)
        {
            Mesh baked = new Mesh { name = source.name + "_InventoryPreviewMesh" };
            skinned.BakeMesh(baked);
            previewMeshes.Add(baked);
            copy.AddComponent<MeshFilter>().sharedMesh = baked;
            CopyRendererSettings(sourceRenderer, copy.AddComponent<MeshRenderer>());
        }
        for (int i = 0; i < source.childCount; i++)
            CopyVisualHierarchy(source.GetChild(i), copy.transform, false);
    }

    private static void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.sharedMaterials = source.sharedMaterials;
        target.shadowCastingMode = ShadowCastingMode.Off;
        target.receiveShadows = true;
        target.lightProbeUsage = LightProbeUsage.Off;
        target.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private void CreatePlaceholderMannequin(Transform parent)
    {
        CreatePreviewPrimitive(parent, "PreviewBody", PrimitiveType.Capsule,
            new Vector3(0f, 1.02f, 0f), new Vector3(0.48f, 0.62f, 0.32f), new Color(0.31f, 0.29f, 0.25f, 1f));
        CreatePreviewPrimitive(parent, "PreviewHead", PrimitiveType.Sphere,
            new Vector3(0f, 1.82f, 0f), new Vector3(0.3f, 0.32f, 0.3f), new Color(0.48f, 0.4f, 0.31f, 1f));
        CreatePreviewPrimitive(parent, "PreviewLeftArm", PrimitiveType.Capsule,
            new Vector3(-0.34f, 1.02f, 0f), new Vector3(0.16f, 0.45f, 0.16f), new Color(0.26f, 0.24f, 0.2f, 1f));
        CreatePreviewPrimitive(parent, "PreviewRightArm", PrimitiveType.Capsule,
            new Vector3(0.34f, 1.02f, 0f), new Vector3(0.16f, 0.45f, 0.16f), new Color(0.26f, 0.24f, 0.2f, 1f));
        CreatePreviewPrimitive(parent, "PreviewLeftLeg", PrimitiveType.Capsule,
            new Vector3(-0.14f, 0.38f, 0f), new Vector3(0.18f, 0.43f, 0.18f), new Color(0.22f, 0.2f, 0.17f, 1f));
        CreatePreviewPrimitive(parent, "PreviewRightLeg", PrimitiveType.Capsule,
            new Vector3(0.14f, 0.38f, 0f), new Vector3(0.18f, 0.43f, 0.18f), new Color(0.22f, 0.2f, 0.17f, 1f));
    }

    private void CreatePreviewPrimitive(Transform parent, string name, PrimitiveType type,
        Vector3 position, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.color = color;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            previewMaterials.Add(material);
        }
        SetLayerRecursively(part, PreviewLayer);
    }

    private void CreateEquipmentSlot(Transform parent, string label, Vector2 position,
        float size = 52f, float labelHeight = 16f, bool showLabel = true)
    {
        Image border = CreateFixedImage(parent, "EquipmentSlot_" + label, new Vector2(size, size),
            position, new Color(0.29f, 0.25f, 0.2f, 1f));
        CreateFixedImage(border.transform, "SlotDark", new Vector2(size - 4f, size - 4f),
            Vector2.zero, new Color(0.045f, 0.044f, 0.039f, 1f));
        CreateFixedImage(border.transform, "SlotHighlight", new Vector2(size - 8f, 1f),
            new Vector2(0f, size * 0.5f - 5f), new Color(0.18f, 0.16f, 0.13f, 1f));
        if (showLabel)
            CreateText(parent, "EquipmentLabel_" + label, label, 10, FontStyle.Normal,
                new Color(0.56f, 0.52f, 0.44f, 1f), new Vector2(size + 12f, labelHeight),
                position + new Vector2(0f, -size * 0.5f - labelHeight * 0.5f - 1f));
    }

    private void SetTab(bool quest)
    {
        if (itemsTabBackground != null) itemsTabBackground.color = quest
            ? new Color(0.13f, 0.12f, 0.1f, 1f) : new Color(0.28f, 0.235f, 0.17f, 1f);
        if (questTabBackground != null) questTabBackground.color = quest
            ? new Color(0.28f, 0.235f, 0.17f, 1f) : new Color(0.13f, 0.12f, 0.1f, 1f);
        if (itemsTabText != null)
        {
            itemsTabText.fontStyle = quest ? FontStyle.Normal : FontStyle.Bold;
            itemsTabText.color = quest ? new Color(0.69f, 0.64f, 0.54f, 1f) : new Color(0.91f, 0.85f, 0.72f, 1f);
        }
        if (questTabText != null)
        {
            questTabText.fontStyle = quest ? FontStyle.Bold : FontStyle.Normal;
            questTabText.color = quest ? new Color(0.91f, 0.85f, 0.72f, 1f) : new Color(0.69f, 0.64f, 0.54f, 1f);
        }
        if (capacityText != null) capacityText.text = "0 / 80";
        if (weightText != null) weightText.text = "Вес: 0.0%";
    }

    private GameObject CreateButton(Transform parent, string name, Vector2 size, Vector2 position,
        Color background, string caption, int fontSize, Color textColor)
    {
        Image image = CreateFixedImage(parent, name, size, position, background);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = Color.Lerp(background, new Color(0.62f, 0.52f, 0.37f, 1f), 0.28f);
        colors.pressedColor = Color.Lerp(background, Color.black, 0.25f);
        colors.selectedColor = background;
        button.colors = colors;
        CreateText(image.transform, "Caption", caption, fontSize, FontStyle.Normal, textColor,
            size - new Vector2(2f, 2f), Vector2.zero);
        return image.gameObject;
    }

    private Text CreateText(Transform parent, string name, string value, int fontSize, FontStyle style,
        Color color, Vector2 size, Vector2 position)
    {
        GameObject textObject = CreateRect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(-size.x * 0.5f, -size.y * 0.5f),
            new Vector2(size.x * 0.5f, size.y * 0.5f));
        textObject.GetComponent<RectTransform>().anchoredPosition = position;
        Text text = textObject.AddComponent<Text>();
        text.font = uiFont;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateFixedImage(Transform parent, string name, Vector2 size, Vector2 position,
        Color color, float rotation = 0f)
    {
        GameObject go = CreateRect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(-size.x * 0.5f, -size.y * 0.5f),
            new Vector2(size.x * 0.5f, size.y * 0.5f));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject CreateStretchImage(Transform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject go = CreateRect(parent, name, anchorMin, anchorMax,
            new Vector2(0.5f, 0.5f), offsetMin, offsetMax);
        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return go;
    }

    private static GameObject CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return go;
    }

    private void DestroyPreviewResources()
    {
        foreach (Mesh mesh in previewMeshes) if (mesh != null) Destroy(mesh);
        foreach (Material material in previewMaterials) if (material != null) Destroy(material);
        previewMeshes.Clear();
        previewMaterials.Clear();
    }

    private static void SetLayerRecursively(GameObject node, int layer)
    {
        node.layer = layer;
        foreach (Transform child in node.transform) SetLayerRecursively(child.gameObject, layer);
    }
}
