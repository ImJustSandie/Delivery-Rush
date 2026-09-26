using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Gestiona la visualización del número de jugador y su posición en la escena Lobby.
/// Este componente debe adjuntarse al Prefab del Jugador.
/// </summary>
public class LobbyPlayerDisplay : NetworkBehaviour
{
    [Header("UI & Display")]
    [Tooltip("Referencia al componente TextMeshPro que muestra el nombre/número sobre el jugador.")]
    [SerializeField] private TMP_Text playerLabelText;

    [Tooltip("Formato del texto. {0} será reemplazado por el número de jugador (OwnerClientId + 1).")]
    [SerializeField] private string labelFormat = "Jugador {0}";

    [Header("Lobby Positioning")]
    [Tooltip("Nombre del objeto padre en el Lobby que contiene las posiciones de los slots.")]
    [SerializeField] private string lobbySlotsObjectName = "LobbySlots";

    [Tooltip("Nombre de la escena de Lobby para aplicar la lógica visual.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Tooltip("Offset horizontal si no se encuentran los LobbySlots en la escena.")]
    [SerializeField] private float fallbackSlotOffset = 2.0f;

    [Header("Lobby Scale")]
    [Tooltip("Escala personalizada que adoptará el personaje mientras esté en la escena de Lobby.")]
    [SerializeField] private Vector3 lobbyScale = new Vector3(1.5f, 1.5f, 1.5f);

    [Header("Podium Scale")]
    [Tooltip("Escala personalizada que adoptará el personaje mientras esté en la escena de Podio.")]
    [SerializeField] private Vector3 podiumScale = new Vector3(1.5f, 1.5f, 1.5f);

    [Tooltip("Nombre de la escena de Podio para aplicar la escala de podio.")]
    [SerializeField] private string podiumSceneName = "Podio";

    [Tooltip("Si es true, el personaje restaurará su escala original al salir del Lobby o Podio hacia otra escena.")]
    [SerializeField] private bool restoreOriginalScaleOnExit = true;

    private Camera mainCamera;

    private bool positionApplied = false;
    private Vector3 originalScale = Vector3.one;
    private bool hasStoredOriginalScale = false;

    private readonly NetworkVariable<int> assignedSlotIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int PlayerSlotIndex => GetSlotIndex();

    public int GetSlotIndex()
    {
        if (assignedSlotIndex.Value >= 0)
        {
            return assignedSlotIndex.Value;
        }

        if (NetworkGameManager.Instance != null)
        {
            return NetworkGameManager.Instance.GetPlayerSlot(OwnerClientId);
        }

        return (int)(OwnerClientId % 4);
    }

    private void Awake()
    {
        StoreOriginalScale();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        StoreOriginalScale();

        if (IsServer)
        {
            int slot = NetworkGameManager.Instance != null 
                ? NetworkGameManager.Instance.GetOrAssignPlayerSlot(OwnerClientId) 
                : (int)(OwnerClientId % 4);
            assignedSlotIndex.Value = slot;
        }

        assignedSlotIndex.OnValueChanged += OnSlotIndexChanged;

        // Actualizar la etiqueta del jugador (número de jugador: SlotIndex + 1)
        UpdatePlayerLabel();

        // Aplicar la escala según la escena activa
        UpdateScaleForCurrentScene(SceneManager.GetActiveScene().name);

        // Suscribirse a eventos de carga de escena por si la escena actual aún está cargando
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnLoadComplete;
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;

        // Intentar aplicar posición inicial en el Lobby
        TryUpdateLobbyPosition();
    }

    public override void OnNetworkDespawn()
    {
        assignedSlotIndex.OnValueChanged -= OnSlotIndexChanged;
        base.OnNetworkDespawn();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnLoadComplete;
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }

    private void OnSlotIndexChanged(int previous, int current)
    {
        UpdatePlayerLabel();
        TryUpdateLobbyPosition();
    }

    private void StoreOriginalScale()
    {
        if (!hasStoredOriginalScale)
        {
            originalScale = transform.localScale;
            hasStoredOriginalScale = true;
        }
    }

    private void OnLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        UpdateScaleForCurrentScene(sceneName);
        if (sceneName == lobbySceneName)
        {
            if (IsServer && NetworkGameManager.Instance != null)
            {
                assignedSlotIndex.Value = NetworkGameManager.Instance.GetOrAssignPlayerSlot(OwnerClientId);
            }
            positionApplied = false;
            TryUpdateLobbyPosition();
        }
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        UpdateScaleForCurrentScene(scene.name);
        if (scene.name == lobbySceneName)
        {
            if (IsServer && NetworkGameManager.Instance != null)
            {
                assignedSlotIndex.Value = NetworkGameManager.Instance.GetOrAssignPlayerSlot(OwnerClientId);
            }
            positionApplied = false;
            TryUpdateLobbyPosition();
        }
    }

    private bool IsPodiumScene(string sceneName)
    {
        return sceneName == podiumSceneName || sceneName == "Podio" || sceneName == "Podium";
    }

    private void UpdateScaleForCurrentScene(string sceneName)
    {
        if (sceneName == lobbySceneName)
        {
            transform.localScale = lobbyScale;
        }
        else if (IsPodiumScene(sceneName))
        {
            transform.localScale = podiumScale;
        }
        else if (restoreOriginalScaleOnExit)
        {
            transform.localScale = originalScale;
        }
    }

    private void LateUpdate()
    {
        if (playerLabelText == null) return;
        Camera cam = ResolveCamera();
        if (cam == null) return;

        // Solo en Podio el texto debe mirar siempre al frente de la cámara (paralelo al plano de vista)
        // En Lobby/MainScene mantiene el billboard clásico hacia la posición de la cámara
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool isPodio = IsPodiumScene(sceneName);

        if (isPodio)
        {
            // Frente de cámara, independiente de la dirección final del jugador tras saltos
            playerLabelText.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
        }
        else
        {
            Vector3 dir = playerLabelText.transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                playerLabelText.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    private void Update()
    {
        if (!positionApplied && SceneManager.GetActiveScene().name == lobbySceneName)
        {
            TryUpdateLobbyPosition();
        }

        if (playerLabelText != null && ResolveCamera() == null)
        {
            ResolveCamera();
        }
    }

    private Camera ResolveCamera()
    {
        if (mainCamera != null) return mainCamera;

        // 1) MainCamera tag
        if (Camera.main != null)
        {
            mainCamera = Camera.main;
            return mainCamera;
        }

        // 2) Cualquier cámara activa (PlayerCamera del owner en MainScene no tiene tag MainCamera)
        Camera anyCam = FindFirstObjectByType<Camera>();
        // Preferir la cámara del jugador local (owner) si existe, para que los labels remotos miren al observador
        PlayerMovementManager localPlayer = null;
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (pm.IsOwner)
            {
                localPlayer = pm;
                break;
            }
        }
        if (localPlayer != null)
        {
            Camera localCam = localPlayer.GetComponentInChildren<Camera>(true);
            if (localCam != null && localCam.enabled)
            {
                mainCamera = localCam;
                return mainCamera;
            }
        }

        if (anyCam != null)
        {
            // Si hay varias, preferir la habilitada
            if (anyCam.enabled)
            {
                mainCamera = anyCam;
                return mainCamera;
            }
            // Fallback a la primera encontrada
            mainCamera = anyCam;
            return mainCamera;
        }

        return null;
    }

    /// <summary>
    /// Establece el texto del identificador del jugador (ej. "Jugador 1", "Jugador 2" o nombre personalizado).
    /// </summary>
    public void UpdatePlayerLabel()
    {
        if (playerLabelText == null) return;

        int playerNumber = GetSlotIndex() + 1;
        NetworkPlayerSkinSynchronizer skinSync = GetComponent<NetworkPlayerSkinSynchronizer>();
        string customName = "";

        if (skinSync != null && !string.IsNullOrWhiteSpace(skinSync.PlayerName))
        {
            customName = skinSync.PlayerName;
        }
        else if (IsOwner)
        {
            customName = PlayerCustomizationData.PlayerName;
        }

        if (!string.IsNullOrWhiteSpace(customName))
        {
            playerLabelText.text = customName;
        }
        else
        {
            playerLabelText.text = string.Format(labelFormat, playerNumber);
        }
    }

    /// <summary>
    /// Intenta posicionar al jugador en su slot correspondiente dentro del Lobby.
    /// </summary>
    public bool TryUpdateLobbyPosition()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != lobbySceneName) return false;

        GameObject slotsContainer = GameObject.Find(lobbySlotsObjectName);
        Vector3 targetPosition = Vector3.zero;
        Quaternion targetRotation = Quaternion.identity;
        bool foundSlot = false;

        int slotIndex = GetSlotIndex();

        // 1. Intentar buscar dentro del contenedor principal (LobbySlots)
        if (slotsContainer != null)
        {
            if (slotsContainer.transform.childCount > slotIndex)
            {
                Transform slot = slotsContainer.transform.GetChild(slotIndex);
                targetPosition = slot.position;
                targetRotation = slot.rotation;
                foundSlot = true;
            }
            else
            {
                string[] possibleChildNames = new string[]
                {
                    $"Slot_{slotIndex}",
                    $"Slot_{slotIndex + 1}",
                    $"Slot{slotIndex + 1}",
                    $"Slot {slotIndex + 1}",
                    $"LobbySlot_{slotIndex}",
                    $"LobbySlot_{slotIndex + 1}",
                    $"LobbySlot{slotIndex + 1}"
                };

                foreach (string cName in possibleChildNames)
                {
                    Transform childSlot = slotsContainer.transform.Find(cName);
                    if (childSlot != null)
                    {
                        targetPosition = childSlot.position;
                        targetRotation = childSlot.rotation;
                        foundSlot = true;
                        break;
                    }
                }
            }
        }

        // 2. Intentar buscar por nombre de objeto global en la escena
        if (!foundSlot)
        {
            string[] possibleGlobalNames = new string[]
            {
                $"Slot_{slotIndex}",
                $"Slot_{slotIndex + 1}",
                $"Slot{slotIndex + 1}",
                $"LobbySlot_{slotIndex}",
                $"LobbySlot_{slotIndex + 1}",
                $"LobbySlot{slotIndex + 1}"
            };

            foreach (string gName in possibleGlobalNames)
            {
                GameObject gObj = GameObject.Find(gName);
                if (gObj != null)
                {
                    targetPosition = gObj.transform.position;
                    targetRotation = gObj.transform.rotation;
                    foundSlot = true;
                    break;
                }
            }
        }

        // 3. Fallback solo si no existe ningún slot configurado en la escena
        if (!foundSlot)
        {
            targetPosition = new Vector3(slotIndex * fallbackSlotOffset, 0f, 0f);
        }

        Debug.Log($"[LobbyPlayerDisplay] Jugador {OwnerClientId} (Slot {slotIndex}): Encontró slot real={foundSlot}, Posición={targetPosition}");

        ApplyPosition(targetPosition, targetRotation);

        // Si el objeto tiene ClientNetworkTransform y es ejecutado en el servidor para un cliente remoto,
        // sincronizar la posición enviando un RPC al dueño.
        if (IsServer && !IsOwner)
        {
            TeleportLobbyOwnerRpc(targetPosition, targetRotation);
        }

        positionApplied = foundSlot;
        return foundSlot;
    }

    [Rpc(SendTo.Owner)]
    private void TeleportLobbyOwnerRpc(Vector3 position, Quaternion rotation)
    {
        ApplyPosition(position, rotation);
    }

    private void ApplyPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        if (cc != null && SceneManager.GetActiveScene().name != lobbySceneName)
        {
            cc.enabled = true;
        }
    }
}
