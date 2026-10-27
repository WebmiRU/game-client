using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
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

    private string _host = "127.0.0.1";
    private readonly string _port = DefaultPort.ToString();
    private string _status = "Ожидание запуска...";
    private string _authenticatedUsername = string.Empty;

    private TcpClient _client;
    private NetworkStream _stream;
    private StreamReader _reader;
    private StreamWriter _writer;

    private bool _isConnecting;
    private bool _isConnected;
    private bool _isAuthenticating;
    private bool _isAuthenticated;
    private int _operationId;

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

    private void OnDestroy()
    {
        CloseConnectionResources();
    }

    private void OnGUI()
    {
        float panelWidth = Mathf.Min(300f, Screen.width - 24f);
        float panelHeight = _isConnecting ? 92f : (_isConnected && _isAuthenticated ? 104f : 126f);
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

    private void DisconnectFromServer()
    {
        DisconnectFromServer(true);
    }

    private void DisconnectFromServer(bool updateStatus)
    {
        ++_operationId;
        CloseConnectionResources();
        _isConnecting = false;
        _isConnected = false;
        _isAuthenticating = false;
        _isAuthenticated = false;
        _authenticatedUsername = string.Empty;

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
