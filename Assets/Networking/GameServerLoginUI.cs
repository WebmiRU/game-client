using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Temporary IMGUI login panel for connecting to the Go game server.
/// It starts automatically when Play Mode starts, so no scene wiring is required.
/// </summary>
public sealed class GameServerLoginUI : MonoBehaviour
{
    private const int DefaultPort = 7777;
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
    private string _port = DefaultPort.ToString();
    private string _username = "player1";
    private string _password = string.Empty;
    private string _status = "Не подключено";
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

    private void OnDestroy()
    {
        CloseConnectionResources();
    }

    private void OnGUI()
    {
        float panelWidth = Mathf.Min(460f, Screen.width - 24f);
        float panelHeight = _isAuthenticated ? 250f : (_isConnected ? 355f : 390f);
        Rect panelRect = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            Mathf.Max(12f, (Screen.height - panelHeight) * 0.5f),
            panelWidth,
            panelHeight);

        GUILayout.BeginArea(panelRect, GUI.skin.window);
        GUILayout.BeginVertical();
        GUILayout.Label("Подключение к Game Server", CenteredTitleStyle());
        GUILayout.Space(8f);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !_isConnecting && !_isConnected;

        GUILayout.BeginHorizontal();
        GUILayout.Label("IP / хост", GUILayout.Width(90f));
        _host = GUILayout.TextField(_host);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Порт", GUILayout.Width(90f));
        _port = GUILayout.TextField(_port);
        GUILayout.EndHorizontal();

        GUI.enabled = wasEnabled;
        GUILayout.Space(8f);

        if (!_isConnected)
        {
            if (_isConnecting)
            {
                GUILayout.Label("Устанавливаю TCP-соединение...");
                if (GUILayout.Button("Отменить подключение", GUILayout.Height(30f)))
                {
                    DisconnectFromServer();
                }
            }
            else if (GUILayout.Button("Подключиться", GUILayout.Height(34f)))
            {
                ConnectToServerAsync();
            }
        }
        else if (!_isAuthenticated)
        {
            GUILayout.Label("TCP-соединение установлено.");
            GUILayout.Space(5f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Логин", GUILayout.Width(90f));
            _username = GUILayout.TextField(_username);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Пароль", GUILayout.Width(90f));
            _password = GUILayout.PasswordField(_password, '*');
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !_isAuthenticating;
            if (_isAuthenticating)
            {
                GUILayout.Label("Проверяю логин и пароль...");
            }
            else if (GUILayout.Button("Авторизоваться", GUILayout.Height(34f)))
            {
                AuthenticateAsync();
            }
            GUI.enabled = previousEnabled;

            if (GUILayout.Button("Отключиться"))
            {
                DisconnectFromServer();
            }
        }
        else
        {
            GUILayout.Label("Авторизация успешна", CenteredTitleStyle());
            GUILayout.Label("Вы вошли как: " + _authenticatedUsername);
            GUILayout.Space(10f);

            if (GUILayout.Button("Отключиться", GUILayout.Height(34f)))
            {
                DisconnectFromServer();
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label("Статус: " + _status, WrappedStatusStyle());
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
        if (string.IsNullOrWhiteSpace(host))
        {
            SetConnectionError("Укажи IP-адрес или имя сервера.");
            return;
        }

        if (!int.TryParse(_port, out int port) || port < 1 || port > 65535)
        {
            SetConnectionError("Порт должен быть числом от 1 до 65535.");
            return;
        }

        int operationId = ++_operationId;
        _isConnecting = true;
        _status = "Подключение к " + host + ":" + port + "...";

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
                    // Observe the connection task after timing out.
                }

                throw new TimeoutException("Сервер не ответил за 5 секунд.");
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
            _status = "Подключено к " + host + ":" + port;
            Debug.Log("GameServer: TCP connection established to " + host + ":" + port);
        }
        catch (Exception exception)
        {
            if (operationId == _operationId)
            {
                CloseConnectionResources();
                _isConnected = false;
                _isAuthenticated = false;
                _status = "Не удалось подключиться: " + exception.Message;
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

        string username = _username.Trim();
        string password = _password;
        if (string.IsNullOrWhiteSpace(username))
        {
            _status = "Введи логин.";
            return;
        }

        int operationId = _operationId;
        StreamReader reader = _reader;
        StreamWriter writer = _writer;
        _isAuthenticating = true;
        _status = "Проверяю учётные данные...";

        try
        {
            AuthRequest request = new AuthRequest
            {
                type = "auth",
                username = username,
                password = password
            };

            string json = JsonUtility.ToJson(request);
            await writer.WriteLineAsync(json);

            Task<string> responseTask = reader.ReadLineAsync();
            Task completed = await Task.WhenAny(
                responseTask,
                Task.Delay(ResponseTimeoutMilliseconds));

            if (completed != responseTask)
            {
                throw new TimeoutException("Сервер не прислал ответ на запрос авторизации за 10 секунд.");
            }

            string responseLine = await responseTask;
            if (operationId != _operationId)
            {
                return;
            }

            if (string.IsNullOrEmpty(responseLine))
            {
                throw new IOException("Сервер закрыл соединение.");
            }

            AuthResponse response = JsonUtility.FromJson<AuthResponse>(responseLine);
            if (response == null || response.type != "auth_result")
            {
                throw new InvalidDataException("Неожиданный ответ сервера на запрос авторизации.");
            }

            if (!response.success)
            {
                string reason = string.IsNullOrWhiteSpace(response.message)
                    ? "invalid_credentials"
                    : response.message;

                _status = "Авторизация не удалась: " + DescribeAuthFailure(reason);
                // Never log the password or the complete request JSON.
                Debug.LogWarning(
                    "GameServer: authentication failed; username=" + username +
                    ", server=" + _host.Trim() + ":" + _port +
                    ", reason=" + reason);
                return;
            }

            _isAuthenticated = true;
            _authenticatedUsername = string.IsNullOrWhiteSpace(response.playerId)
                ? username
                : response.playerId;
            _password = string.Empty;
            _status = "Авторизация успешна. Протокол: " + response.protocolVersion;
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
                    "GameServer: authentication exchange failed; username=" + username +
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
        _password = string.Empty;

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

    private void SetConnectionError(string message)
    {
        _status = message;
        Debug.LogError("GameServer: " + message);
    }

    private static string DescribeAuthFailure(string reason)
    {
        if (reason == "invalid_credentials")
        {
            return "неверный логин или пароль.";
        }

        if (reason == "already_authenticated")
        {
            return "соединение уже авторизовано.";
        }

        return reason + ".";
    }

    private static GUIStyle CenteredTitleStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };
    }

    private static GUIStyle WrappedStatusStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            wordWrap = true
        };
    }
}
