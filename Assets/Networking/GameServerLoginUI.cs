using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Automatically connects to the local Go server and logs in with the development
/// account. A small status panel stays in the lower-left corner while debugging.
/// </summary>
public sealed class GameServerLoginUI : MonoBehaviour
{
    private const int DefaultPort = 7777;
    private const string AutoLoginUsername = "player1";
    private const string AutoLoginPassword = "player1";
    private const int ConnectTimeoutMilliseconds = 5000;
    private const int ResponseTimeoutMilliseconds = 10000;

    [Serializable]
    private sealed class AuthRequest
    {
        public string type = "auth";
        public string username;
        public string password;
    }

    [Serializable]
    private sealed class AuthResponse
    {
        public string type;
        public bool success;
        public string message;
        public int protocolVersion;
        public string playerId;
    }

    [Serializable]
    private sealed class ServerMessage
    {
        public string type;
        public bool success;
        public string message;
        public string playerId;
        public string itemId;
        public string itemName;
        public WorldInventoryStack[] inventory;
        public float x;
        public float y;
        public float z;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW;
        public bool positionIsRoot;
        public float visibilityRange;
        public float attackSpeed;
        public float retryAfter;
        public int requestId;
        public int playerHealth;
        public int playerMaxHealth;
        public WorldMonsterData monster;
        public WorldPlayerData[] players;
        public WorldMonsterData[] monsters;
        public WorldGroundItemData[] items;
        public WorldTargetData target;
        // Client-only marker: this packet contains one entity event, not a full world snapshot.
        public bool partialSnapshot;
    }

    [Serializable]
    private sealed class WorldVector3
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    private sealed class WorldQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    private sealed class WorldColliderData
    {
        public string shape;
        public float radius;
        public float height;
        public WorldVector3 size;
    }

    [Serializable]
    private sealed class WorldPlayerData
    {
        public string playerId;
        public int health;
        public int maxHealth;
        public WorldVector3 position;
        public WorldQuaternion rotation;
        public WorldColliderData collider;
    }

    [Serializable]
    private sealed class WorldMonsterData
    {
        public string id;
        public string templateId;
        public string name;
        public WorldVector3 position;
        public WorldQuaternion rotation;
        public WorldColliderData collider;
        public int health;
        public int maxHealth;
        public bool alive;
        public float deathElapsedSeconds;
        public bool spawning;
        public float spawnElapsedSeconds;
        public WorldVector3 spawnStartPosition;
        public float groundY;
        public float verticalVelocity;
        public float fallElapsedSeconds;
        public string action;
        public string aiState;
    }

    [Serializable]
    private sealed class WorldGroundItemData
    {
        public string id;
        public string itemTemplate;
        public string name;
        public WorldVector3 position;
        public int quantity;
    }

    [Serializable]
    private sealed class WorldInventoryStack
    {
        public string itemTemplate;
        public string name;
        public string category;
        public bool stackable;
        public int quantity;
    }

    [Serializable]
    private sealed class WorldTargetData
    {
        public string type;
        public string id;
        public string name;
    }

    [Serializable]
    private sealed class TargetRequest
    {
        public string type = "set_target";
        public string targetType;
        public string targetId;
    }

    [Serializable]
    private sealed class AttackRequest
    {
        // The attack order has its own target, independent of the UI selection.
        public string type = "attack";
        public string targetType;
        public string targetId;
        public int requestId;
    }

    private sealed class QueuedServerMessage
    {
        public int operationId;
        public string json;
    }

    [Serializable]
    private sealed class PlayerStateRequest
    {
        public string type = "player_state";
        public float x;
        public float y;
        public float z;
        public bool hasMoveTarget;
        public float moveTargetX;
        public float moveTargetZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW;
    }

    [Serializable]
    private sealed class WorldInterestRequest
    {
        public string type = "world_interest";
        public float visibilityRange;
    }

    private string _host = "127.0.0.1";
    private readonly string _port = DefaultPort.ToString();
    [SerializeField, Range(50f, 500f)] private float visibilityRange = 100f;
    [SerializeField, Min(0.25f)] private float worldInterestUpdateInterval = 1f;
    private string _status = "Ожидание запуска...";
    private string _authenticatedUsername = string.Empty;

    private PlayerController _cachedPlayerController;
    private float _nextWorldInterestTime;
    private Vector3 _lastWorldInterestPosition;
    private bool _hasSentWorldInterest;
    private string _lastSnapshotSummary = string.Empty;
    private readonly Dictionary<string, GameObject> _monsterViews =
        new Dictionary<string, GameObject>();
    private readonly Dictionary<string, string> _monsterTemplateIds =
        new Dictionary<string, string>();
    private readonly Dictionary<string, Material> _monsterMaterials =
        new Dictionary<string, Material>();
    private readonly Dictionary<string, Texture2D> _monsterTextures =
        new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, GameObject> _otherPlayerCollisionViews =
        new Dictionary<string, GameObject>();
    private readonly Dictionary<string, GameObject> _groundItemViews =
        new Dictionary<string, GameObject>();
    private Terrain _worldTerrain;
    private Material _smileyFaceMaterial;
    private Material _dogMuzzleMaterial;
    private Material _otherPlayerMaterial;
    private Material _groundItemMaterial;
    private string _selectedTargetType = string.Empty;
    private string _selectedTargetID = string.Empty;
    private bool _hasPendingTargetSelection;
    private float _serverAttackSpeed = 1f;
    private bool _hasServerAttackSpeed;
    private int _playerHealth = 100;
    private int _playerMaxHealth = 100;
    private WorldInventoryStack[] _playerInventory = Array.Empty<WorldInventoryStack>();

    private GUIStyle _inventoryHudButtonStyle;
    private Texture2D _inventoryHudBorderTexture;
    private Texture2D _inventoryHudBackgroundTexture;
    private Texture2D _inventoryHudHoverTexture;
    private Texture2D _inventoryHudPressedTexture;

    private TcpClient _client;
    private NetworkStream _stream;
    private StreamReader _reader;
    private StreamWriter _writer;

    private bool _isConnecting;
    private bool _isConnected;
    private bool _isAuthenticating;
    private bool _isAuthenticated;
    private int _operationId;

    private readonly ConcurrentQueue<QueuedServerMessage> _incomingMessages =
        new ConcurrentQueue<QueuedServerMessage>();
    private bool _hasPendingSpawn;
    private bool _hasAppliedSpawn;
    private Vector3 _pendingSpawnSurfacePosition;
    private Quaternion _pendingSpawnRotation = Quaternion.identity;
    private bool _pendingSpawnPositionIsRoot;

    private readonly object _outgoingLock = new object();
    private string _latestOutgoingPlayerState;
    private string _latestOutgoingWorldInterest;
    private string _latestOutgoingTarget;
    private string _latestOutgoingAttack;
    private SemaphoreSlim _outgoingSignal = new SemaphoreSlim(0);

    public static GameServerLoginUI Instance { get; private set; }

    public bool IsReadyForPlayerState
    {
        get { return _isConnected && _isAuthenticated && _hasAppliedSpawn; }
    }

    /// <summary>Effective attacks per second last supplied by the game server.</summary>
    public float ServerAttackSpeed => _serverAttackSpeed;
    public bool HasServerAttackSpeed => _hasServerAttackSpeed;
    public int PlayerHealth => _playerHealth;
    public int PlayerMaxHealth => _playerMaxHealth;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateLoginPanel()
    {
        if (FindFirstObjectByType<GameServerLoginUI>() != null)
        {
            return;
        }

        GameObject panel = new GameObject("GameServerLoginUI");
        DontDestroyOnLoad(panel);
        panel.AddComponent<GameServerLoginUI>();
    }

    private void Start()
    {
        // Development mode: connect and authenticate automatically on Play.
        ConnectToServerAsync();
    }

    private void Update()
    {
        while (_incomingMessages.TryDequeue(out QueuedServerMessage queued))
        {
            if (queued == null || queued.operationId != _operationId)
            {
                continue;
            }

            ProcessServerMessage(queued.json);
        }

        ApplyPendingSpawn();
        UpdateWorldInterest();
    }

