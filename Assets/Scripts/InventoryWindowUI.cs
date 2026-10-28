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
        if (hudButtonBackground != null) Destroy(hudButtonBackground);
        if (hudButtonHover != null) Destroy(hudButtonHover);
        if (hudButtonPressed != null) Destroy(hudButtonPressed);
        if (hudButtonBorder != null) Destroy(hudButtonBorder);
    }

    /// <summary>Entry point used by the always-visible server HUD.</summary>
    public static void ToggleFromHUD()
    {
        InventoryWindowUI inventory = FindFirstObjectByType<InventoryWindowUI>();
        if (inventory == null)
        {
            GameObject ui = new GameObject("InventoryWindowUI");
            DontDestroyOnLoad(ui);
            inventory = ui.AddComponent<InventoryWindowUI>();
        }

        inventory.Toggle();
    }

    public void Toggle() => SetOpen(!IsOpen);

    public void SetOpen(bool open)
    {
        IsOpen = open;
        if (window != null) window.SetActive(open);
        if (previewCamera != null) previewCamera.enabled = open;
        if (open) RefreshCharacterPreview();
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
        canvasObject.transform.SetParent(transform, false);
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
    }

    private GUIStyle hudToggleStyle;
    private Texture2D hudButtonBackground;
    private Texture2D hudButtonHover;
    private Texture2D hudButtonPressed;
    private Texture2D hudButtonBorder;

    private void OnGUI()
    {
        // IMGUI is rendered independently of the screen-space Canvas and EventSystem,
        // so the open-inventory control remains visible even if a UI module/canvas is misconfigured.
        EnsureHudToggleStyle();

        float scale = Mathf.Clamp(Screen.width / 1920f, 0.75f, 2f);
        float width = 318f * scale;
        float height = 70f * scale;
        float margin = 22f * scale;
        Rect rect = new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
        float border = 2f * scale;

        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height), hudButtonBorder, ScaleMode.StretchToFill);
        Rect inner = new Rect(rect.x + border, rect.y + border, rect.width - border * 2f, rect.height - border * 2f);
        string caption = IsOpen ? "ЗАКРЫТЬ ИНВЕНТАРЬ  [I]" : "ОТКРЫТЬ ИНВЕНТАРЬ  [I]";
        if (GUI.Button(inner, caption, hudToggleStyle))
        {
            Toggle();
        }
    }

    private void EnsureHudToggleStyle()
    {
        if (hudToggleStyle != null) return;

        hudButtonBackground = CreateHudTexture(new Color(0.075f, 0.062f, 0.045f, 0.98f));
        hudButtonHover = CreateHudTexture(new Color(0.22f, 0.17f, 0.095f, 1f));
        hudButtonPressed = CreateHudTexture(new Color(0.035f, 0.03f, 0.022f, 1f));
        hudButtonBorder = CreateHudTexture(new Color(0.76f, 0.59f, 0.30f, 1f));

        hudToggleStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 17,
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(8, 8, 4, 4),
            normal = { background = hudButtonBackground, textColor = new Color(1f, 0.88f, 0.63f, 1f) },
            hover = { background = hudButtonHover, textColor = Color.white },
            active = { background = hudButtonPressed, textColor = Color.white },
            focused = { background = hudButtonBackground, textColor = new Color(1f, 0.88f, 0.63f, 1f) }
        };
    }

    private static Texture2D CreateHudTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "InventoryHudButtonTexture",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        return texture;
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
