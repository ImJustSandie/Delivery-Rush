using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gestiona las opciones de interfaz para la navegación y desconexión durante la partida o podio.
/// Proporciona funcionalidades para:
/// 1. Volver al Lobby manteniendo la sesión activa (solo Host/Servidor).
/// 2. Desconectarse de la sesión y regresar a la escena de conexión (Host y Cliente).
/// </summary>
public class GameNavigationUIHandler : MonoBehaviour
{
    [Header("UI Buttons (Opcional)")]
    [Tooltip("Botón para que el Host regrese a todos los jugadores al Lobby sin desconectarse.")]
    [SerializeField] private Button returnToLobbyButton;

    [Tooltip("Botón para cerrar la conexión y volver a la escena de conexión.")]
    [SerializeField] private Button disconnectButton;

    [Header("Scene Config (Fallback si no hay NetworkGameManager)")]
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string connectionSceneName = "ConnectionScene";

    private void Start()
    {
        ConfigureButtons();
    }

    private void OnEnable()
    {
        UpdateButtonsState();
    }

    /// <summary>
    /// Asigna listeners y visibilidad a los botones vinculados por Inspector.
    /// </summary>
    private void ConfigureButtons()
    {
        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.onClick.RemoveAllListeners();
            returnToLobbyButton.onClick.AddListener(ReturnToLobby);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(DisconnectAndReturnToConnection);
        }

        UpdateButtonsState();
    }

    /// <summary>
    /// Actualiza la interacción/visibilidad del botón de volver al lobby según si el cliente local es Servidor/Host.
    /// </summary>
    public void UpdateButtonsState()
    {
        if (returnToLobbyButton != null)
        {
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;
            // Solo el Host puede forzar la carga de escena sincronizada al Lobby
            returnToLobbyButton.gameObject.SetActive(isServer);
        }
    }

    /// <summary>
    /// Transiciona a todos los jugadores conectados de vuelta al Lobby manteniendo la sesión de red activa.
    /// Puede ser asignado directamente a eventos OnClick de botones UI.
    /// </summary>
    public void ReturnToLobby()
    {
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.ReturnAllToLobby();
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
        {
            Debug.Log($"[GameNavigationUIHandler] Cargando escena de lobby: {lobbySceneName}");
            DiagonalSceneTransition.TransitionTo(lobbySceneName);
        }
        else
        {
            Debug.LogWarning("[GameNavigationUIHandler] No se puede volver al lobby: Se requiere ser Host/Servidor.");
        }
    }

    /// <summary>
    /// Cierra la conexión de red actual y regresa a la escena principal de conexión (ConnectionScene).
    /// Puede ser asignado directamente a eventos OnClick de botones UI.
    /// </summary>
    public void DisconnectAndReturnToConnection()
    {
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.DisconnectLocalPlayer();
        }
        else
        {
            Debug.Log("[GameNavigationUIHandler] Desconectando sesión de red...");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            DiagonalSceneTransition.TransitionTo(connectionSceneName);
        }
    }
}
