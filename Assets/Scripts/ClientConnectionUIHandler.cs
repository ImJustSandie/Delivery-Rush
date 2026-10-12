using System.Collections;
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

    [Header("Transition")]
    [Tooltip("Si la sincronizacion al Lobby tarda mas que esto (segundos), se revela la pantalla y se muestra error. La escena suele llegar en 1-3s.")]
    [SerializeField] private float syncCoverTimeout = 20f;

    private Coroutine connectingAnimationCoroutine;
    private Coroutine syncCoverWatchdog;

    private void Start()
    {
        ConfigureButtons();
        SubscribeToNetworkEvents();
        UpdateStatus("");
    }

    private void OnDestroy()
    {
        StopConnectingAnimation();
        StopSyncCoverWatchdog();
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
        string rawInput = "";
        if (ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text))
        {
            rawInput = ipInputField.text.Trim();
            targetIP = RoomCodeUtility.CodeToIP(rawInput, defaultAddress);
        }

        transport.SetConnectionData(targetIP, port);
        Debug.Log($"[ClientConnectionUIHandler] Configurando IP de conexion a: {targetIP}:{port} (Input: '{rawInput}')");

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        bool success = NetworkManager.Singleton.StartClient();

        if (success)
        {
            string displayInfo = !string.IsNullOrEmpty(rawInput) ? rawInput.ToUpper() : targetIP;
            StartConnectingAnimation($"Conectando a sala [{displayInfo}] ({targetIP})");
            // El cambio a Lobby lo inicia el servidor por sincronizacion de red
            // (el cliente no sabe cuando llegara): cubrir ya con la animacion de
            // cierre y quedarse cubierto hasta que el Lobby se revele solo.
            CloseTransitionForPendingSync();
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
        StopSyncCoverWatchdog();

        // Si el cierre preventivo esta a medias, abrir al instante para que el
        // cambio de escena posterior pueda tomar la transicion (si no, lo ignora
        // por estar en curso). Si ya quedo cerrado, TransitionTo lo maneja solo.
        DiagonalSceneTransition transition = DiagonalSceneTransition.Instance;
        if (transition != null && transition.IsTransitioning)
        {
            transition.SetOpenImmediate();
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (!string.IsNullOrEmpty(previousSceneName))
        {
            DiagonalSceneTransition.TransitionTo(previousSceneName);
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
            StopSyncCoverWatchdog();
            UpdateStatus("Conexion fallida o desconectada del host.");
            // La sincronizacion no llegara: revelar si el cierre preventivo cubrio.
            ReopenTransitionIfCovered();
        }
    }

    /// <summary>
    /// Reproduce la animacion de cierre y deja la pantalla cubierta hasta que la
    /// escena sincronizada (Lobby) la revele con su apertura. Si no hay
    /// instancia de transicion, no hace nada.
    /// </summary>
    private void CloseTransitionForPendingSync()
    {
        DiagonalSceneTransition transition = DiagonalSceneTransition.Instance;
        if (transition != null && !transition.IsClosed && !transition.IsTransitioning)
        {
            transition.Close();
        }
        StopSyncCoverWatchdog();
        syncCoverWatchdog = StartCoroutine(SyncCoverWatchdogRoutine());
    }

    /// <summary>
    /// Si la sincronizacion tarda demasiado (sigue en esta escena), revela la
    /// pantalla para no dejarla cubierta sin respuesta. Al cambiar de escena
    /// este objeto se destruye y la rutina muere sola.
    /// </summary>
    private IEnumerator SyncCoverWatchdogRoutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(5f, syncCoverTimeout));
        syncCoverWatchdog = null;
        StopConnectingAnimation();
        UpdateStatus("Tiempo de espera agotado. Verifica el codigo e intentalo de nuevo.");
        ReopenTransitionIfCovered();
    }

    private void StopSyncCoverWatchdog()
    {
        if (syncCoverWatchdog != null)
        {
            StopCoroutine(syncCoverWatchdog);
            syncCoverWatchdog = null;
        }
    }

    private void ReopenTransitionIfCovered()
    {
        DiagonalSceneTransition transition = DiagonalSceneTransition.Instance;
        if (transition != null && transition.IsClosed)
        {
            transition.Open();
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
