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

        _blueBackgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "NewInventoryDarkBlueBackground",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _blueBackgroundTexture.SetPixel(0, 0, new Color(0.012f, 0.045f, 0.15f, 0.80f));
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
