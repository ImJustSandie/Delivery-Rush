using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gestiona la interfaz de usuario de la escena Lobby.
/// Muestra el numero de jugadores conectados, la IP de conexion y el boton de inicio de juego (exclusivo para el Host/Servidor).
/// </summary>
public class LobbyUIHandler : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("Texto TMP para mostrar el numero de jugadores conectados.")]
    [SerializeField] private TMP_Text playerCountText;

    [Tooltip("Texto TMP para mostrar la IP del servidor/host al que se está conectado.")]
    [SerializeField] private TMP_Text ipAddressText;

    [Tooltip("Formato del texto de la IP. {0} representa la IP y {1} el puerto.")]
    [SerializeField] private string ipTextFormat = "{0}";

    [Tooltip("Boton para iniciar la partida.")]
    [SerializeField] private Button startGameButton;

    [Tooltip("Boton para regresar a la escena de conexion.")]
    [SerializeField] private Button backButton;

    [Tooltip("Botón del panel de IP: al pulsarlo copia la IP al portapapeles. Si se deja vacío se busca el Button en el panel de ipAddressText.")]
    [SerializeField] private Button ipCopyButton;

    [Header("Scene Configuration")]
    [Tooltip("Nombre de la escena de juego principal a la que se cambiara al presionar Jugar.")]
    [SerializeField] private string mainSceneName = "MainScene";

    [Tooltip("Nombre de la escena de conexion a la que se cambiara al presionar Volver.")]
    [SerializeField] private string connectionSceneName = "ConnectionScene";

    private void Start()
    {
        EnsureCanvasWorldCamera();

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LobbyUIHandler] NetworkManager.Singleton no fue encontrado.");
            return;
        }

        // Suscribirse a eventos de conexion/desconexion para actualizar la UI
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Configurar la UI inicial
        UpdatePlayerCountUI();
        UpdateConnectionIPUI();
        ConfigureStartButton();
        ConfigureBackButton();
        ConfigureIpCopyButton();
    }

    /// <summary>
    /// Asegura que el Canvas del Lobby tenga asignada la Main Camera de la escena como renderCamera si esta en Screen Space - Camera.
    /// </summary>
    private void EnsureCanvasWorldCamera()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = GetComponentInChildren<Canvas>();

        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
        {
            if (Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[LobbyUIHandler] Cliente conectado con ID: {clientId}");
        UpdatePlayerCountUI();
        UpdateConnectionIPUI();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[LobbyUIHandler] Cliente desconectado con ID: {clientId}");
        UpdatePlayerCountUI();
        UpdateConnectionIPUI();

        // Si el cliente desconectado es el cliente local (o el host cerró la conexión)
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId && !NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[LobbyUIHandler] Se perdió la conexión con el servidor/host. Regresando a la escena de conexión...");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != connectionSceneName)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(connectionSceneName);
            }
        }
    }

    /// <summary>
    /// Actualiza el texto de conteo de jugadores conectados.
    /// </summary>
    private void UpdatePlayerCountUI()
    {
        if (playerCountText == null) return;

        int count = 0;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        }

        playerCountText.text = count.ToString();
    }

    /// <summary>
    /// Actualiza el texto TMP con la dirección IP local activa o la del Host conectado.
    /// </summary>
    private void UpdateConnectionIPUI()
    {
        if (ipAddressText == null) return;

        string address = GetLocalIPAddress();
        ushort port = 7777;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                port = transport.ConnectionData.Port;

                // Si es Cliente (no Server/Host) y tiene una IP asignada distinta de 0.0.0.0 o 127.0.0.1, mostramos la IP a la que se conectó
                if (!NetworkManager.Singleton.IsServer)
                {
                    string targetAddress = transport.ConnectionData.Address;
                    if (!string.IsNullOrEmpty(targetAddress) && targetAddress != "0.0.0.0" && targetAddress != "127.0.0.1")
                    {
                        address = targetAddress;
                    }
                }
            }
        }

        try
        {
            ipAddressText.text = string.Format(ipTextFormat, address, port);
        }
        catch
        {
            ipAddressText.text = address;
        }
    }

    /// <summary>
    /// Obtiene la dirección IPv4 local activa del dispositivo (Wi-Fi / Red local).
    /// </summary>
    /// <returns>Dirección IPv4 en formato string o 127.0.0.1 en caso de error.</returns>
    public static string GetLocalIPAddress()
    {
        // 1. Método con Socket UDP dummy (muy rápido y efectivo en Android/iOS/Windows para detectar la interfaz de salida activa)
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                {
                    string ipStr = endPoint.Address.ToString();
                    if (!ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254."))
                    {
                        return ipStr;
                    }
                }
            }
        }
        catch
        {
            // Ignorar y pasar al siguiente método si falla (ej. sin conexión externa)
        }

        // 2. Método de escaneo de interfaces de red activas
        try
        {
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.OperationalStatus == OperationalStatus.Up &&
                    item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    IPInterfaceProperties adapterProperties = item.GetIPProperties();
                    foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string addressStr = ip.Address.ToString();
                            if (!addressStr.StartsWith("127.") && !addressStr.StartsWith("169.254."))
                            {
                                return addressStr;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignorar y pasar al fallback DNS
        }

        // 3. Fallback mediante DNS Host Entry
        try
        {
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    string addressStr = ip.ToString();
                    if (!addressStr.StartsWith("127.") && !addressStr.StartsWith("169.254."))
                    {
                        return addressStr;
                    }
                }
            }
        }
        catch
        {
            // Ignorar
        }

        return "127.0.0.1";
    }

    /// <summary>
    /// Configura el boton de inicio de juego segun si el cliente local es Servidor/Host.
    /// </summary>
    private void ConfigureStartButton()
    {
        if (startGameButton == null) return;

        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // El boton solo debe estar visible/activo para el Host / Servidor
        startGameButton.gameObject.SetActive(isServer);

        if (isServer)
        {
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(OnStartGameButtonClicked);
        }
    }

    /// <summary>
    /// Configura el boton de volver a la escena de conexion.
    /// </summary>
    private void ConfigureBackButton()
    {
        if (backButton == null) return;

        backButton.onClick.RemoveAllListeners();
        backButton.onClick.AddListener(OnBackButtonClicked);
    }

    /// <summary>
    /// Configura el boton del panel de IP para copiar la IP al portapapeles.
    /// </summary>
    private void ConfigureIpCopyButton()
    {
        if (ipCopyButton == null && ipAddressText != null)
        {
            GameObject panel = ipAddressText.gameObject;
            ipCopyButton = panel.GetComponent<Button>();
            if (ipCopyButton == null && panel.transform.parent != null)
                ipCopyButton = panel.transform.parent.GetComponent<Button>();
            if (ipCopyButton == null && panel.transform.parent != null)
                ipCopyButton = panel.transform.parent.GetComponentInChildren<Button>(true);
        }

        if (ipCopyButton == null) return;

        ipCopyButton.onClick.RemoveAllListeners();
        ipCopyButton.onClick.AddListener(CopyIpToClipboard);
    }

    /// <summary>
    /// Copia el texto visible de la IP al portapapeles del sistema.
    /// </summary>
    public void CopyIpToClipboard()
    {
        if (ipAddressText == null) return;

        string ip = ipAddressText.text;
        if (string.IsNullOrWhiteSpace(ip))
        {
            Debug.LogWarning("[LobbyUIHandler] No hay IP para copiar (texto vacío).");
            return;
        }

        GUIUtility.systemCopyBuffer = ip.Trim();
        Debug.Log($"[LobbyUIHandler] IP copiada al portapapeles: {ip.Trim()}");
    }

    /// <summary>
    /// Metodo ejecutado al pulsar el boton "Volver".
    /// Cierra la conexion de red y regresa a la escena de conexion.
    /// </summary>
    public void OnBackButtonClicked()
    {
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.DisconnectLocalPlayer();
        }
        else
        {
            Debug.Log("[LobbyUIHandler] Regresando a la escena de conexión...");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(connectionSceneName);
        }
    }


    /// <summary>
    /// Metodo ejecutado al pulsar el boton "Jugar".
    /// </summary>
    public void OnStartGameButtonClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[LobbyUIHandler] Solo el servidor/host puede iniciar la partida.");
            return;
        }

        // Reiniciar puntajes de todos los jugadores para la nueva partida
        foreach (var player in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (player != null) player.ResetScoreServerSide();
        }

        // Marcar el inicio de la partida en el NetworkGameManager para bloquear late-join
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.StartGame();
        }

        // Determinar el nombre de la escena a cargar (usando el NetworkGameManager o la variable local)
        string targetScene = (NetworkGameManager.Instance != null) ? NetworkGameManager.Instance.MainSceneName : mainSceneName;

        Debug.Log($"[LobbyUIHandler] Iniciando partida... Cambiando a escena: {targetScene}");
        NetworkManager.Singleton.SceneManager.LoadScene(targetScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