    private void OnDestroy()
    {
        DisconnectFromServer(false);
        if (_inventoryHudBorderTexture != null) Destroy(_inventoryHudBorderTexture);
        if (_inventoryHudBackgroundTexture != null) Destroy(_inventoryHudBackgroundTexture);
        if (_inventoryHudHoverTexture != null) Destroy(_inventoryHudHoverTexture);
        if (_inventoryHudPressedTexture != null) Destroy(_inventoryHudPressedTexture);
        foreach (Material material in _monsterMaterials.Values)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
        _monsterMaterials.Clear();

        foreach (Texture2D texture in _monsterTextures.Values)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        _monsterTextures.Clear();

        if (_smileyFaceMaterial != null)
        {
            Destroy(_smileyFaceMaterial);
            _smileyFaceMaterial = null;
        }
        if (_dogMuzzleMaterial != null)
        {
            Destroy(_dogMuzzleMaterial);
            _dogMuzzleMaterial = null;
        }
        if (_otherPlayerMaterial != null)
        {
            Destroy(_otherPlayerMaterial);
            _otherPlayerMaterial = null;
        }
        if (_groundItemMaterial != null)
        {
            Destroy(_groundItemMaterial);
            _groundItemMaterial = null;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnGUI()
    {
        float panelWidth = Mathf.Min(300f, Screen.width - 24f);
        float panelHeight = _isConnecting ? 92f : (_isConnected && _isAuthenticated ? 176f : 126f);
        Rect panelRect = new Rect(
            12f,
            Mathf.Max(12f, Screen.height - panelHeight - 12f),
            panelWidth,
            panelHeight);

        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.BeginVertical();
        GUILayout.Label("GAME SERVER", TitleStyle());
        GUILayout.Label(_host + ":" + _port + "  |  " + _status, StatusStyle());

        if (_isAuthenticated)
        {
            GUILayout.Label("Вход выполнен: " + _authenticatedUsername);
            GUILayout.Label("Дальность отрисовки: " + Mathf.RoundToInt(visibilityRange));
            float adjustedRange = GUILayout.HorizontalSlider(
                Mathf.Clamp(visibilityRange, 50f, 500f), 50f, 500f);
            if (Mathf.Abs(adjustedRange - visibilityRange) > 0.1f)
            {
                visibilityRange = Mathf.Clamp(adjustedRange, 50f, 500f);
                _nextWorldInterestTime = 0f;
            }
        }

        if (_isConnecting)
        {
            GUILayout.Label("Подключение...");
            if (GUILayout.Button("Отменить", GUILayout.Height(22f)))
            {
                DisconnectFromServer();
            }
        }
        else if (!_isConnected)
        {
            if (GUILayout.Button("Повторить подключение", GUILayout.Height(24f)))
            {
                ConnectToServerAsync();
            }
        }
        else if (!_isAuthenticated)
        {
            if (_isAuthenticating)
            {
                GUILayout.Label("Авторизация...");
            }
            else if (GUILayout.Button("Повторить авторизацию", GUILayout.Height(24f)))
            {
                AuthenticateAsync();
            }
            if (GUILayout.Button("Отключиться", GUILayout.Height(22f)))
            {
                DisconnectFromServer();
            }
        }
        else if (GUILayout.Button("Отключиться", GUILayout.Height(22f)))
        {
            DisconnectFromServer();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();

        DrawInventoryToggle();
        // Draw the window in this proven, active IMGUI pass instead of relying on a
        // separate component's OnGUI callback.
        NewInventoryWindowUI.DrawOverlayFromHUD();
    }

    /// <summary>
    /// IMGUI uses top-left screen coordinates; Input System pointer positions use
    /// bottom-left coordinates. This identifies clicks on the inventory HUD button
    /// before PlayerController treats the same press as a world click.
    /// </summary>
    public static bool IsPointerOverInventoryToggle(Vector2 pointerBottomLeft)
    {
        float scale = Mathf.Clamp(Screen.width / 1920f, 0.8f, 1.5f);
        float width = 370f * scale;
        float height = 76f * scale;
        float margin = 22f * scale;
        Rect guiRect = new Rect(
            Screen.width - width - margin,
            Screen.height - height - margin,
            width,
            height);
        Vector2 guiPosition = new Vector2(pointerBottomLeft.x, Screen.height - pointerBottomLeft.y);
        return guiRect.Contains(guiPosition);
    }

    private void DrawInventoryToggle()
    {
        EnsureInventoryHudButtonStyle();

        float scale = Mathf.Clamp(Screen.width / 1920f, 0.8f, 1.5f);
        float width = 370f * scale;
        float height = 76f * scale;
        float margin = 22f * scale;
        Rect rect = new Rect(
            Screen.width - width - margin,
            Screen.height - height - margin,
            width,
            height);

        GUI.DrawTexture(rect, _inventoryHudBorderTexture, ScaleMode.StretchToFill);
        float border = 2f * scale;
        Rect buttonRect = new Rect(
            rect.x + border,
            rect.y + border,
            rect.width - border * 2f,
            rect.height - border * 2f);

        string caption = NewInventoryWindowUI.IsOpen
            ? "ЗАКРЫТЬ ИНВЕНТАРЬ  [I]"
            : "ОТКРЫТЬ ИНВЕНТАРЬ  [I]";

        if (GUI.Button(buttonRect, caption, _inventoryHudButtonStyle))
        {
            NewInventoryWindowUI.ToggleFromHUD();
        }
    }

    private void EnsureInventoryHudButtonStyle()
    {
        if (_inventoryHudButtonStyle != null) return;

        _inventoryHudBorderTexture = CreateSolidHudTexture(
            new Color(0.76f, 0.59f, 0.30f, 1f), "InventoryHudBorder");
        _inventoryHudBackgroundTexture = CreateSolidHudTexture(
            new Color(0.075f, 0.062f, 0.045f, 0.98f), "InventoryHudBackground");
        _inventoryHudHoverTexture = CreateSolidHudTexture(
            new Color(0.22f, 0.17f, 0.095f, 1f), "InventoryHudHover");
        _inventoryHudPressedTexture = CreateSolidHudTexture(
            new Color(0.035f, 0.03f, 0.022f, 1f), "InventoryHudPressed");

        _inventoryHudButtonStyle = new GUIStyle(GUI.skin.button);
        _inventoryHudButtonStyle.alignment = TextAnchor.MiddleCenter;
        _inventoryHudButtonStyle.fontStyle = FontStyle.Bold;
        _inventoryHudButtonStyle.fontSize = Mathf.RoundToInt(18f * Mathf.Clamp(Screen.width / 1920f, 0.8f, 1.5f));
        _inventoryHudButtonStyle.wordWrap = false;
        _inventoryHudButtonStyle.clipping = TextClipping.Clip;
        _inventoryHudButtonStyle.padding = new RectOffset(8, 8, 4, 4);
        _inventoryHudButtonStyle.normal.background = _inventoryHudBackgroundTexture;
        _inventoryHudButtonStyle.normal.textColor = new Color(1f, 0.88f, 0.63f, 1f);
        _inventoryHudButtonStyle.hover.background = _inventoryHudHoverTexture;
        _inventoryHudButtonStyle.hover.textColor = Color.white;
        _inventoryHudButtonStyle.active.background = _inventoryHudPressedTexture;
        _inventoryHudButtonStyle.active.textColor = Color.white;
        _inventoryHudButtonStyle.focused.background = _inventoryHudBackgroundTexture;
        _inventoryHudButtonStyle.focused.textColor = new Color(1f, 0.88f, 0.63f, 1f);
    }

    private static Texture2D CreateSolidHudTexture(Color color, string textureName)
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

    private async void ConnectToServerAsync()
    {
        if (_isConnecting || _isConnected)
        {
            return;
        }

        string host = _host.Trim();
        int port = DefaultPort;

        int operationId = ++_operationId;
        _isConnecting = true;
        _status = "Подключение...";
        TcpClient client = new TcpClient();
        _client = client;

        try
        {
            Task connectTask = client.ConnectAsync(host, port);
            Task completed = await Task.WhenAny(
                connectTask,
                Task.Delay(ConnectTimeoutMilliseconds));

            if (completed != connectTask)
            {
                client.Close();
                try
                {
                    await connectTask;
                }
                catch
                {
                    // Observe the task after timing out.
                }

                throw new TimeoutException("сервер не ответил за 5 секунд");
            }

            await connectTask;
            if (operationId != _operationId)
            {
                client.Close();
                return;
            }

            _stream = client.GetStream();
            _reader = new StreamReader(_stream, new UTF8Encoding(false));
            _writer = new StreamWriter(_stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            _isConnected = true;
            _status = "TCP подключён";
            Debug.Log("GameServer: TCP connected to " + host + ":" + port);

            // Start authentication immediately after a successful TCP connection.
            AuthenticateAsync();
        }
        catch (Exception exception)
        {
            if (operationId == _operationId)
            {
                CloseConnectionResources();
                _isConnected = false;
                _isAuthenticated = false;
                _status = "Ошибка подключения: " + exception.Message;
                Debug.LogError(
                    "GameServer: TCP connection failed to " + host + ":" + port +
                    " (" + exception.GetType().Name + ": " + exception.Message + ")");
            }
        }
        finally
        {
            if (operationId == _operationId)
            {
                _isConnecting = false;
            }
        }
    }

    private async void AuthenticateAsync()
    {
        if (!_isConnected || _isAuthenticating || _isAuthenticated)
        {
            return;
        }

        int operationId = _operationId;
        StreamReader reader = _reader;
        StreamWriter writer = _writer;
        _isAuthenticating = true;
        _status = "Авторизация...";

        try
        {
            AuthRequest request = new AuthRequest
            {
                type = "auth",
                username = AutoLoginUsername,
                password = AutoLoginPassword
            };

            // The serialized request is sent, never written to logs.
            await writer.WriteLineAsync(JsonUtility.ToJson(request));

            Task<string> responseTask = reader.ReadLineAsync();
            Task completed = await Task.WhenAny(
                responseTask,
                Task.Delay(ResponseTimeoutMilliseconds));

            if (completed != responseTask)
            {
                throw new TimeoutException("сервер не ответил на запрос авторизации за 10 секунд");
            }

            string responseLine = await responseTask;
            if (operationId != _operationId)
            {
                return;
            }

            if (string.IsNullOrEmpty(responseLine))
            {
                throw new IOException("сервер закрыл соединение");
            }

            AuthResponse response = JsonUtility.FromJson<AuthResponse>(responseLine);
            if (response == null || response.type != "auth_result")
            {
                throw new InvalidDataException("неожиданный ответ сервера");
            }

            if (!response.success)
            {
                string reason = string.IsNullOrWhiteSpace(response.message)
                    ? "invalid_credentials"
                    : response.message;

                _status = "Отказ: " + reason;
                // Never log the password or the complete request JSON.
                Debug.LogWarning(
                    "GameServer: authentication failed; username=" + AutoLoginUsername +
                    ", server=" + _host + ":" + DefaultPort +
                    ", reason=" + reason);

                // The server closes duplicate sessions immediately; don't leave the UI
                // claiming that the TCP connection is still available for another auth try.
                if (reason == "already_logged_in")
                {
                    DisconnectFromServer(false);
                    _status = "Отказ: " + reason;
                }
                return;
            }

            _isAuthenticated = true;
            _authenticatedUsername = string.IsNullOrWhiteSpace(response.playerId)
                ? AutoLoginUsername
                : response.playerId;
            _status = "Авторизован";
            Debug.Log(
                "GameServer: authentication succeeded; username=" + _authenticatedUsername +
                ", protocol=" + response.protocolVersion);

            // The server sends the spawn point as the next line after auth_result.
            // Keep reading subsequent server messages without blocking Unity's main thread.
            _ = ReceiveServerMessagesAsync(reader, operationId);

            // Player transforms share one coalescing async writer, so a slow connection
            // cannot accumulate an unbounded queue of obsolete movement updates.
            SemaphoreSlim sendSignal = new SemaphoreSlim(0);
            lock (_outgoingLock)
            {
                _latestOutgoingPlayerState = null;
                _latestOutgoingWorldInterest = null;
                _outgoingSignal = sendSignal;
            }
            _ = SendQueuedMessagesAsync(writer, sendSignal, operationId);
        }
        catch (Exception exception)
        {
            if (operationId == _operationId)
            {
                _status = "Ошибка авторизации: " + exception.Message;
                // Do not include the password or serialized request in logs.
                Debug.LogError(
                    "GameServer: authentication exchange failed; username=" + AutoLoginUsername +
                    " (" + exception.GetType().Name + ": " + exception.Message + ")");
                DisconnectFromServer(false);
            }
        }
        finally
        {
            if (operationId == _operationId)
            {
                _isAuthenticating = false;
            }
        }
    }

    private async Task ReceiveServerMessagesAsync(StreamReader reader, int operationId)
    {
        try
        {
            while (operationId == _operationId)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (operationId != _operationId)
                {
                    return;
                }

                if (line == null)
                {
                    QueueIncomingMessage(operationId, "{\"type\":\"connection_closed\"}");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    QueueIncomingMessage(operationId, line);
                }
            }
        }
        catch (Exception exception)
        {
            if (operationId == _operationId)
            {
                ServerMessage error = new ServerMessage
                {
                    type = "transport_error",
                    message = exception.Message
                };
                QueueIncomingMessage(operationId, JsonUtility.ToJson(error));
            }
        }
    }

    private void QueueIncomingMessage(int operationId, string json)
    {
        _incomingMessages.Enqueue(new QueuedServerMessage
        {
            operationId = operationId,
            json = json
        });
    }

    private void ProcessServerMessage(string json)
    {
        ServerMessage message;
        try
        {
            message = JsonUtility.FromJson<ServerMessage>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("GameServer: invalid server message (" + exception.Message + ")");
            return;
        }

        if (message == null || string.IsNullOrWhiteSpace(message.type))
        {
            Debug.LogWarning("GameServer: received an empty server message.");
            return;
        }

        switch (message.type)
        {
            case "spawn":
                if (!message.success)
                {
                    _status = "Ошибка получения точки появления: " + message.message;
                    Debug.LogError("GameServer: spawn request failed; reason=" + message.message);
                    return;
                }

                _pendingSpawnSurfacePosition = new Vector3(message.x, message.y, message.z);
                _pendingSpawnRotation = new Quaternion(
                    message.rotationX, message.rotationY, message.rotationZ, message.rotationW);
                if (_pendingSpawnRotation.x == 0f && _pendingSpawnRotation.y == 0f &&
                    _pendingSpawnRotation.z == 0f && _pendingSpawnRotation.w == 0f)
                {
                    _pendingSpawnRotation = Quaternion.identity;
                }
                else
                {
                    _pendingSpawnRotation = Quaternion.Normalize(_pendingSpawnRotation);
                }
                _pendingSpawnPositionIsRoot = message.positionIsRoot;
                _hasPendingSpawn = true;
                _status = "Авторизован; координаты получены";
                Debug.Log(
                    "GameServer: spawn received at " + _pendingSpawnSurfacePosition +
                    ", restoredSavedPosition=" + _pendingSpawnPositionIsRoot +
                    ", rotation=" + _pendingSpawnRotation);
                break;

            case "monster_spawned":
            case "monster_state":
                if (message.monster != null)
                {
                    message.partialSnapshot = true;
                    message.monsters = new[] { message.monster };
                    ApplyWorldSnapshot(message);
                }
                break;

            case "world_snapshot":
                if (!message.success)
                {
                    Debug.LogWarning("GameServer: world interest request rejected; reason=" + message.message);
                    _hasSentWorldInterest = true;
                    _nextWorldInterestTime = Time.unscaledTime + 0.25f;
                    return;
                }

                ApplyWorldSnapshot(message);
                break;

            case "item_picked_up":
                RemoveGroundItemView(message.itemId);
                if (string.Equals(message.playerId, _authenticatedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    _playerInventory = message.inventory ?? Array.Empty<WorldInventoryStack>();
                    Debug.Log("Picked up " + (string.IsNullOrWhiteSpace(message.itemName) ? "item" : message.itemName) +
                        "; inventory stacks=" + _playerInventory.Length + ".");
                }
                else
                {
                    Debug.Log("Ground item removed after pickup: " + message.itemId + ".");
                }
                break;

            case "player_state_result":
                if (!message.success)
                {
                    _status = "Ошибка синхронизации: " + message.message;
                    Debug.LogWarning(
                        "GameServer: rejected player state; reason=" + message.message);
                }
                else if (message.message == "state_registered")
                {
                    Debug.Log("GameServer: server registered initial player position and rotation.");
                }
                else if (message.message == "server_collision_correction" ||
                         message.message == "movement_retry_exhausted")
                {
                    if (_cachedPlayerController == null)
                    {
                        _cachedPlayerController = FindFirstObjectByType<PlayerController>();
                    }
                    if (_cachedPlayerController != null)
                    {
                        Quaternion correctedRotation = new Quaternion(
                            message.rotationX, message.rotationY,
                            message.rotationZ, message.rotationW);
                        if (correctedRotation.x == 0f && correctedRotation.y == 0f &&
                            correctedRotation.z == 0f && correctedRotation.w == 0f)
                        {
                            correctedRotation = _cachedPlayerController.transform.rotation;
                        }
                        else
                        {
                            correctedRotation = correctedRotation.normalized;
                        }

                        bool stopMovement = message.message == "movement_retry_exhausted";
                        _cachedPlayerController.ApplyServerCollisionCorrection(
                            new Vector3(message.x, message.y, message.z),
                            correctedRotation,
                            stopMovement);
                        if (stopMovement)
                        {
                            Debug.Log("GameServer: movement stopped after the configured no-progress timeout.");
                        }
                    }
                }
                else if (message.message == "server_ground_correction")
                {
                    if (_cachedPlayerController == null)
                    {
                        _cachedPlayerController = FindFirstObjectByType<PlayerController>();
                    }
                    if (_cachedPlayerController != null)
                    {
                        _cachedPlayerController.ApplyServerGroundCorrection(message.y);
                    }
                }
                break;

            case "attack_result":
                ApplyMonsterCombatResult(message.monster);
                HandleAttackResult(message);
                break;

            case "target_result":
                _hasPendingTargetSelection = false;
                if (message.success)
                {
                    ApplySelectedTarget(message.target);
                    Debug.Log(message.target == null
                        ? "GameServer: target cleared by server."
                        : "GameServer: target selected; type=" + message.target.type +
                          ", id=" + message.target.id + ", name=" + message.target.name);
                }
                else
                {
                    ApplySelectedTarget(null);
                    Debug.LogWarning("GameServer: target selection rejected; reason=" + message.message);
                }
                break;

            case "connection_closed":
                if (_isConnected)
                {
                    _status = "Сервер закрыл соединение";
                    Debug.LogWarning("GameServer: server closed the TCP connection.");
                    DisconnectFromServer(false);
                }
                break;

            case "transport_error":
                if (_isConnected)
                {
                    _status = "Ошибка чтения от сервера: " + message.message;
                    Debug.LogError("GameServer: TCP receive failed (" + message.message + ")");
                    DisconnectFromServer(false);
                }
                break;

            case "error":
            case "protocol_error":
                _status = "Ошибка сервера: " + message.message;
                Debug.LogWarning("GameServer: server error; type=" + message.type + ", reason=" + message.message);
                break;

            default:
                Debug.Log("GameServer: server message received; type=" + message.type);
                break;
        }
    }

    private void ApplyPendingSpawn()
    {
        if (!_hasPendingSpawn || !_isAuthenticated)
        {
            return;
        }

        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player == null)
        {
            // A scene may still be loading when the spawn message arrives. Keep the
            // position pending and apply it as soon as the player's controller exists.
            return;
        }

        player.SetSpawnPosition(
            _pendingSpawnSurfacePosition,
            _pendingSpawnRotation,
            _pendingSpawnPositionIsRoot);
        _cachedPlayerController = player;
        _hasPendingSpawn = false;
        _hasAppliedSpawn = true;
        _hasSentWorldInterest = false;
        _nextWorldInterestTime = 0f;
        _status = "Авторизован; персонаж появился";
    }

    private void UpdateWorldInterest()
    {
        if (!IsReadyForPlayerState)
        {
            return;
        }

        if (_cachedPlayerController == null)
        {
            _cachedPlayerController = FindFirstObjectByType<PlayerController>();
        }
        if (_cachedPlayerController == null)
        {
            return;
        }

        Vector3 position = _cachedPlayerController.transform.position;
        float now = Time.unscaledTime;
        bool movedEnough = !_hasSentWorldInterest ||
            (position - _lastWorldInterestPosition).sqrMagnitude >= 25f;
        if (!movedEnough && now < _nextWorldInterestTime)
        {
            return;
        }

        SendWorldInterest();
        _lastWorldInterestPosition = position;
        _hasSentWorldInterest = true;
        _nextWorldInterestTime = now + Mathf.Max(0.25f, worldInterestUpdateInterval);
    }

    private void SendWorldInterest()
    {
        if (!IsReadyForPlayerState || _writer == null)
        {
            return;
        }

        WorldInterestRequest request = new WorldInterestRequest
        {
            type = "world_interest",
            visibilityRange = Mathf.Clamp(visibilityRange, 50f, 500f)
        };
        string json = JsonUtility.ToJson(request);

        SemaphoreSlim signal;
        bool signalSender;
        lock (_outgoingLock)
        {
            signalSender = string.IsNullOrEmpty(_latestOutgoingPlayerState) &&
                string.IsNullOrEmpty(_latestOutgoingWorldInterest) &&
                string.IsNullOrEmpty(_latestOutgoingTarget);
            _latestOutgoingWorldInterest = json;
            signal = _outgoingSignal;
        }

        if (signalSender)
        {
            signal.Release();
        }
    }

    private void ApplyWorldSnapshot(ServerMessage message)
    {
        if (message.playerMaxHealth > 0)
        {
            _playerMaxHealth = message.playerMaxHealth;
            _playerHealth = Mathf.Clamp(message.playerHealth, 0, _playerMaxHealth);
        }

        if (!message.partialSnapshot)
        {
            _playerInventory = message.inventory ?? Array.Empty<WorldInventoryStack>();
        }

        if (message.attackSpeed > 0f &&
            !float.IsNaN(message.attackSpeed) &&
            !float.IsInfinity(message.attackSpeed))
        {
            float newAttackSpeed = Mathf.Clamp(message.attackSpeed, 0.1f, 10f);
            if (!_hasServerAttackSpeed || Mathf.Abs(_serverAttackSpeed - newAttackSpeed) > 0.01f)
            {
                Debug.Log("GameServer: effective attack speed = " + newAttackSpeed.ToString("0.##") +
                    " attacks/second.");
            }

            _serverAttackSpeed = newAttackSpeed;
            _hasServerAttackSpeed = true;
        }

        if (!message.partialSnapshot)
        {
            ApplyOtherPlayerCollisionViews(message.players);
        }

        WorldMonsterData[] monsters = message.monsters ?? Array.Empty<WorldMonsterData>();
        WorldGroundItemData[] items = message.items ?? Array.Empty<WorldGroundItemData>();
        HashSet<string> visibleMonsterIDs = new HashSet<string>();

        foreach (WorldMonsterData monster in monsters)
        {
            if (monster == null || string.IsNullOrWhiteSpace(monster.id) ||
                monster.position == null)
            {
                continue;
            }

            visibleMonsterIDs.Add(monster.id);
            string templateID = monster.templateId ?? string.Empty;
            // The server owns the collider definition; templateId only selects the visual mesh.
            string colliderShape = monster.collider != null &&
                !string.IsNullOrWhiteSpace(monster.collider.shape)
                    ? monster.collider.shape
                    : (templateID == "test-cube-enemy" ? "box" : "cylinder");
            float modelSize = 2f;
            bool hasView = _monsterViews.TryGetValue(monster.id, out GameObject view) &&
                view != null;
            bool templateChanged = _monsterTemplateIds.TryGetValue(monster.id, out string oldTemplate) &&
                oldTemplate != templateID;

            if (!hasView || templateChanged)
            {
                if (hasView)
                {
                    Destroy(view);
                    if (_monsterTemplateIds.TryGetValue(monster.id, out string staleTemplate))
                    {
                        DestroyMonsterAssets(monster.id, staleTemplate);
                    }
                }

                // Presentation comes from server-supplied templateId; id identifies this instance.
                PrimitiveType primitiveType = templateID == "test-cube-enemy"
                    ? PrimitiveType.Cube
                    : PrimitiveType.Sphere;
                view = GameObject.CreatePrimitive(primitiveType);
                view.name = monster.name + " (ID " + monster.id + ")";
                view.transform.localScale = Vector3.one * modelSize;

                Collider existingCollider = view.GetComponent<Collider>();
                if (colliderShape == "box")
                {
                    BoxCollider box = existingCollider as BoxCollider;
                    if (box == null)
                    {
                        if (existingCollider != null)
                        {
                            existingCollider.enabled = false;
                            Destroy(existingCollider);
                        }
                        box = view.AddComponent<BoxCollider>();
                    }
                    Vector3 worldBoxSize = monster.collider != null && monster.collider.size != null
                        ? new Vector3(
                            PositiveOrDefault(monster.collider.size.x, 2f),
                            PositiveOrDefault(monster.collider.size.y, 2f),
                            PositiveOrDefault(monster.collider.size.z, 2f))
                        : Vector3.one * 2f;
                    box.center = Vector3.zero;
                    box.size = worldBoxSize / modelSize;
                    box.isTrigger = false;
                }
                else
                {
                    if (existingCollider != null)
                    {
                        existingCollider.enabled = false;
                        Destroy(existingCollider);
                    }
                    CapsuleCollider cylinderProxy = view.AddComponent<CapsuleCollider>();
                    cylinderProxy.direction = 1;
                    cylinderProxy.center = Vector3.zero;
                    float worldRadius = monster.collider != null
                        ? PositiveOrDefault(monster.collider.radius, 1f)
                        : 1f;
                    float worldHeight = monster.collider != null
                        ? PositiveOrDefault(monster.collider.height, 2f)
                        : 2f;
                    cylinderProxy.radius = worldRadius / modelSize;
                    cylinderProxy.height = worldHeight / modelSize;
                    cylinderProxy.isTrigger = false;
                }

                Renderer renderer = view.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material material = GetMonsterMaterial(monster.id, templateID);
                    if (material != null)
                    {
                        renderer.sharedMaterial = material;
                    }
                }

                if (templateID == "test-sphere-enemy")
                {
                    CreateSmileyFace(view);
                }
                else if (templateID == "test-cube-enemy")
                {
                    CreateDogAppearance(view);
                }
                CreateMonsterNameLabel(view, monster);
                _monsterViews[monster.id] = view;
                _monsterTemplateIds[monster.id] = templateID;
            }

            if (view.name != monster.name + " (ID " + monster.id + ")")
            {
                view.name = monster.name + " (ID " + monster.id + ")";
            }

            view.transform.localScale = Vector3.one * modelSize;

            // Falling monsters are triggers on the client so a moving spawn collider
            // cannot depenetrate or push the CharacterController. The server moves the
            // monster aside before it reaches the player's space. Landed monsters and
            // visible corpses keep solid colliders.
            bool isFalling = monster.alive && string.Equals(
                monster.action, "falling", StringComparison.OrdinalIgnoreCase);
            Collider monsterCollision = view.GetComponent<Collider>();
            if (monsterCollision != null)
            {
                monsterCollision.isTrigger = isFalling;
                IgnoreGroundItemCollisionsForActor(monsterCollision);
            }

            MonsterDeathPresentation deathPresentation = view.GetComponent<MonsterDeathPresentation>();
            if (deathPresentation == null)
            {
                deathPresentation = view.AddComponent<MonsterDeathPresentation>();
            }

            // The server sends base/contact coordinates. Falling motion is predicted locally
            // and reconciled with server position/velocity; landed mobs align to this Terrain.
            // Once dead, let the client-owned roll animation control the corpse position so
            // periodic snapshots cannot snap the body back to its standing point.
            if (!deathPresentation.IsDead)
            {
                float baseY = monster.position.y;
                if (!isFalling &&
                    TryGetTerrainSurfaceY(monster.position.x, monster.position.z, out float terrainY))
                {
                    baseY = terrainY;
                }

                Vector3 serverViewPosition = new Vector3(
                    monster.position.x,
                    baseY + modelSize * 0.5f,
                    monster.position.z);
                MonsterMotionPresentation motionPresentation =
                    view.GetComponent<MonsterMotionPresentation>();
                if (motionPresentation == null)
                {
                    motionPresentation = view.AddComponent<MonsterMotionPresentation>();
                }

                // Falling is predicted locally between snapshots. The server still supplies
                // position, ground height and vertical velocity for authoritative corrections.
                if (isFalling && monster.spawnStartPosition != null)
                {
                    motionPresentation.ApplyServerState(
                        serverViewPosition,
                        true,
                        monster.groundY,
                        monster.verticalVelocity);
                }
                else
                {
                    motionPresentation.ApplyServerState(serverViewPosition, false, baseY, 0f);
                }
            }

            // Rotation always comes from the server until death begins. Identity (+Z forward)
            // is the explicit default for an entity without a valid server rotation.
            Quaternion rotation = monster.rotation != null
                ? new Quaternion(
                    monster.rotation.x, monster.rotation.y,
                    monster.rotation.z, monster.rotation.w)
                : Quaternion.identity;
            float rotationMagnitudeSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            if (!deathPresentation.IsDead)
            {
                view.transform.rotation = rotationMagnitudeSquared > 0.0001f
                    ? rotation.normalized
                    : Quaternion.identity;
            }

            Transform labelTransform = view.transform.Find("NameLabel");
            if (labelTransform != null)
            {
                labelTransform.localPosition = new Vector3(
                    0f, (modelSize * 0.5f + 0.45f) / modelSize, 0f);
                TextMesh textMesh = labelTransform.GetComponent<TextMesh>();
                if (textMesh != null)
                {
                    textMesh.text = monster.name;
                    WorldTargetable targetable = view.GetComponent<WorldTargetable>();
                    if (targetable == null)
                    {
                        targetable = view.AddComponent<WorldTargetable>();
                    }
                    targetable.Configure("monster", monster.id, monster.name, textMesh);
                    targetable.SetHealth(monster.health, monster.maxHealth, monster.alive);
                    targetable.SetAlive(monster.alive);
                }
            }

            deathPresentation.ApplyServerState(
                monster.alive, templateID, monster.deathElapsedSeconds,
                monster.spawning, monster.spawnElapsedSeconds);
        }

        // The server keeps dead monsters in snapshots during their death animation and
        // corpse fade. An omitted view has left the requested range or its corpse expired.
        if (!message.partialSnapshot)
        {
            List<string> staleIDs = new List<string>();
            foreach (KeyValuePair<string, GameObject> pair in _monsterViews)
            {
                if (!visibleMonsterIDs.Contains(pair.Key))
                {
                    if (pair.Value != null)
                    {
                        Destroy(pair.Value);
                    }
                    staleIDs.Add(pair.Key);
                }
            }
            foreach (string staleID in staleIDs)
            {
                if (_monsterTemplateIds.TryGetValue(staleID, out string staleTemplate))
                {
                    DestroyMonsterAssets(staleID, staleTemplate);
                }
                _monsterViews.Remove(staleID);
                _monsterTemplateIds.Remove(staleID);
            }

            ApplyGroundItemViews(items);
            if (!_hasPendingTargetSelection)
            {
                ApplySelectedTarget(message.target);
            }

            int playerCount = message.players == null ? 0 : message.players.Length;
            int itemCount = items.Length;
            string summary = "players=" + playerCount +
                ", monsters=" + monsters.Length +
                ", items=" + itemCount +
                ", range=" + Mathf.RoundToInt(message.visibilityRange);
            if (summary != _lastSnapshotSummary)
            {
                _lastSnapshotSummary = summary;
                Debug.Log("GameServer: world snapshot received; " + summary);
            }
        }
    }

    private void ApplyMonsterCombatResult(WorldMonsterData monster)
    {
        if (monster == null || string.IsNullOrWhiteSpace(monster.id) ||
            !_monsterViews.TryGetValue(monster.id, out GameObject view) || view == null)
        {
            return;
        }

        // Combat responses can arrive between world snapshots. Keep the local collision
        // mode in sync immediately: falling mobs are triggers, while dead visible corpses
        // remain solid.
        Collider monsterCollision = view.GetComponent<Collider>();
        if (monsterCollision != null)
        {
            monsterCollision.isTrigger = monster.alive && string.Equals(
                monster.action, "falling", StringComparison.OrdinalIgnoreCase);
        }

        WorldTargetable targetable = view.GetComponent<WorldTargetable>();
        if (targetable != null)
        {
            targetable.SetHealth(monster.health, monster.maxHealth, monster.alive);
            targetable.SetAlive(monster.alive);
        }

        MonsterMotionPresentation motionPresentation = view.GetComponent<MonsterMotionPresentation>();
        if (!monster.alive && motionPresentation != null)
        {
            // Once the server confirms death, the death presentation owns the transform.
            motionPresentation.Stop();
        }

        MonsterDeathPresentation deathPresentation = view.GetComponent<MonsterDeathPresentation>();
        if (deathPresentation == null)
        {
            deathPresentation = view.AddComponent<MonsterDeathPresentation>();
        }
        deathPresentation.ApplyServerState(
            monster.alive, monster.templateId, monster.deathElapsedSeconds,
            monster.spawning, monster.spawnElapsedSeconds);
    }

    private static float PositiveOrDefault(float value, float fallback)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)
            ? value
            : fallback;
    }

