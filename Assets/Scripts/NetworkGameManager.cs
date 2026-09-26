using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Administrador del estado de la partida y aprobacion de conexiones de red.
/// Evita que se unan mas clientes una vez iniciada la ronda.
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    [Header("Scene Names")]
    [SerializeField] private string connectionSceneName = "ConnectionScene";
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string mainSceneName = "MainScene";

    [Header("Game State & Slot Configuration")]
    [SerializeField] private bool gameStarted = false;
    [SerializeField] private int maxPlayers = 4;

    public bool IsGameStarted => gameStarted;
    public int MaxPlayers => maxPlayers;
    public string ConnectionSceneName => connectionSceneName;
    public string LobbySceneName => lobbySceneName;
    public string MainSceneName => mainSceneName;

    private readonly System.Collections.Generic.Dictionary<ulong, int> clientSlotMap = new System.Collections.Generic.Dictionary<ulong, int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        ConfigureConnectionApproval();
        SubscribeToNetworkEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
    }

    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        FreePlayerSlot(clientId);

        // Si el cliente desconectado es el cliente local (y no es el host cerrando el servidor)
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId && !NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[NetworkGameManager] El cliente local se ha desconectado del host. Regresando a ConnectionScene...");
            ResetGame();
            if (SceneManager.GetActiveScene().name != connectionSceneName)
            {
                SceneManager.LoadScene(connectionSceneName);
            }
        }
    }

    /// <summary>
    /// Asigna o recupera el slot de jugador (0 a maxPlayers - 1) para un clientId dado.
    /// </summary>
    public int GetOrAssignPlayerSlot(ulong clientId)
    {
        if (clientSlotMap.TryGetValue(clientId, out int slot))
        {
            return slot;
        }

        bool[] usedSlots = new bool[maxPlayers];
        foreach (var kvp in clientSlotMap)
        {
            if (kvp.Value >= 0 && kvp.Value < maxPlayers)
            {
                usedSlots[kvp.Value] = true;
            }
        }

        int freeSlot = 0;
        for (int i = 0; i < maxPlayers; i++)
        {
            if (!usedSlots[i])
            {
                freeSlot = i;
                break;
            }
        }

        clientSlotMap[clientId] = freeSlot;
        Debug.Log($"[NetworkGameManager] Asignado Slot {freeSlot} al cliente {clientId}.");
        return freeSlot;
    }

    /// <summary>
    /// Obtiene el slot asignado a un clientId o calcula fallback.
    /// </summary>
    public int GetPlayerSlot(ulong clientId)
    {
        if (clientSlotMap.TryGetValue(clientId, out int slot))
        {
            return slot;
        }
        return (int)(clientId % (ulong)maxPlayers);
    }

    /// <summary>
    /// Libera el slot ocupado por un cliente al desconectarse.
    /// </summary>
    public void FreePlayerSlot(ulong clientId)
    {
        if (clientSlotMap.ContainsKey(clientId))
        {
            Debug.Log($"[NetworkGameManager] Liberando Slot {clientSlotMap[clientId]} del cliente {clientId}.");
            clientSlotMap.Remove(clientId);
        }
    }

    /// <summary>
    /// Configura la aprobacion de conexiones en el NetworkManager.
    /// </summary>
    public void ConfigureConnectionApproval()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCheck;
        SubscribeToNetworkEvents();
    }

    /// <summary>
    /// Callback invocado cuando un cliente intenta conectarse al servidor/host.
    /// </summary>
    private void ConnectionApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // Si el juego ya inicio, rechazar la conexion
        if (gameStarted)
        {
            response.Approved = false;
            response.Reason = "La partida ya ha comenzado. No puedes unirte en este momento.";
            response.CreatePlayerObject = false;
            response.Pending = false;
            Debug.LogWarning($"[NetworkGameManager] Conexion rechazada para el cliente {request.ClientNetworkId}: La partida ya esta en curso.");
            return;
        }

        // Si la sala está llena (máximo de clientes alcanzado)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count >= maxPlayers)
        {
            response.Approved = false;
            response.Reason = $"La sala está llena. Máximo {maxPlayers} jugadores permitidos.";
            response.CreatePlayerObject = false;
            response.Pending = false;
            Debug.LogWarning($"[NetworkGameManager] Conexión rechazada para cliente {request.ClientNetworkId}: Sala llena ({maxPlayers}/{maxPlayers}).");
            return;
        }

        // Si esta en el Lobby o antes del inicio y hay espacio, aprobar conexion
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Pending = false;
        Debug.Log($"[NetworkGameManager] Conexion aprobada para el cliente {request.ClientNetworkId}.");
    }

    /// <summary>
    /// Marca el juego como iniciado e inhabilita nuevas conexiones.
    /// </summary>
    public void StartGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            gameStarted = true;
            Debug.Log("[NetworkGameManager] La partida ha comenzado. Se han bloqueado nuevas conexiones de clientes.");
        }
    }

    /// <summary>
    /// Restablece el estado del juego para permitir nuevas conexiones en el lobby manteniendo los slots asignados a los clientes actuales.
    /// </summary>
    public void ResetGame(bool clearSlots = false)
    {
        gameStarted = false;
        if (clearSlots)
        {
            clientSlotMap.Clear();
        }
        Debug.Log($"[NetworkGameManager] Estado del juego restablecido. (Slots limpiados: {clearSlots}). Nuevas conexiones permitidas.");
    }

    /// <summary>
    /// Retorna a todos los jugadores conectados a la escena Lobby sin cerrar la conexión de red (exclusivo para Servidor/Host).
    /// </summary>
    public void ReturnAllToLobby()
    {
        ResetGame();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
        {
            Debug.Log($"[NetworkGameManager] Transicionando a todos los jugadores a la escena de Lobby: {lobbySceneName}");
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogWarning("[NetworkGameManager] Solo el Servidor/Host puede regresar a todos los jugadores al Lobby.");
        }
    }

    /// <summary>
    /// Desconecta la sesión de red del jugador local y lo regresa a la escena de conexión principal.
    /// </summary>
    public void DisconnectLocalPlayer()
    {
        Debug.Log("[NetworkGameManager] Desconectando jugador local y regresando a la escena de conexión...");
        ResetGame(true);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (SceneManager.GetActiveScene().name != connectionSceneName)
        {
            SceneManager.LoadScene(connectionSceneName);
        }
    }
}

