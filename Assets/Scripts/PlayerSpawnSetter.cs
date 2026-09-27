using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawnSetter : NetworkBehaviour
{
    [Header("Spawn Configuration")]
    [Tooltip("Nombre del objeto padre en la escena que contiene los SpawnPoints individuales de cada jugador.")]
    [SerializeField] private string spawnPointsContainerName = "SpawnPoints";

    [Tooltip("Nombre base de un SpawnPoint único (o prefijo para SpawnPoint_0, SpawnPoint_1, etc.).")]
    [SerializeField] private string spawnPointObjectName = "SpawnPoint";

    [Tooltip("Altura extra sobre el SpawnPoint para evitar que el CharacterController se entierre en el suelo.")]
    [SerializeField] private float spawnHeightOffset = 0.15f;

    [Tooltip("Offset horizontal por defecto si solo existe un único SpawnPoint.")]
    [SerializeField] private float fallbackSpawnOffset = 1.5f;

    [Header("Ground Alignment")]
    [Tooltip("Nombre de la escena del Lobby donde PlayerSpawnSetter debe omitir la reubicación.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Tooltip("Si es true, realiza un Raycast hacia abajo para ajustar la altura exacta al nivel del suelo.")]
    [SerializeField] private bool snapToGround = true;

    [Tooltip("Altura desde la que baja la sonda de suelo en spawns aleatorios. Debe superar la altura del volumen de spawn más alto para no empezar enterrada.")]
    [SerializeField] private float groundProbeHeight = 30f;

    [Tooltip("Máscara de capas considerada como suelo para el Raycast de alineación.")]
    [SerializeField] private LayerMask groundLayerMask = ~0;

    [Header("Random Spawn")]
    [Tooltip("Si es true, el spawn es aleatorio en vez de determinista por slot.")]
    [SerializeField] private bool randomizeSpawn = true;
    [Tooltip("Si es true, usa los volúmenes de los CollectibleSpawner como áreas de aparición (punto aleatorio dentro del volumen).")]
    [SerializeField] private bool useSpawnerVolumes = true;
    [Tooltip("Capas consideradas obstáculos (edificios, muros, etc.). El punto se descarta si cae dentro de un Collider sólido de estas capas. Excluye la capa del suelo.")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [Tooltip("Medio tamaño de la caja de comprobación (debe aproximar el tamaño del jugador).")]
    [SerializeField] private Vector3 obstructionCheckHalfExtents = new Vector3(0.5f, 1f, 0.5f);
    [Tooltip("Número máximo de posiciones aleatorias a probar antes de rendirse (evita spawnear dentro de edificios/obstáculos).")]
    [SerializeField] private int maxSpawnAttempts = 20;

    private void Awake()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeFromSceneManager();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            if (SceneManager.GetActiveScene().name != lobbySceneName)
            {
                if (!TryApplySpawnPosition())
                {
                    SubscribeToSceneManager();
                }
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromSceneManager();
        base.OnNetworkDespawn();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsServer) return;
        if (scene.name == lobbySceneName) return;

        // Cada vez que se carga una escena (ej. MainScene de nuevo), intentar re-posicionar
        if (!TryApplySpawnPosition())
        {
            SubscribeToSceneManager();
        }
    }

    private void SubscribeToSceneManager()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnSceneLoadComplete;
        }
    }

    private void UnsubscribeFromSceneManager()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
        }
    }

    private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (!IsServer) return;

        if (TryApplySpawnPosition())
        {
            UnsubscribeFromSceneManager();
        }
    }

    private int GetAssignedSlotIndex()
    {
        LobbyPlayerDisplay display = GetComponent<LobbyPlayerDisplay>();
        if (display != null)
        {
            return display.PlayerSlotIndex;
        }

        if (NetworkGameManager.Instance != null)
        {
            return NetworkGameManager.Instance.GetPlayerSlot(OwnerClientId);
        }

        return (int)(OwnerClientId % 4);
    }

    public bool TryApplySpawnPosition()
    {
        if (SceneManager.GetActiveScene().name == lobbySceneName) return false;

        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;
        bool pointFound = false;
        int assignedSlot = GetAssignedSlotIndex();

        // 0. Spawn aleatorio: punto libre dentro de un volumen de spawner (misma lógica anti-obstáculos que los objetos).
        if (randomizeSpawn && useSpawnerVolumes)
        {
            if (TryGetRandomSpawnerPosition(out spawnPos, out spawnRot))
                pointFound = true;
        }

        if (!pointFound)
        {
            // 1. Intentar buscar dentro del contenedor "SpawnPoints" (ej. SpawnPoints -> Slot 0, Slot 1, etc.)
            GameObject container = GameObject.Find(spawnPointsContainerName);
            if (container != null && container.transform.childCount > 0)
            {
                if (randomizeSpawn)
                {
                    // Aleatorio: probar hijos en orden aleatorio hasta hallar uno libre de obstáculos.
                    pointFound = TryGetRandomFreeChildPoint(container, out spawnPos, out spawnRot);
                    if (!pointFound)
                    {
                        // Todos bloqueados: no spawnear dentro de un obstáculo, reintentar más tarde.
                        Debug.LogWarning($"[PlayerSpawnSetter] Todos los puntos de {spawnPointsContainerName} están dentro de obstáculos, reintentando más tarde.");
                        return false;
                    }
                }
                else
                {
                    int slotIndex = (int)(assignedSlot % container.transform.childCount);
                    Transform slotTransform = container.transform.GetChild(slotIndex);
                    spawnPos = slotTransform.position;
                    spawnRot = slotTransform.rotation;
                    pointFound = true;
                }
            }
            else
            {
                // 2. Intentar buscar por objeto con nombre específico (ej. SpawnPoint_0, SpawnPoint_1)
                string specificName = $"{spawnPointObjectName}_{assignedSlot}";
                GameObject specificObj = GameObject.Find(specificName);
                if (specificObj != null)
                {
                    spawnPos = specificObj.transform.position;
                    spawnRot = specificObj.transform.rotation;
                    pointFound = true;
                }
                else
                {
                    // 3. Fallback: buscar "SpawnPoint" único y aplicar offset horizontal de resguardo
                    GameObject singleObj = GameObject.Find(spawnPointObjectName);
                    if (singleObj != null)
                    {
                        Vector3 offset = new Vector3((assignedSlot % 4) * fallbackSpawnOffset, 0f, 0f);
                        spawnPos = singleObj.transform.position + offset;
                        spawnRot = singleObj.transform.rotation;
                        pointFound = true;
                    }
                }
            }

            if (!pointFound)
            {
                return false;
            }
        }

        // Ajustar altura por Raycast si está activado
        if (snapToGround)
        {
            spawnPos = GetGroundedPosition(spawnPos);
        }
        else
        {
            spawnPos += Vector3.up * spawnHeightOffset;
        }

        // Condicional: no spawnear dentro de edificios ni obstáculos (misma lógica que los objetos).
        if (IsPositionBlocked(spawnPos))
        {
            Debug.LogWarning($"[PlayerSpawnSetter] Spawn del jugador {OwnerClientId} descartado en {spawnPos}: dentro de un obstáculo.");
            return false;
        }

        // Aplicar en el servidor
        ApplySpawnPosition(spawnPos, spawnRot);

        // Si el owner es un cliente remoto (no el host), enviarle la posición
        // porque ClientNetworkTransform da autoridad al owner.
        if (!IsOwner)
        {
            TeleportOwnerToSpawnRpc(spawnPos, spawnRot);
        }

        Debug.Log($"[PlayerSpawnSetter] Jugador {OwnerClientId} (Slot {assignedSlot}) posicionado en {spawnPos} en la escena {SceneManager.GetActiveScene().name}");
        return true;
    }

    /// <summary>
    /// Devuelve true si la posición cae dentro de un Collider sólido (no Trigger)
    /// de <see cref="obstacleLayers"/>. Misma lógica que los objetos
    /// (<see cref="CollectibleSpawnManager.IsPositionBlocked"/>), con caja de tamaño de jugador.
    /// </summary>
    public bool IsPositionBlocked(Vector3 position)
    {
        Collider[] hits = Physics.OverlapBox(
            position,
            obstructionCheckHalfExtents,
            Quaternion.identity,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
        return hits != null && hits.Length > 0;
    }

    /// <summary>
    /// Punto aleatorio dentro del volumen de un CollectibleSpawner aleatorio,
    /// reintentando en otro sitio si cae dentro de un obstáculo.
    /// La altura NO se aleatoriza: se toma solo XZ del volumen y la Y la define el suelo.
    /// </summary>
    private bool TryGetRandomSpawnerPosition(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        CollectibleSpawner[] spawners = FindObjectsByType<CollectibleSpawner>(FindObjectsSortMode.None);
        if (spawners == null || spawners.Length == 0) return false;

        int attempts = Mathf.Max(1, maxSpawnAttempts);
        for (int i = 0; i < attempts; i++)
        {
            CollectibleSpawner s = spawners[Random.Range(0, spawners.Length)];
            if (s == null) continue;
            Vector3 sampled = s.GetRandomSpawnPosition();
            Vector3 candidate;
            if (snapToGround)
            {
                // Solo XZ del volumen; Y desde el suelo (sonda alta para no empezar enterrada).
                if (!TryProbeGround(new Vector3(sampled.x, sampled.y, sampled.z), out candidate))
                    continue;
            }
            else
            {
                candidate = sampled + Vector3.up * spawnHeightOffset;
            }
            // Condicional: si está dentro de una caja de colisión, probar en otro sitio
            if (IsPositionBlocked(candidate)) continue;
            position = candidate;
            rotation = s.GetSpawnRotation();
            return true;
        }
        Debug.LogWarning($"[PlayerSpawnSetter] No se halló punto libre en spawners tras {attempts} intentos (todos dentro de obstáculos o sin suelo).");
        return false;
    }

    /// <summary>Elige un hijo aleatorio del contenedor que esté libre de obstáculos (tras ajuste de suelo).</summary>
    private bool TryGetRandomFreeChildPoint(GameObject container, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        int childCount = container.transform.childCount;
        int attempts = Mathf.Max(childCount, Mathf.Max(1, maxSpawnAttempts));
        for (int i = 0; i < attempts; i++)
        {
            Transform child = container.transform.GetChild(Random.Range(0, childCount));
            if (child == null) continue;
            Vector3 candidate = child.position;
            Quaternion candidateRot = child.rotation;
            if (snapToGround)
                candidate = GetGroundedPosition(candidate);
            else
                candidate += Vector3.up * spawnHeightOffset;
            if (IsPositionBlocked(candidate)) continue;
            // Devolver el punto base (el ajuste de suelo/offset se aplica después una sola vez)
            position = child.position;
            rotation = candidateRot;
            return true;
        }
        return false;
    }

    private Vector3 GetGroundedPosition(Vector3 originalPos)
    {
        // Empezar Raycast 5 metros por encima del punto original para detectar el suelo.
        // Se ignoran Triggers para no apoyar al jugador sobre hitboxes/volúmenes.
        Vector3 rayStart = originalPos + Vector3.up * 5.0f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 50.0f, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * spawnHeightOffset;
        }
        return originalPos + Vector3.up * spawnHeightOffset;
    }

    /// <summary>
    /// Sonda de suelo para spawns aleatorios: ignora la Y de entrada, baja una sonda
    /// desde bien alto y devuelve el punto de suelo + offset. Retorna false si no hay
    /// suelo debajo (el candidato se descarta en vez de dejar al jugador enterrado).
    /// Ignora Triggers para no dejar al jugador flotando sobre hitboxes.
    /// </summary>
    private bool TryProbeGround(Vector3 sampledPos, out Vector3 groundedPos)
    {
        groundedPos = Vector3.zero;
        Vector3 rayStart = new Vector3(sampledPos.x, sampledPos.y + groundProbeHeight, sampledPos.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundProbeHeight + 100f, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            groundedPos = hit.point + Vector3.up * spawnHeightOffset;
            return true;
        }
        return false;
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerToSpawnRpc(Vector3 position, Quaternion rotation)
    {
        ApplySpawnPosition(position, rotation);
    }

    private void ApplySpawnPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        if (cc != null && IsOwner) cc.enabled = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spawnHeightOffset < -5f) spawnHeightOffset = -5f;
        if (spawnHeightOffset > 10f) spawnHeightOffset = 10f;
        if (groundProbeHeight < 5f) groundProbeHeight = 5f;
        if (fallbackSpawnOffset < 0f) fallbackSpawnOffset = 0f;
        if (maxSpawnAttempts < 1) maxSpawnAttempts = 1;
        if (obstructionCheckHalfExtents.x < 0.01f) obstructionCheckHalfExtents.x = 0.01f;
        if (obstructionCheckHalfExtents.y < 0.01f) obstructionCheckHalfExtents.y = 0.01f;
        if (obstructionCheckHalfExtents.z < 0.01f) obstructionCheckHalfExtents.z = 0.01f;
    }
#endif
}
