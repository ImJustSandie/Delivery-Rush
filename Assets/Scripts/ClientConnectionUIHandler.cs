using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Gestiona la interfaz de usuario y la logica de conexion para la escena del cliente.
/// Permite ingresar una IP, conectarse a un host/servidor mediante Unity NGO,
/// o regresar a la escena anterior.
/// </summary>
public class ClientConnectionUIHandler : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("Campo de texto de TMP para ingresar la IP del servidor/host.")]
    [SerializeField] private TMP_InputField ipInputField;

    [Tooltip("Boton para iniciar la conexion como cliente.")]
    [SerializeField] private Button connectButton;

    [Tooltip("Boton para regresar a la escena anterior.")]
    [SerializeField] private Button backButton;

    [Tooltip("Texto opcional para mostrar mensajes de estado de la conexion.")]
    [SerializeField] private TMP_Text statusText;

    [Header("Network Connection Configuration")]
    [Tooltip("IP por defecto si el campo de texto esta vacio.")]
    [SerializeField] private string defaultAddress = "127.0.0.1";

    [Tooltip("Puerto de conexion.")]
    [SerializeField] private ushort port = 7777;

    [Header("Scene Configuration")]
    [Tooltip("Nombre de la escena anterior a la que se regresara al presionar Volver.")]
    [SerializeField] private string previousSceneName = "ConnectionScene";

    [Header("Animation Settings")]
    [Tooltip("Intervalo de tiempo en segundos para la animacion de tres puntos.")]
    [SerializeField] private float dotAnimationInterval = 0.5f;

    private Coroutine connectingAnimationCoroutine;

    private void Start()
    {
        ConfigureButtons();
        SubscribeToNetworkEvents();
        UpdateStatus("");
    }

    private void OnDestroy()
    {
        StopConnectingAnimation();
        UnsubscribeFromNetworkEvents();
    }

    /// <summary>
    /// Configura los oyentes (listeners) de los botones de la UI si estan asignados.
    /// </summary>
    private void ConfigureButtons()
    {
        if (connectButton != null)
        {
            connectButton.onClick.RemoveAllListeners();
            connectButton.onClick.AddListener(OnConnectButtonClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
    }

    /// <summary>
    /// Suscribe callbacks a los eventos del NetworkManager.
    /// </summary>
    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    /// <summary>
    /// Desuscribe callbacks del NetworkManager.
    /// </summary>
    private void UnsubscribeFromNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    /// <summary>
    /// Intenta conectar al cliente a la IP indicada.
    /// </summary>
    public void OnConnectButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ClientConnectionUIHandler] NetworkManager.Singleton no fue encontrado en la escena.");
            UpdateStatus("Error: NetworkManager no disponible.");
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[ClientConnectionUIHandler] UnityTransport no fue encontrado en NetworkManager.");
            UpdateStatus("Error: UnityTransport no encontrado.");
            return;
        }

        string targetIP = defaultAddress;
        if (ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text))
        {
            targetIP = ipInputField.text.Trim();
        }

        transport.SetConnectionData(targetIP, port);
        Debug.Log($"[ClientConnectionUIHandler] Configurando IP de conexion a: {targetIP}:{port}");

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        bool success = NetworkManager.Singleton.StartClient();

        if (success)
        {
            StartConnectingAnimation($"Conectando a {targetIP}:{port}");
        }
        else
        {
            Debug.LogWarning("[ClientConnectionUIHandler] Fallo al iniciar el cliente.");
            UpdateStatus("Error al iniciar el cliente.");
        }
    }

    /// <summary>
    /// Regresa a la escena anterior especificada en `previousSceneName`.
    /// </summary>
    public void OnBackButtonClicked()
    {
        Debug.Log($"[ClientConnectionUIHandler] Regresando a la escena previa: {previousSceneName}");

        StopConnectingAnimation();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (!string.IsNullOrEmpty(previousSceneName))
        {
            SceneManager.LoadScene(previousSceneName);
        }
        else
        {
            Debug.LogWarning("[ClientConnectionUIHandler] Nombre de escena anterior no configurado.");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log($"[ClientConnectionUIHandler] Cliente conectado exitosamente con ID: {clientId}");
            StopConnectingAnimation();
            UpdateStatus("¡Conexion establecida!");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.LogWarning("[ClientConnectionUIHandler] Desconectado o la conexion fallo.");
            StopConnectingAnimation();
            UpdateStatus("Conexion fallida o desconectada del host.");
        }
    }

    private void StartConnectingAnimation(string baseMessage)
    {
        StopConnectingAnimation();
        connectingAnimationCoroutine = StartCoroutine(AnimateConnectingText(baseMessage));
    }

    private void StopConnectingAnimation()
    {
        if (connectingAnimationCoroutine != null)
        {
            StopCoroutine(connectingAnimationCoroutine);
            connectingAnimationCoroutine = null;
        }
    }

    private System.Collections.IEnumerator AnimateConnectingText(string baseMessage)
    {
        int dotCount = 0;

        while (true)
        {
            string dots = new string('.', dotCount);
            if (statusText != null)
            {
                statusText.text = $"{baseMessage}{dots}";
            }

            dotCount = (dotCount + 1) % 4;
            yield return new WaitForSeconds(dotAnimationInterval);
        }
    }

    private void UpdateStatus(string message)
    {
        StopConnectingAnimation();
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