    private void DestroyMonsterAssets(string monsterID, string templateID)
    {
        string materialKey = monsterID + "|" + templateID;
        if (_monsterMaterials.TryGetValue(materialKey, out Material material))
        {
            if (material != null)
            {
                Destroy(material);
            }
            _monsterMaterials.Remove(materialKey);
        }

        if (_monsterTextures.TryGetValue(materialKey, out Texture2D texture))
        {
            if (texture != null)
            {
                Destroy(texture);
            }
            _monsterTextures.Remove(materialKey);
        }
    }

    private Material GetMonsterMaterial(string monsterID, string templateID)
    {
        string materialKey = monsterID + "|" + templateID;
        if (_monsterMaterials.TryGetValue(materialKey, out Material existing) && existing != null)
        {
            return existing;
        }

        bool isSmiley = templateID == "test-sphere-enemy";
        Texture2D texture = null;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }
        if (shader == null)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
            Debug.LogWarning("GameServer: no compatible shader found for test monsters.");
            return null;
        }

        Material material = new Material(shader);
        material.name = isSmiley ? "Enemy2_YellowSphereMaterial" : "Enemy1_DogCoatMaterial";
        material.mainTexture = null;
        material.color = isSmiley
            ? new Color(1f, 0.84f, 0.02f)
            : new Color(0.58f, 0.34f, 0.16f);

        if (texture != null)
        {
            _monsterTextures[materialKey] = texture;
        }
        _monsterMaterials[materialKey] = material;
        return material;
    }

    private static Texture2D CreateWarningTexture()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];
        Color32 dark = new Color32(34, 39, 45, 255);
        Color32 orange = new Color32(255, 145, 20, 255);
        Color32 light = new Color32(235, 235, 220, 255);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 color = (((x + y) / 15) % 2 == 0) ? orange : dark;
                if (x < 4 || x >= size - 4 || y < 4 || y >= size - 4)
                {
                    color = light;
                }
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(false, true);
        return texture;
    }

    private Material GetOrCreateSolidMaterial(ref Material material, string materialName, Color color)
    {
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        if (shader == null)
        {
            Debug.LogWarning("GameServer: no compatible shader found for " + materialName + ".");
            return null;
        }

        material = new Material(shader)
        {
            name = materialName,
            color = color
        };
        return material;
    }

    private void CreateSmileyFace(GameObject view)
    {
        _smileyFaceMaterial = GetOrCreateSolidMaterial(
            ref _smileyFaceMaterial,
            "Enemy_FacialDetailMaterial",
            new Color(0.055f, 0.045f, 0.02f));
        if (_smileyFaceMaterial == null)
        {
            return;
        }

        // Features sit on the sphere's local +Z hemisphere and inherit only server rotation.
        CreateSmileyDot(view.transform, "LeftEye", new Vector3(-0.14f, 0.12f, 0.463f), 0.075f);
        CreateSmileyDot(view.transform, "RightEye", new Vector3(0.14f, 0.12f, 0.463f), 0.075f);

        // Seven small dots make a curved smile that follows the sphere as it rotates.
        for (int i = 0; i < 7; i++)
        {
            float t = i / 6f;
            float x = Mathf.Lerp(-0.17f, 0.17f, t);
            float y = -0.105f + 0.085f * Mathf.Pow(Mathf.Abs(x) / 0.17f, 2f);
            float z = Mathf.Sqrt(Mathf.Max(0.01f, 0.25f - x * x - y * y)) + 0.004f;
            CreateSmileyDot(
                view.transform,
                "Smile" + (i + 1),
                new Vector3(x, y, z),
                0.043f);
        }
    }

    private void CreateSmileyDot(Transform parent, string dotName, Vector3 localPosition, float diameter)
    {
        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = dotName;
        dot.transform.SetParent(parent, false);
        dot.transform.localPosition = localPosition;
        dot.transform.localScale = Vector3.one * diameter;

        Collider collider = dot.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        Renderer renderer = dot.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = _smileyFaceMaterial;
        }
    }

    private void CreateDogAppearance(GameObject view)
    {
        Material dark = GetOrCreateSolidMaterial(
            ref _smileyFaceMaterial,
            "Enemy_FacialDetailMaterial",
            new Color(0.055f, 0.035f, 0.02f));
        Material muzzle = GetOrCreateSolidMaterial(
            ref _dogMuzzleMaterial,
            "Enemy_DogMuzzleMaterial",
            new Color(0.88f, 0.66f, 0.40f));
        if (dark == null || muzzle == null)
        {
            return;
        }

        // The root cube is 2 x 2 x 2 metres. Every feature is a child in the
        // dog's local coordinates: +Z is the front, -Z is the tail/back.
        CreateDogPart(view.transform, "DogEarLeft", PrimitiveType.Sphere,
            new Vector3(-0.32f, 0.34f, 0.50f), new Vector3(0.24f, 0.34f, 0.14f), dark);
        CreateDogPart(view.transform, "DogEarRight", PrimitiveType.Sphere,
            new Vector3(0.32f, 0.34f, 0.50f), new Vector3(0.24f, 0.34f, 0.14f), dark);

        CreateDogPart(view.transform, "DogEyeLeft", PrimitiveType.Sphere,
            new Vector3(-0.22f, 0.15f, 0.515f), new Vector3(0.13f, 0.13f, 0.08f), dark);
        CreateDogPart(view.transform, "DogEyeRight", PrimitiveType.Sphere,
            new Vector3(0.22f, 0.15f, 0.515f), new Vector3(0.13f, 0.13f, 0.08f), dark);

        CreateDogPart(view.transform, "DogMuzzle", PrimitiveType.Sphere,
            new Vector3(0f, -0.04f, 0.515f), new Vector3(0.42f, 0.25f, 0.13f), muzzle);
        CreateDogPart(view.transform, "DogNose", PrimitiveType.Sphere,
            new Vector3(0f, -0.025f, 0.58f), new Vector3(0.13f, 0.105f, 0.07f), dark);
        CreateDogPart(view.transform, "DogMouth", PrimitiveType.Cube,
            new Vector3(0f, -0.19f, 0.53f), new Vector3(0.20f, 0.035f, 0.035f), dark);

        // Two short paws on each side hint at four legs without exceeding the simple box collider.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int alongBody = -1; alongBody <= 1; alongBody += 2)
            {
                CreateDogPart(view.transform,
                    "DogPaw_" + side + "_" + alongBody,
                    PrimitiveType.Sphere,
                    new Vector3(side * 0.515f, -0.30f, alongBody * 0.25f),
                    new Vector3(0.22f, 0.30f, 0.22f),
                    dark);
            }
        }

        GameObject tail = CreateDogPart(view.transform, "DogTail", PrimitiveType.Capsule,
            new Vector3(0f, 0.17f, -0.55f), new Vector3(0.14f, 0.42f, 0.14f), dark);
        if (tail != null)
        {
            tail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }

    private GameObject CreateDogPart(
        Transform parent,
        string partName,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 worldSize,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        // The parent dog is scaled to two metres; divide child dimensions by that scale
        // to keep the decorative parts at the intended world sizes.
        part.transform.localScale = worldSize / 2f;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
        return part;
    }

    private void ApplyOtherPlayerCollisionViews(WorldPlayerData[] players)
    {
        HashSet<string> visiblePlayerIDs = new HashSet<string>();
        if (players != null)
        {
            foreach (WorldPlayerData player in players)
            {
                if (player == null || string.IsNullOrWhiteSpace(player.playerId) ||
                    player.playerId == _authenticatedUsername || player.position == null)
                {
                    continue;
                }

                visiblePlayerIDs.Add(player.playerId);
                if (!_otherPlayerCollisionViews.TryGetValue(player.playerId, out GameObject view) ||
                    view == null)
                {
                    view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    view.name = "Player_" + player.playerId;
                    Renderer renderer = view.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                        renderer.sharedMaterial = GetOrCreateSolidMaterial(
                            ref _otherPlayerMaterial,
                            "OtherPlayerMaterial",
                            new Color(0.62f, 0.73f, 0.88f));
                    }

                    CapsuleCollider capsule = view.GetComponent<CapsuleCollider>();
                    if (capsule == null)
                    {
                        capsule = view.AddComponent<CapsuleCollider>();
                    }
                    capsule.direction = 1;
                    capsule.center = Vector3.zero;
                    capsule.radius = 0.5f;
                    capsule.height = 2f;
                    capsule.isTrigger = false;
                    _otherPlayerCollisionViews[player.playerId] = view;
                }

                float radius = player.collider != null && player.collider.radius > 0f
                    ? player.collider.radius
                    : 0.25f;
                float height = player.collider != null && player.collider.height > 0f
                    ? player.collider.height
                    : 2f;
                view.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                view.transform.position = new Vector3(
                    player.position.x,
                    player.position.y + height * 0.5f,
                    player.position.z);
                IgnoreGroundItemCollisionsForActor(view.GetComponent<Collider>());

                if (player.rotation != null)
                {
                    Quaternion rotation = new Quaternion(
                        player.rotation.x, player.rotation.y, player.rotation.z, player.rotation.w);
                    if (rotation.x * rotation.x + rotation.y * rotation.y +
                        rotation.z * rotation.z + rotation.w * rotation.w > 0.0001f)
                    {
                        rotation = rotation.normalized;
                        view.transform.rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
                    }
                }

                string playerName = player.playerId;
                Transform labelTransform = view.transform.Find("NameLabel");
                TextMesh playerLabel = labelTransform != null
                    ? labelTransform.GetComponent<TextMesh>()
                    : null;
                if (playerLabel == null)
                {
                    playerLabel = CreateWorldNameLabel(view, playerName, height * 0.5f + 0.45f);
                }
                WorldTargetable targetable = view.GetComponent<WorldTargetable>();
                if (targetable == null)
                {
                    targetable = view.AddComponent<WorldTargetable>();
                }
                targetable.Configure("player", player.playerId, playerName, playerLabel);
            }
        }

        List<string> stalePlayerIDs = new List<string>();
        foreach (KeyValuePair<string, GameObject> pair in _otherPlayerCollisionViews)
        {
            if (!visiblePlayerIDs.Contains(pair.Key))
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
                stalePlayerIDs.Add(pair.Key);
            }
        }

        foreach (string staleID in stalePlayerIDs)
        {
            _otherPlayerCollisionViews.Remove(staleID);
        }
    }

    private static void CreateMonsterNameLabel(GameObject view, WorldMonsterData monster)
    {
        TextMesh label = CreateWorldNameLabel(view, monster.name, 1.45f);
        WorldTargetable targetable = view.GetComponent<WorldTargetable>();
        if (targetable == null)
        {
            targetable = view.AddComponent<WorldTargetable>();
        }
        targetable.Configure("monster", monster.id, monster.name, label);
    }

    private static TextMesh CreateWorldNameLabel(GameObject view, string displayName, float worldOffset)
    {
        GameObject labelObject = new GameObject("NameLabel");
        labelObject.transform.SetParent(view.transform, false);

        Vector3 parentScale = view.transform.lossyScale;
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(parentScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(parentScale.y));
        float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(parentScale.z));
        labelObject.transform.localPosition = new Vector3(0f, worldOffset / scaleY, 0f);
        labelObject.transform.localScale = new Vector3(1f / scaleX, 1f / scaleY, 1f / scaleZ);

        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = displayName;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 42;
        label.characterSize = 0.1f;
        label.color = Color.white;
        label.richText = false;
        return label;
    }

    private static bool IsSoulshotItem(WorldGroundItemData item)
    {
        if (item == null) return false;
        string template = item.itemTemplate ?? string.Empty;
        string itemName = item.name ?? string.Empty;
        return template.IndexOf("soulshot", StringComparison.OrdinalIgnoreCase) >= 0 ||
               itemName.IndexOf("soulshot", StringComparison.OrdinalIgnoreCase) >= 0 ||
               itemName.IndexOf("соулшот", StringComparison.OrdinalIgnoreCase) >= 0 ||
               itemName.IndexOf("соушот", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ApplyGroundItemViews(WorldGroundItemData[] items)
    {
        HashSet<string> visibleItemIDs = new HashSet<string>();
        foreach (WorldGroundItemData item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id) || item.position == null) continue;
            visibleItemIDs.Add(item.id);
            bool isSoulshot = IsSoulshotItem(item);
            bool isNewView = !_groundItemViews.TryGetValue(item.id, out GameObject view) || view == null;

            if (isNewView)
            {
                if (isSoulshot)
                {
                    view = new GameObject("GroundItem_" + item.id);

                    // Match the collision to the visible sideways capsule. The old large
                    // sphere extended well below ground, so it did not provide reliable
                    // contact where the actual item is visible.
                    CapsuleCollider itemCollider = view.AddComponent<CapsuleCollider>();
                    itemCollider.direction = 1; // local Y; parent rotation turns it horizontal
                    itemCollider.radius = SoulshotVisual.CollisionRadius;
                    itemCollider.height = SoulshotVisual.CollisionHeight;
                    itemCollider.center = Vector3.zero;
                    itemCollider.isTrigger = false;

                    view.AddComponent<SoulshotVisual>().Configure(item.itemTemplate);
                }
                else
                {
                    view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Renderer renderer = view.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = GetOrCreateSolidMaterial(
                            ref _groundItemMaterial, "GroundItemMaterial", new Color(0.86f, 0.68f, 0.18f));
                    }
                }
                view.name = "GroundItem_" + item.id;
                _groundItemViews[item.id] = view;
                ConfigureGroundItemCollision(view.GetComponent<Collider>());
            }

            const float itemSize = 0.35f;
            // The server heightmap can differ slightly from the client landscape.
            // Always ground soulshots on the client's actual Terrain surface; never
            // let the server Y coordinate pull a visible drop below that surface.
            float baseY = item.position.y;
            float localGroundY = 0f;
            bool hasClientGround = isSoulshot &&
                TryGetTerrainSurfaceY(item.position.x, item.position.z, out localGroundY);
            if (hasClientGround)
            {
                baseY = localGroundY;
            }

            Vector3 restingPosition;
            if (isSoulshot)
            {
                view.transform.localScale = Vector3.one;
                // Put the capsule's bottom at the terrain surface plus a small safety clearance.
                restingPosition = new Vector3(
                    item.position.x,
                    baseY + SoulshotVisual.GroundCenterOffset,
                    item.position.z);
                view.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else
            {
                view.transform.localScale = Vector3.one * itemSize;
                restingPosition = new Vector3(item.position.x, baseY + itemSize * 0.5f, item.position.z);
                view.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            }

            // Ground drops appear already resting on the client landscape. Do not animate
            // them from the server's Y coordinate: even a small heightmap mismatch would
            // make the item visibly start below ground or sink during the animation.
            GroundItemDropAnimation oldDropAnimation = view.GetComponent<GroundItemDropAnimation>();
            if (oldDropAnimation != null)
            {
                oldDropAnimation.enabled = false;
                Destroy(oldDropAnimation);
            }
            view.transform.position = restingPosition;

            string itemName = string.IsNullOrWhiteSpace(item.name)
                ? (string.IsNullOrWhiteSpace(item.itemTemplate) ? "Предмет" : item.itemTemplate)
                : item.name;
            string displayName = item.quantity > 1 ? itemName + " x" + item.quantity : itemName;

            // Match the monster nameplate style. WorldTargetable keeps this label hidden
            // until the item is hovered or selected, then highlights it in the same yellow.
            Transform labelTransform = view.transform.Find("NameLabel");
            TextMesh itemLabel = labelTransform != null
                ? labelTransform.GetComponent<TextMesh>()
                : null;
            if (itemLabel == null)
            {
                itemLabel = CreateWorldNameLabel(view, displayName, 0.45f);
            }
            else
            {
                itemLabel.text = displayName;
            }

            WorldTargetable targetable = view.GetComponent<WorldTargetable>();
            if (targetable == null) targetable = view.AddComponent<WorldTargetable>();
            targetable.Configure("item", item.id, displayName, itemLabel);
        }

        List<string> staleIDs = new List<string>();
        foreach (KeyValuePair<string, GameObject> pair in _groundItemViews)
        {
            if (!visibleItemIDs.Contains(pair.Key))
            {
                if (pair.Value != null) Destroy(pair.Value);
                staleIDs.Add(pair.Key);
            }
        }
        foreach (string staleID in staleIDs) _groundItemViews.Remove(staleID);
    }

    private void ConfigureGroundItemCollision(Collider itemCollider)
    {
        if (itemCollider == null)
        {
            return;
        }

        // Items are static world drops: preserve terrain contact, but ignore every other
        // collider so a drop never blocks the player, another player, a monster or props.
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (Collider other in colliders)
        {
            if (other == null || other == itemCollider || other is TerrainCollider)
            {
                continue;
            }

            Physics.IgnoreCollision(itemCollider, other, true);
        }
    }

    public void IgnoreGroundItemCollisionsForActor(Collider actorCollider)
    {
        if (actorCollider == null)
        {
            return;
        }

        foreach (GameObject itemView in _groundItemViews.Values)
        {
            if (itemView == null)
            {
                continue;
            }

            Collider itemCollider = itemView.GetComponent<Collider>();
            if (itemCollider != null && itemCollider != actorCollider)
            {
                Physics.IgnoreCollision(itemCollider, actorCollider, true);
            }
        }
    }

    private void RemoveGroundItemView(string itemID)
    {
        if (string.IsNullOrWhiteSpace(itemID))
        {
            return;
        }

        if (_groundItemViews.TryGetValue(itemID, out GameObject view))
        {
            if (view != null)
            {
                Destroy(view);
            }
            _groundItemViews.Remove(itemID);
        }

        if (_cachedPlayerController == null)
        {
            _cachedPlayerController = FindFirstObjectByType<PlayerController>();
        }
        if (_cachedPlayerController != null)
        {
            _cachedPlayerController.CancelGroundItemMovement(itemID);
        }

        if (_selectedTargetType == "item" &&
            string.Equals(_selectedTargetID, itemID, StringComparison.Ordinal))
        {
            ApplySelectedTarget(null);
        }
    }

    private bool TryGetTerrainSurfaceY(float worldX, float worldZ, out float surfaceY)
    {
        surfaceY = 0f;

        if (_worldTerrain == null || !TerrainCoversPosition(_worldTerrain, worldX, worldZ))
        {
            _worldTerrain = null;
            Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (Terrain candidate in terrains)
            {
                if (TerrainCoversPosition(candidate, worldX, worldZ))
                {
                    _worldTerrain = candidate;
                    break;
                }
            }
        }

        if (_worldTerrain == null || _worldTerrain.terrainData == null)
        {
            return false;
        }

        TerrainCollider terrainCollider = _worldTerrain.GetComponent<TerrainCollider>();
        if (terrainCollider != null && terrainCollider.enabled)
        {
            Bounds bounds = terrainCollider.bounds;
            Ray ray = new Ray(new Vector3(worldX, bounds.max.y + 10f, worldZ), Vector3.down);
            if (terrainCollider.Raycast(ray, out RaycastHit hit, bounds.size.y + 20f))
            {
                surfaceY = hit.point.y;
                return true;
            }
        }

        // Fallback for scenes where TerrainCollider is missing or disabled.
        TerrainData data = _worldTerrain.terrainData;
        Vector3 origin = _worldTerrain.transform.position;
        Vector3 scale = _worldTerrain.transform.lossyScale;
        float width = data.size.x * Mathf.Abs(scale.x);
        float length = data.size.z * Mathf.Abs(scale.z);
        if (width <= 0f || length <= 0f) return false;

        float normalizedX = (worldX - origin.x) / width;
        float normalizedZ = (worldZ - origin.z) / length;
        if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
            return false;

        surfaceY = origin.y +
            data.GetInterpolatedHeight(normalizedX, normalizedZ) * Mathf.Abs(scale.y);
        return true;
    }

    private static bool TerrainCoversPosition(Terrain terrain, float worldX, float worldZ)
    {
        if (terrain == null || terrain.terrainData == null) return false;
        Vector3 origin = terrain.transform.position;
        Vector3 scale = terrain.transform.lossyScale;
        float width = terrain.terrainData.size.x * Mathf.Abs(scale.x);
        float length = terrain.terrainData.size.z * Mathf.Abs(scale.z);
        return worldX >= origin.x && worldX <= origin.x + width &&
            worldZ >= origin.z && worldZ <= origin.z + length;
    }

    private void LateUpdate()
    {
        Camera currentCamera = Camera.main;
        if (currentCamera == null)
        {
            return;
        }

        foreach (KeyValuePair<string, GameObject> pair in _monsterViews)
        {
            GameObject view = pair.Value;
            if (view == null)
            {
                continue;
            }

            // Only the nameplate faces the camera. Monster models and the smiley face
            // remain fixed to the server-provided quaternion.
            Transform label = view.transform.Find("NameLabel");
            if (label == null)
            {
                continue;
            }

            Vector3 directionToCamera = currentCamera.transform.position - label.position;
            if (directionToCamera.sqrMagnitude > 0.0001f)
            {
                // TextMesh's visible front is opposite to the default billboard face in this
                // setup; the half-turn keeps the text readable instead of mirrored.
                label.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up) *
                    Quaternion.Euler(0f, 180f, 0f);
            }
        }
    }

    private void ClearWorldViews()
    {
        foreach (GameObject view in _monsterViews.Values)
        {
            if (view != null)
            {
                Destroy(view);
            }
        }
        _monsterViews.Clear();
        _monsterTemplateIds.Clear();

        foreach (GameObject view in _otherPlayerCollisionViews.Values)
        {
            if (view != null)
            {
                Destroy(view);
            }
        }
        _otherPlayerCollisionViews.Clear();

        foreach (GameObject view in _groundItemViews.Values)
        {
            if (view != null)
            {
                Destroy(view);
            }
        }
        _groundItemViews.Clear();
        _selectedTargetType = string.Empty;
        _selectedTargetID = string.Empty;
        _hasPendingTargetSelection = false;
        _lastSnapshotSummary = string.Empty;
    }

    public bool IsTargetSelected(WorldTargetable target)
    {
        return target != null &&
            !string.IsNullOrEmpty(_selectedTargetID) &&
            string.Equals(_selectedTargetType, target.TargetType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_selectedTargetID, target.TargetID, StringComparison.Ordinal);
    }

    /// <summary>Queues one server-authoritative melee attack against the active command target.</summary>
    public bool RequestAttack(int requestId, string targetType, string targetId)
    {
        if (!IsReadyForPlayerState || _writer == null ||
            string.IsNullOrWhiteSpace(targetType) ||
            string.IsNullOrWhiteSpace(targetId))
        {
            return false;
        }

        // The server resolves this reference against live entities and validates range,
        // cooldown, and damage. Changing the UI selection cannot retarget this command.
        string json = JsonUtility.ToJson(new AttackRequest
        {
            type = "attack",
            targetType = targetType,
            targetId = targetId,
            requestId = requestId
        });

        SemaphoreSlim signal;
        bool signalSender;
        lock (_outgoingLock)
        {
            // PlayerController permits only one outstanding request. Never overwrite
            // an attack command that is still waiting to be written to the TCP stream.
            if (!string.IsNullOrEmpty(_latestOutgoingAttack))
            {
                return false;
            }

            signalSender = string.IsNullOrEmpty(_latestOutgoingPlayerState) &&
                string.IsNullOrEmpty(_latestOutgoingWorldInterest) &&
                string.IsNullOrEmpty(_latestOutgoingTarget) &&
                string.IsNullOrEmpty(_latestOutgoingAttack);
            _latestOutgoingAttack = json;
            signal = _outgoingSignal;
        }

        if (signalSender)
        {
            signal.Release();
        }

        Debug.Log("GameServer: attack command queued for " + targetType + ":" + targetId + "; requestId=" + requestId);
        return true;
    }

    private void HandleAttackResult(ServerMessage message)
    {
        if (message.attackSpeed > 0f &&
            !float.IsNaN(message.attackSpeed) &&
            !float.IsInfinity(message.attackSpeed))
        {
            _serverAttackSpeed = Mathf.Clamp(message.attackSpeed, 0.1f, 10f);
            _hasServerAttackSpeed = true;
        }

        if (_cachedPlayerController == null)
        {
            _cachedPlayerController = FindFirstObjectByType<PlayerController>();
        }

        if (_cachedPlayerController != null)
        {
            _cachedPlayerController.HandleAttackResult(
                message.requestId,
                message.target != null ? message.target.type : string.Empty,
                message.target != null ? message.target.id : string.Empty,
                message.success,
                message.message,
                message.attackSpeed,
                message.retryAfter);
        }

        if (message.success)
        {
            Debug.Log("GameServer: attack accepted; target=" +
                (message.target != null ? message.target.type + ":" + message.target.id : "unknown") +
                ", requestId=" + message.requestId);
        }
        else
        {
            Debug.LogWarning("GameServer: attack rejected; reason=" + message.message +
                ", requestId=" + message.requestId);
        }
    }

    public void CancelGroundItemPickupIntent()
    {
        if (!string.Equals(_selectedTargetType, "item", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplySelectedTarget(null);
        _hasPendingTargetSelection = true;
        if (!IsReadyForPlayerState || _writer == null)
        {
            _hasPendingTargetSelection = false;
            return;
        }

        string json = JsonUtility.ToJson(new TargetRequest
        {
            type = "set_target",
            targetType = string.Empty,
            targetId = string.Empty
        });

        SemaphoreSlim signal;
        bool signalSender;
        lock (_outgoingLock)
        {
            signalSender = string.IsNullOrEmpty(_latestOutgoingPlayerState) &&
                string.IsNullOrEmpty(_latestOutgoingWorldInterest) &&
                string.IsNullOrEmpty(_latestOutgoingTarget);
            _latestOutgoingTarget = json;
            signal = _outgoingSignal;
        }

        if (signalSender)
        {
            signal.Release();
        }
    }

    public void SelectTarget(WorldTargetable target)
    {
        if (target == null)
        {
            return;
        }

        WorldTargetData selected = new WorldTargetData
        {
            type = target.TargetType,
            id = target.TargetID,
            name = target.DisplayName
        };
        ApplySelectedTarget(selected);
        _hasPendingTargetSelection = true;

        if (!IsReadyForPlayerState || _writer == null ||
            string.IsNullOrWhiteSpace(selected.type) || string.IsNullOrWhiteSpace(selected.id))
        {
            _hasPendingTargetSelection = false;
            return;
        }

        string json = JsonUtility.ToJson(new TargetRequest
        {
            type = "set_target",
            targetType = selected.type,
            targetId = selected.id
        });

        SemaphoreSlim signal;
        bool signalSender;
        lock (_outgoingLock)
        {
            signalSender = string.IsNullOrEmpty(_latestOutgoingPlayerState) &&
                string.IsNullOrEmpty(_latestOutgoingWorldInterest) &&
                string.IsNullOrEmpty(_latestOutgoingTarget);
            _latestOutgoingTarget = json;
            signal = _outgoingSignal;
        }
        if (signalSender)
        {
            signal.Release();
        }
    }

    private void ApplySelectedTarget(WorldTargetData target)
    {
        _selectedTargetType = target == null ? string.Empty : target.type ?? string.Empty;
        _selectedTargetID = target == null ? string.Empty : target.id ?? string.Empty;
        RefreshTargetSelection();
    }

    private void RefreshTargetSelection()
    {
        foreach (GameObject view in _monsterViews.Values)
        {
            UpdateTargetSelectionOnView(view);
        }
        foreach (GameObject view in _otherPlayerCollisionViews.Values)
        {
            UpdateTargetSelectionOnView(view);
        }
        foreach (GameObject view in _groundItemViews.Values)
        {
            UpdateTargetSelectionOnView(view);
        }
    }

    private void UpdateTargetSelectionOnView(GameObject view)
    {
        if (view == null)
        {
            return;
        }
        WorldTargetable targetable = view.GetComponent<WorldTargetable>();
        if (targetable != null)
        {
            targetable.SetSelected(
                targetable.TargetType == _selectedTargetType &&
                targetable.TargetID == _selectedTargetID &&
                !string.IsNullOrEmpty(_selectedTargetID));
        }
    }

    public void SendPlayerState(
        Vector3 position,
        Quaternion rotation,
        bool hasMoveTarget,
        Vector3 moveTargetPosition)
    {
        if (!IsReadyForPlayerState)
        {
            return;
        }

        StreamWriter writer = _writer;
        if (writer == null)
        {
            return;
        }

        PlayerStateRequest request = new PlayerStateRequest
        {
            type = "player_state",
            x = position.x,
            y = position.y,
            z = position.z,
            hasMoveTarget = hasMoveTarget,
            moveTargetX = moveTargetPosition.x,
            moveTargetZ = moveTargetPosition.z,
            rotationX = rotation.x,
            rotationY = rotation.y,
            rotationZ = rotation.z,
            rotationW = rotation.w
        };
        string json = JsonUtility.ToJson(request);

        SemaphoreSlim signal;
        bool signalSender;
        lock (_outgoingLock)
        {
            signalSender = string.IsNullOrEmpty(_latestOutgoingPlayerState) &&
                string.IsNullOrEmpty(_latestOutgoingWorldInterest) &&
                string.IsNullOrEmpty(_latestOutgoingTarget);
            _latestOutgoingPlayerState = json;
            signal = _outgoingSignal;
        }

        if (signalSender)
        {
            signal.Release();
        }
    }

    private async Task SendQueuedMessagesAsync(
        StreamWriter writer,
        SemaphoreSlim signal,
        int operationId)
    {
        try
        {
            while (operationId == _operationId)
            {
                await signal.WaitAsync().ConfigureAwait(false);
                if (operationId != _operationId)
                {
                    return;
                }

                while (operationId == _operationId)
                {
                    string playerStateJson;
                    string worldInterestJson;
                    string targetJson;
                    string attackJson;
                    lock (_outgoingLock)
                    {
                        playerStateJson = _latestOutgoingPlayerState;
                        worldInterestJson = _latestOutgoingWorldInterest;
                        targetJson = _latestOutgoingTarget;
                        attackJson = _latestOutgoingAttack;
                        _latestOutgoingPlayerState = null;
                        _latestOutgoingWorldInterest = null;
                        _latestOutgoingTarget = null;
                        _latestOutgoingAttack = null;
                    }

                    if (string.IsNullOrEmpty(playerStateJson) &&
                        string.IsNullOrEmpty(worldInterestJson) &&
                        string.IsNullOrEmpty(targetJson) &&
                        string.IsNullOrEmpty(attackJson))
                    {
                        break;
                    }

                    if (!string.IsNullOrEmpty(playerStateJson))
                    {
                        await writer.WriteLineAsync(playerStateJson).ConfigureAwait(false);
                    }
                    if (!string.IsNullOrEmpty(worldInterestJson))
                    {
                        await writer.WriteLineAsync(worldInterestJson).ConfigureAwait(false);
                    }
                    if (!string.IsNullOrEmpty(targetJson))
                    {
                        await writer.WriteLineAsync(targetJson).ConfigureAwait(false);
                    }
                    if (!string.IsNullOrEmpty(attackJson))
                    {
                        await writer.WriteLineAsync(attackJson).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (operationId == _operationId)
            {
                QueueIncomingMessage(operationId, JsonUtility.ToJson(new ServerMessage
                {
                    type = "transport_error",
                    message = "Send failed: " + exception.Message
                }));
            }
        }
    }

    private void DisconnectFromServer()
    {
        DisconnectFromServer(true);
    }

    private void DisconnectFromServer(bool updateStatus)
    {
        ++_operationId;
        CloseConnectionResources();
        ClearWorldViews();
        _cachedPlayerController = null;
        _hasSentWorldInterest = false;
        _isConnecting = false;
        _isConnected = false;
        _isAuthenticating = false;
        _isAuthenticated = false;
        _authenticatedUsername = string.Empty;
        _hasPendingSpawn = false;
        _hasAppliedSpawn = false;
        _serverAttackSpeed = 1f;
        _hasServerAttackSpeed = false;
        _playerInventory = Array.Empty<WorldInventoryStack>();
        while (_incomingMessages.TryDequeue(out _)) { }

        SemaphoreSlim signal;
        lock (_outgoingLock)
        {
            _latestOutgoingPlayerState = null;
            _latestOutgoingWorldInterest = null;
            _latestOutgoingTarget = null;
            _latestOutgoingAttack = null;
            signal = _outgoingSignal;
        }
        try { signal.Release(); } catch (SemaphoreFullException) { }

        if (updateStatus)
        {
            _status = "Отключено";
            Debug.Log("GameServer: disconnected");
        }
    }

    private void CloseConnectionResources()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }

        _writer = null;
        _reader = null;
        _stream = null;
        _client = null;
    }

    private static GUIStyle TitleStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 12
        };
    }

    private static GUIStyle StatusStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            fontSize = 11
        };
    }
}
