using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using TMPro;

public class ConnectionUIHandler : MonoBehaviour
{
    [Header("Scene Configuration")]
    [Tooltip("Nombre de la escena de Lobby a la que el servidor/host cambiará automáticamente al iniciar.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Tooltip("Nombre de la escena de conexión para el cliente.")]
    [SerializeField] private string clientSceneName = "ClientConnectScene";

    [Tooltip("Nombre de la escena de personalización de personaje.")]
    [SerializeField] private string customizationSceneName = "CustomizationScene";

    [Tooltip("Nombre de la escena de tutorial (copia offline de la escena de juego).")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";

    [Header("Network Connection Configuration")]
    [Tooltip("Campo de texto de TMP para ingresar la IP del servidor.")]
    [SerializeField] private TMP_InputField ipInputField;

    [Tooltip("IP por defecto si el campo está vacío.")]
    [SerializeField] private string defaultAddress = "127.0.0.1";

    [Tooltip("Puerto de conexión.")]
    [SerializeField] private ushort port = 7777;

    private void ConfigureHostConnectionData()
    {
        if (NetworkManager.Singleton == null) return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("[ConnectionUIHandler] UnityTransport no fue encontrado en el NetworkManager.");
            return;
        }

        string localIP = LobbyUIHandler.GetLocalIPAddress();
        transport.SetConnectionData(localIP, port, "0.0.0.0");
        Debug.Log($"[ConnectionUIHandler] Host configurado en IP local: {localIP}:{port} (Escuchando en 0.0.0.0)");
    }

    private void ConfigureClientConnectionData()
    {
        if (NetworkManager.Singleton == null) return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("[ConnectionUIHandler] UnityTransport no fue encontrado en el NetworkManager.");
            return;
        }

        string targetIP = defaultAddress;
        if (ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text))
        {
            string rawInput = ipInputField.text.Trim();
            targetIP = RoomCodeUtility.CodeToIP(rawInput, defaultAddress);
        }

        transport.SetConnectionData(targetIP, port);
        Debug.Log($"[ConnectionUIHandler] Cliente configurado para conectar a: {targetIP}:{port}");
    }

    private void Start()
    {
        FixTutorialButtonWiring();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    /// <summary>
    /// Corrige por código el cableado del botón de tutorial.
    /// En ConnectionScene el objeto "ComoJugar_btn" quedó conectado a StartHost
    /// (creación de partida + Lobby) en vez de a OpenTutorialScene.
    /// Como no se pueden modificar escenas, se reasigna aquí en tiempo de ejecución
    /// para que entre directo al tutorial offline sin crear sesión de red.
    /// </summary>
    private void FixTutorialButtonWiring()
    {
        GameObject tutorialButtonGO = GameObject.Find("ComoJugar_btn");
        if (tutorialButtonGO == null) return;

        UnityEngine.UI.Button tutorialButton = tutorialButtonGO.GetComponent<UnityEngine.UI.Button>();
        if (tutorialButton == null)
            tutorialButton = tutorialButtonGO.GetComponentInChildren<UnityEngine.UI.Button>(true);
        if (tutorialButton == null) return;

        tutorialButton.onClick.RemoveAllListeners();
        tutorialButton.onClick.AddListener(OpenTutorialScene);
        Debug.Log("[ConnectionUIHandler] ComoJugar_btn reasignado por código a OpenTutorialScene (tutorial offline).");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Si el cliente desconectado es el cliente local y no es el servidor/host activo
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("[ConnectionUIHandler] Desconectado del servidor. Regresando a la escena de conexión...");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "ConnectionScene")
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("ConnectionScene");
            }
        }
    }

    public void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        ConfigureHostConnectionData();

        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.ConfigureConnectionApproval();
            NetworkGameManager.Instance.ResetGame();
        }

        NetworkManager.Singleton.StartHost();

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    public void StartClient()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        ConfigureClientConnectionData();
        NetworkManager.Singleton.StartClient();
    }

    public void StartServer()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        ConfigureHostConnectionData();

        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.ConfigureConnectionApproval();
            NetworkGameManager.Instance.ResetGame();
        }

        NetworkManager.Singleton.StartServer();

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    /// <summary>
    /// Cambia a la escena de conexión del cliente donde se puede ingresar la IP.
    /// </summary>
    public void OpenClientScene()
    {
        Debug.Log($"[ConnectionUIHandler] Cambiando a la escena del cliente: {clientSceneName}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(clientSceneName);
    }

    /// <summary>
    /// Cambia a la escena de personalización de personaje.
    /// </summary>
    public void OpenCustomizationScene()
    {
        Debug.Log($"[ConnectionUIHandler] Cambiando a la escena de personalización: {customizationSceneName}");
        UnityEngine.SceneManagement.SceneManager.LoadScene(customizationSceneName);
    }

    /// <summary>
    /// Entra al tutorial ("Cómo jugar"): carga la copia de la escena de juego en modo
    /// completamente offline (sin Host, sin red, sin Lobby).
    /// Asignar al OnClick del botón ComoJugar_btn del menú principal.
    /// </summary>
    public void OpenTutorialScene()
    {
        Debug.Log($"[ConnectionUIHandler] Entrando al tutorial offline: {tutorialSceneName}");
        TutorialManager.OpenTutorial(tutorialSceneName);
    }
}

