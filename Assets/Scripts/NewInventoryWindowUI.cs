using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal first pass of the new inventory window: just the supplied frame
/// texture and a translucent dark-blue interior. The legacy inventory UI is not
/// referenced or modified by this component.
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
        _frameTexture = Resources.Load<Texture2D>("inventory_current");
        if (_frameTexture == null)
        {
            Debug.LogError("NewInventoryWindowUI: could not load Assets/Resources/inventory_current.png.", this);
        }

        _blueBackgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "NewInventoryWindowBlueBackground",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _blueBackgroundTexture.SetPixel(0, 0, new Color(0.015f, 0.055f, 0.18f, 0.80f));
        _blueBackgroundTexture.Apply(false, true);
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

        // The blue layer is intentionally the only content beneath the frame.
        // Its alpha allows the game world to remain faintly visible through it.
        float inset = 28f * scale;
        Rect interiorRect = new Rect(
            frameRect.x + inset,
            frameRect.y + inset,
            frameRect.width - inset * 2f,
            frameRect.height - inset * 2f);

        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(interiorRect, _blueBackgroundTexture, ScaleMode.StretchToFill, true);

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

        if (_blueBackgroundTexture != null)
        {
            Destroy(_blueBackgroundTexture);
        }
    }
}
