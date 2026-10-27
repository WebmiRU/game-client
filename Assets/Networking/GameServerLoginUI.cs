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
        public float x;
        public float y;
        public float z;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW;
        public bool positionIsRoot;
        public float visibilityRange;
        public WorldPlayerData[] players;
        public WorldMonsterData[] monsters;
        public WorldGroundItemData[] items;
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
    private sealed class WorldPlayerData
    {
        public string playerId;
        public WorldVector3 position;
        public WorldQuaternion rotation;
    }

    [Serializable]
    private sealed class WorldMonsterData
    {
        public string id;
        public string templateId;
        public string name;
        public WorldVector3 position;
        public WorldQuaternion rotation;
        public int health;
        public int maxHealth;
        public bool alive;
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
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW;
    }

    [Serializable]
    private sealed class WorldInterestRequest
    {
        public string type = "world_interest";
        public float x;
        public float y;
        public float z;
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
    private Material _monsterMaterial;

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
    private SemaphoreSlim _outgoingSignal = new SemaphoreSlim(0);

    public static GameServerLoginUI Instance { get; private set; }

    public bool IsReadyForPlayerState
    {
        get { return _isConnected && _isAuthenticated && _hasAppliedSpawn; }
    }

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
        if (_monsterMaterial != null)
        {
            Destroy(_monsterMaterial);
            _monsterMaterial = null;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnGUI()
    {
        float panelWidth = Mathf.Min(300f, Screen.width - 24f);
        float panelHeight = _isConnecting ? 92f : (_isConnected && _isAuthenticated ? 146f : 126f);
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

            case "world_snapshot":
                if (!message.success)
                {
                    Debug.LogWarning("GameServer: world interest request rejected; reason=" + message.message);
                    return;
                }

                ApplyWorldSnapshot(message);
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

        SendWorldInterest(position);
        _lastWorldInterestPosition = position;
        _hasSentWorldInterest = true;
        _nextWorldInterestTime = now + Mathf.Max(0.25f, worldInterestUpdateInterval);
    }

    private void SendWorldInterest(Vector3 position)
    {
        if (!IsReadyForPlayerState || _writer == null)
        {
            return;
        }

        WorldInterestRequest request = new WorldInterestRequest
        {
            type = "world_interest",
            x = position.x,
            y = position.y,
            z = position.z,
            visibilityRange = Mathf.Clamp(visibilityRange, 50f, 500f)
        };
        string json = JsonUtility.ToJson(request);

        SemaphoreSlim signal;
        bool signalSender;
        lock (_outgoingLock)
        {
            signalSender = string.IsNullOrEmpty(_latestOutgoingPlayerState) &&
                string.IsNullOrEmpty(_latestOutgoingWorldInterest);
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
        WorldMonsterData[] monsters = message.monsters ?? Array.Empty<WorldMonsterData>();
        HashSet<string> visibleMonsterIDs = new HashSet<string>();

        foreach (WorldMonsterData monster in monsters)
        {
            if (monster == null || string.IsNullOrWhiteSpace(monster.id) ||
                monster.position == null || !monster.alive)
            {
                continue;
            }

            visibleMonsterIDs.Add(monster.id);
            if (!_monsterViews.TryGetValue(monster.id, out GameObject view) || view == null)
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                view.name = "ServerMonster_" + monster.id;
                Collider collider = view.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                Renderer renderer = view.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (_monsterMaterial == null)
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (shader == null)
                        {
                            shader = Shader.Find("Standard");
                        }
                        if (shader != null)
                        {
                            _monsterMaterial = new Material(shader)
                            {
                                color = new Color(0.78f, 0.16f, 0.12f)
                            };
                        }
                    }

                    if (_monsterMaterial != null)
                    {
                        renderer.sharedMaterial = _monsterMaterial;
                    }
                }

                _monsterViews[monster.id] = view;
            }

            // Test monster positions are ground-surface coordinates; a Unity capsule's
            // pivot is at its center, so raise it by one unit to make it stand on the floor.
            view.transform.position = new Vector3(
                monster.position.x,
                monster.position.y + 1f,
                monster.position.z);
            if (monster.rotation != null)
            {
                Quaternion rotation = new Quaternion(
                    monster.rotation.x, monster.rotation.y,
                    monster.rotation.z, monster.rotation.w);
                view.transform.rotation = rotation.sqrMagnitude > 0.0001f
                    ? Quaternion.Normalize(rotation)
                    : Quaternion.identity;
            }
        }

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
            _monsterViews.Remove(staleID);
        }

        int playerCount = message.players == null ? 0 : message.players.Length;
        int itemCount = message.items == null ? 0 : message.items.Length;
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
        _lastSnapshotSummary = string.Empty;
    }

    public void SendPlayerState(Vector3 position, Quaternion rotation)
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
                string.IsNullOrEmpty(_latestOutgoingWorldInterest);
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
                    lock (_outgoingLock)
                    {
                        playerStateJson = _latestOutgoingPlayerState;
                        worldInterestJson = _latestOutgoingWorldInterest;
                        _latestOutgoingPlayerState = null;
                        _latestOutgoingWorldInterest = null;
                    }

                    if (string.IsNullOrEmpty(playerStateJson) &&
                        string.IsNullOrEmpty(worldInterestJson))
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
        while (_incomingMessages.TryDequeue(out _)) { }

        SemaphoreSlim signal;
        lock (_outgoingLock)
        {
            _latestOutgoingPlayerState = null;
            _latestOutgoingWorldInterest = null;
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
