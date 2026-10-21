using System.Collections;
using System.Collections.Generic;
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

    [Header("Street Layer")]
    [Tooltip("Nombre de la capa de la calle. El spawn aleatorio solo acepta puntos cuyo suelo sea de esta capa.")]
    [SerializeField] private string streetLayerName = "Street";
    [Tooltip("Si es true, el spawn aleatorio descarta candidatos que no esten sobre la capa Street (evita techos, edificios y obstaculos).")]
    [SerializeField] private bool requireStreetGround = true;

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
    [Tooltip("Capas cuyos TRIGGERS también bloquean el spawn (ej. volumen invisible de zona prohibida con IsTrigger activado). Los triggers de los CollectibleSpawner siempre se ignoran.")]
    [SerializeField] private LayerMask blockerTriggerLayers = 1 << 6; // Capa 6 = Obstacles (ver TagManager.asset)

    [Header("Anti-Duplicate Spawn")]
    [Tooltip("Distancia mínima (en metros) entre el punto de spawn de este jugador y el spawn de cualquier otro jugador. 0 = desactivado.")]
    [SerializeField] private float minSpawnDistance = 3f;

    // Registro del servidor: última posición de spawn asignada a cada cliente.
    // Sirve para que dos jugadores nunca elijan el mismo punto (o puntos demasiado cercanos).
    private static readonly Dictionary<ulong, Vector3> serverSpawnPositions = new Dictionary<ulong, Vector3>();
    // Puntos hijos del contenedor SpawnPoints ya ocupados durante la escena/partida actual.
    private static readonly HashSet<Transform> usedSpawnPointTransforms = new HashSet<Transform>();

    [Header("Spawn Retry")]
    [Tooltip("Segundos máximos que el servidor reintenta posicionar a este jugador si el primer intento falla (0 = sin reintentos).")]
    [SerializeField] private float spawnRetryTimeout = 10f;
    [Tooltip("Intervalo en segundos entre reintentos de spawn del servidor.")]
    [SerializeField] private float spawnRetryInterval = 0.25f;

    // Estado de spawn REPLICADO: el servidor lo publica al crear al jugador y el estado
    // viaja JUNTO con el spawn del NetworkObject, de modo que el owner cliente se teleporta
    // en OnNetworkSpawn ANTES de que su ClientNetworkTransform (owner-authoritative) escriba
    // la posición por defecto de Netcode (la causa real de los jugadores solapados).
    private readonly NetworkVariable<bool> networkSpawnAssigned = new NetworkVariable<bool>();
    private readonly NetworkVariable<Vector3> networkSpawnPosition = new NetworkVariable<Vector3>();
    private readonly NetworkVariable<Vector4> networkSpawnRotation = new NetworkVariable<Vector4>();
    private bool serverAppliedSpawn;
    private Coroutine spawnRetryRoutine;

    // Estado de la última limpieza del registro anti-duplicados (para hacerlo una vez
    // por carga de escena aunque haya varias instancias de este componente).
    private static int lastClearedSceneHandle = int.MinValue;
    private static int lastClearedFrame = -1;

    [Header("Tutorial Fixed Spawn")]
    [Tooltip("Si es true, en el tutorial offline el spawn es siempre el primer punto fijo en vez de aleatorio. No afecta a la partida en red.")]
    [SerializeField] private bool fixedSpawnInTutorial = true;

    private void Awake()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // El dummy del tutorial conserva la posición que le diste en la escena
        // (vinculado en TutorialSceneSetup o por nombre "Dummy").
        if (TutorialManager.IsTutorialDummyObject(gameObject)) return;
        // Tutorial offline (sin red): OnNetworkSpawn nunca se ejecuta, posicionar directo.
        if (TutorialManager.IsTutorial && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            TryApplySpawnPositionOffline();
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeFromSceneManager();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkSpawnAssigned.OnValueChanged += HandleNetworkSpawnStateChanged;
        networkSpawnPosition.OnValueChanged += HandleNetworkSpawnStateChanged;
        networkSpawnRotation.OnValueChanged += HandleNetworkSpawnStateChanged;
        if (IsServer)
        {
            if (SceneManager.GetActiveScene().name != lobbySceneName)
            {
                if (!TryApplySpawnPosition())
                {
                    SubscribeToSceneManager();
                    StartSpawnRetryLoop();
                }
            }
        }
        else
        {
            // Late joiner / spawn ya publicado por el servidor: aplicarlo de inmediato.
            TryApplyNetworkSpawnPosition();
        }
    }

    public override void OnNetworkDespawn()
    {
        networkSpawnAssigned.OnValueChanged -= HandleNetworkSpawnStateChanged;
        networkSpawnPosition.OnValueChanged -= HandleNetworkSpawnStateChanged;
        networkSpawnRotation.OnValueChanged -= HandleNetworkSpawnStateChanged;
        StopSpawnRetryLoop();
        // Libera el punto que ocupaba este jugador para las siguientes partidas.
        if (IsServer) serverSpawnPositions.Remove(OwnerClientId);
        UnsubscribeFromSceneManager();
        base.OnNetworkDespawn();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Nueva partida / recarga de escena: liberar los puntos reservados UNA sola vez.
        // sceneLoaded se dispara una vez por cada instancia de PlayerSpawnSetter en
        // la escena; limpiar en cada disparo borraba la reserva que acababa de hacer
        // el jugador anterior (causa real de spawns en el mismo SpawnPoint).
        if (mode == LoadSceneMode.Single && scene.name != lobbySceneName &&
            (scene.handle != lastClearedSceneHandle || Time.frameCount != lastClearedFrame))
        {
            lastClearedSceneHandle = scene.handle;
            lastClearedFrame = Time.frameCount;
            serverSpawnPositions.Clear();
            usedSpawnPointTransforms.Clear();
        }

        if (TutorialManager.IsTutorialDummyObject(gameObject)) return;
        if (TutorialManager.IsTutorial && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            if (scene.name == lobbySceneName) return;
            TryApplySpawnPositionOffline();
            return;
        }
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
        else
        {
            StartSpawnRetryLoop();
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
    {        if (SceneManager.GetActiveScene().name == lobbySceneName) return false;

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

        // Condicional: no spawnear dentro de edificios/obstáculos ni sobre el punto
        // de otro jugador (misma lógica que los objetos + reserva anti-duplicados).
        if (IsPositionBlocked(spawnPos) || IsTooCloseToOtherPlayerSpawns(spawnPos, OwnerClientId))
        {
            Debug.LogWarning($"[PlayerSpawnSetter] Spawn del jugador {OwnerClientId} descartado en {spawnPos}: dentro de un obstáculo o demasiado cerca de otro jugador.");
            return false;
        }

        // Reservar el punto: los siguientes jugadores lo descartarán automáticamente.
        serverSpawnPositions[OwnerClientId] = spawnPos;

        // Aplicar en el servidor
        ApplySpawnPosition(spawnPos, spawnRot);
        serverAppliedSpawn = true;
        StopSpawnRetryLoop();

        // Publicar el spawn replicado: el owner cliente (que es quien tiene autoridad del
        // transform con ClientNetworkTransform) se teleporta apenas recibe el estado,
        // evitando nacer en la posición por defecto apilada con el resto de jugadores.
        networkSpawnAssigned.Value = true;
        networkSpawnPosition.Value = spawnPos;
        networkSpawnRotation.Value = new Vector4(spawnRot.x, spawnRot.y, spawnRot.z, spawnRot.w);

        // Si el owner es un cliente remoto (no el host), enviarle la posición
        // porque ClientNetworkTransform da autoridad al owner. Respaldo del estado
        // replicado (por si el valor llegó antes de que el objeto terminara de crearse).
        if (!IsOwner)
        {
            TeleportOwnerToSpawnRpc(spawnPos, spawnRot);
        }

        Debug.Log($"[PlayerSpawnSetter] Jugador {OwnerClientId} (Slot {assignedSlot}) posicionado en {spawnPos} en la escena {SceneManager.GetActiveScene().name}");
        return true;
    }

    /// <summary>
    /// Posicionamiento offline para el tutorial sin red: reutiliza la misma lógica
    /// de spawn aleatorio / SpawnPoints, pero sin puertas de IsServer ni RPCs.
    /// </summary>
    public bool TryApplySpawnPositionOffline()
    {
        if (SceneManager.GetActiveScene().name == lobbySceneName) return false;

        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;
        bool pointFound = false;

        // Tutorial: punto fijo determinista (primer hijo de SpawnPoints).
        if (fixedSpawnInTutorial && TryGetFixedTutorialPoint(out spawnPos, out spawnRot))
            pointFound = true;

        if (!pointFound && randomizeSpawn && useSpawnerVolumes)
        {
            if (TryGetRandomSpawnerPosition(out spawnPos, out spawnRot))
                pointFound = true;
        }

        if (!pointFound)
        {
            GameObject container = GameObject.Find(spawnPointsContainerName);
            if (container != null && container.transform.childCount > 0)
            {
                if (randomizeSpawn)
                {
                    pointFound = TryGetRandomFreeChildPoint(container, out spawnPos, out spawnRot);
                    if (!pointFound) return false;
                }
                else
                {
                    Transform slotTransform = container.transform.GetChild(0);
                    spawnPos = slotTransform.position;
                    spawnRot = slotTransform.rotation;
                    pointFound = true;
                }
            }
            else
            {
                GameObject singleObj = GameObject.Find(spawnPointObjectName);
                if (singleObj != null)
                {
                    spawnPos = singleObj.transform.position;
                    spawnRot = singleObj.transform.rotation;
                    pointFound = true;
                }
                else
                {
                    // Último recurso: centro del mapa a altura segura.
                    spawnPos = new Vector3(0f, 5f, 0f);
                    spawnRot = Quaternion.identity;
                    pointFound = true;
                }
            }

            if (!pointFound) return false;
        }

        if (snapToGround)
            spawnPos = GetGroundedPosition(spawnPos);
        else
            spawnPos += Vector3.up * spawnHeightOffset;

        if (IsPositionBlocked(spawnPos))
        {
            Debug.LogWarning($"[PlayerSpawnSetter] Spawn offline descartado en {spawnPos}: dentro de un obstáculo.");
            return false;
        }

        ApplySpawnPositionOffline(spawnPos, spawnRot);
        Debug.Log($"[PlayerSpawnSetter] Jugador offline posicionado en {spawnPos} en la escena {SceneManager.GetActiveScene().name}");
        return true;
    }

    private void ApplySpawnPositionOffline(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = position;
        transform.rotation = rotation;
        if (cc != null) cc.enabled = true;
        // Fija el suelo autorizado para el bloqueo de altura en juego/tutorial.
        PlayerMovementManager pm = GetComponent<PlayerMovementManager>();
        if (pm != null) pm.SetAuthorizedGroundY(position.y);
    }

    /// <summary>
    /// Devuelve true si la posición candidata está a menos de
    /// <see cref="minSpawnDistance"/> del punto de spawn registrado de otro jugador.
    /// Es el mecanismo que garantiza que dos jugadores nunca aparezcan en el mismo punto.
    /// </summary>
    private bool IsTooCloseToOtherPlayerSpawns(Vector3 position, ulong selfClientId)
    {
        float minSqr = minSpawnDistance * minSpawnDistance;
        if (minSqr <= 0f) return false;

        foreach (KeyValuePair<ulong, Vector3> entry in serverSpawnPositions)
        {
            if (entry.Key == selfClientId) continue;
            if ((entry.Value - position).sqrMagnitude < minSqr) return true;
        }
        return false;
    }

    /// <summary>Variante para el modo offline (un solo jugador local).</summary>
    private bool IsTooCloseToOtherPlayerSpawns(Vector3 position)
    {
        return IsTooCloseToOtherPlayerSpawns(position, ulong.MaxValue);
    }

    /// <summary>
    /// Devuelve true si la posición cae dentro de un Collider sólido (no Trigger)
    /// de <see cref="obstacleLayers"/>, o dentro de un Trigger de
    /// <see cref="blockerTriggerLayers"/> (zona prohibida invisible).
    /// Exclusiones: los colliders de la capa Street nunca bloquean, y tampoco
    /// bloquean los colliders cuya parte superior no sobrepasa los pies del
    /// candidato (son SUELO, no edificios). Con obstacleLayers = ~0 esto es
    /// imprescindible para no descartar TODO punto por tocar el piso (capa Default).
    /// </summary>
    public bool IsPositionBlocked(Vector3 position)
    {
        Collider[] hits = Physics.OverlapBox(
            position,
            obstructionCheckHalfExtents,
            Quaternion.identity,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            int streetMask = ResolveStreetMask();
            // Un collider que no sube más allá de los pies del candidato es suelo.
            float feetTop = position.y + 0.05f;
            foreach (Collider hit in hits)
            {
                if (hit == null) continue;
                if (streetMask != 0 && IsInMask(hit.gameObject.layer, streetMask)) continue;
                if (hit.bounds.max.y <= feetTop) continue;
                return true;
            }
        }
        return IsTriggerBlocked(position);
    }

    /// <summary>Segunda pasada: triggers de <see cref="blockerTriggerLayers"/> también bloquean el spawn.</summary>
    private bool IsTriggerBlocked(Vector3 position)
    {
        if (blockerTriggerLayers == 0) return false;

        Collider[] triggerHits = Physics.OverlapBox(
            position,
            obstructionCheckHalfExtents,
            Quaternion.identity,
            blockerTriggerLayers,
            QueryTriggerInteraction.Collide);
        if (triggerHits == null) return false;

        foreach (Collider hit in triggerHits)
        {
            if (hit == null) continue;
            // Las hitbox de los spawners son triggers: nunca cuentan como obstáculo.
            if (hit.GetComponent<CollectibleSpawner>() != null) continue;
            return true;
        }
        return false;
    }

    private int ResolveStreetMask()
    {
        if (string.IsNullOrEmpty(streetLayerName)) return 0;
        // 0 = la capa no existe (aun no creada en este build): fallback al comportamiento anterior.
        return LayerMask.GetMask(streetLayerName);
    }

    private static bool IsInMask(int layer, int mask)
    {
        return ((1 << layer) & mask) != 0;
    }

    private bool IsStreetHit(RaycastHit hit, int streetMask)
    {
        if (hit.collider == null) return false;
        return streetMask != 0 && IsInMask(hit.collider.gameObject.layer, streetMask);
    }

    /// <summary>
    /// Punto fijo del tutorial: primer hijo del contenedor SpawnPoints
    /// (SpawnPoint_0), o el objeto SpawnPoint_0 / SpawnPoint por nombre.
    /// Solo se usa en el tutorial offline; la partida en red sigue aleatoria.
    /// </summary>
    private bool TryGetFixedTutorialPoint(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        GameObject container = GameObject.Find(spawnPointsContainerName);
        if (container != null && container.transform.childCount > 0)
        {
            Transform first = container.transform.GetChild(0);
            if (first != null)
            {
                position = first.position;
                rotation = first.rotation;
                return true;
            }
        }
        GameObject named = GameObject.Find($"{spawnPointObjectName}_0");
        if (named == null) named = GameObject.Find(spawnPointObjectName);
        if (named != null)
        {
            position = named.transform.position;
            rotation = named.transform.rotation;
            return true;
        }
        return false;
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
            // Condicional: si está dentro de una caja de colisión o cerca del punto
            // de otro jugador, probar en otro sitio
            if (IsPositionBlocked(candidate)) continue;
            if (IsTooCloseToOtherPlayerSpawns(candidate, OwnerClientId)) continue;
            position = candidate;
            rotation = s.GetSpawnRotation();
            return true;
        }
        Debug.LogWarning($"[PlayerSpawnSetter] No se halló punto libre en spawners tras {attempts} intentos (todos dentro de obstáculos o sin suelo).");
        return false;
    }

    /// <summary>
    /// Elige un hijo aleatorio del contenedor libre de obstáculos, priorizando puntos
    /// que todavía NO hayan sido usados por otro jugador en esta escena. Así dos
    /// jugadores nunca toman el mismo SpawnPoint mientras haya puntos libres.
    /// </summary>
    private bool TryGetRandomFreeChildPoint(GameObject container, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        // Primera pasada: solo puntos nunca usados en esta partida.
        if (TryGetFreeChildPoint(container, true, out position, out rotation)) return true;

        // Segunda pasada: si hay más jugadores que puntos y todos están usados,
        // aceptar un punto usado pero libre de obstáculos y alejado de otros jugadores.
        return TryGetFreeChildPoint(container, false, out position, out rotation);
    }

    private bool TryGetFreeChildPoint(GameObject container, bool onlyUnused, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        int childCount = container.transform.childCount;
        int attempts = Mathf.Max(childCount, Mathf.Max(1, maxSpawnAttempts));
        for (int i = 0; i < attempts; i++)
        {
            Transform child = container.transform.GetChild(Random.Range(0, childCount));
            if (child == null) continue;
            if (onlyUnused && usedSpawnPointTransforms.Contains(child)) continue;
            Vector3 candidate = child.position;
            Quaternion candidateRot = child.rotation;
            if (snapToGround)
                candidate = GetGroundedPosition(candidate);
            else
                candidate += Vector3.up * spawnHeightOffset;
            if (IsPositionBlocked(candidate)) continue;
            if (IsTooCloseToOtherPlayerSpawns(candidate, OwnerClientId)) continue;
            // Reservar el hijo para los siguientes jugadores.
            usedSpawnPointTransforms.Add(child);
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
        // Prioridad a la capa Street: si existe y golpea, ese es el suelo válido.
        Vector3 rayStart = originalPos + Vector3.up * 5.0f;
        int streetMask = ResolveStreetMask();
        if (requireStreetGround && streetMask != 0)
        {
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit streetHit, 50.0f, streetMask, QueryTriggerInteraction.Ignore))
            {
                return streetHit.point + Vector3.up * spawnHeightOffset;
            }
        }
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 50.0f, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            // Si se exige calle pero el suelo golpeado no es Street, igual se devuelve
            // (puntos fijos deterministas); el filtro real anti-obstáculos se aplica
            // en el spawn aleatorio vía TryProbeGround.
            return hit.point + Vector3.up * spawnHeightOffset;
        }
        return originalPos + Vector3.up * spawnHeightOffset;
    }

    /// <summary>
    /// Sonda de suelo para spawns aleatorios: ignora la Y de entrada, baja una sonda
    /// desde bien alto y devuelve el punto de suelo + offset. Retorna false si no hay
    /// suelo debajo (el candidato se descarta en vez de dejar al jugador enterrado).
    /// Ignora Triggers para no dejar al jugador flotando sobre hitboxes.
    /// Prioriza el suelo de la capa Street; si no hay nada en esa capa (escena sin
    /// etiquetar) cae al suelo general para que el spawn aleatorio siga funcionando.
    /// </summary>
    private bool TryProbeGround(Vector3 sampledPos, out Vector3 groundedPos)
    {
        groundedPos = Vector3.zero;
        Vector3 rayStart = new Vector3(sampledPos.x, sampledPos.y + groundProbeHeight, sampledPos.z);
        int streetMask = ResolveStreetMask();
        if (streetMask != 0)
        {
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit streetHit, groundProbeHeight + 100f, streetMask, QueryTriggerInteraction.Ignore))
            {
                groundedPos = streetHit.point + Vector3.up * spawnHeightOffset;
                return true;
            }
            // No forced return: si la escena no tiene colliders en la capa Street,
            // morir aqui descartaria el 100% de los puntos aleatorios del volumen.
        }
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

    /// <summary>Callback de cualquier NetworkVariable de spawn: intenta aplicar el spawn replicado.</summary>
    private void HandleNetworkSpawnStateChanged<T>(T previousValue, T newValue)
    {
        TryApplyNetworkSpawnPosition();
    }

    /// <summary>
    /// Aplica en el cliente owner el spawn publicado por el servidor. El owner tiene
    /// la autoridad del transform (ClientNetworkTransform), así que basta con que él
    /// mismo se mueva: la posición se replica sola al servidor y al resto de clientes.
    /// Los clientes no-owner no hacen nada (reciben la posición por replicación).
    /// </summary>
    private void TryApplyNetworkSpawnPosition()
    {
        if (IsServer) return;         // El servidor ya aplicó su copia directamente.
        if (!IsOwner) return;         // Avatares ajenos: llegan por el NetworkTransform del owner.
        if (!networkSpawnAssigned.Value) return;

        Vector4 rot = networkSpawnRotation.Value;
        ApplySpawnPosition(networkSpawnPosition.Value, new Quaternion(rot.x, rot.y, rot.z, rot.w));
        Debug.Log($"[PlayerSpawnSetter] Owner {OwnerClientId} aplicó su spawn replicado en {networkSpawnPosition.Value}.");
    }

    /// <summary>
    /// Reintenta periódicamente en el servidor hasta posicionar al jugador (o agotar el
    /// tiempo). Evita que un primer intento fallido deje al jugador apilado en la posición
    /// por defecto de Netcode junto a los demás hasta la siguiente carga de escena.
    /// </summary>
    private void StartSpawnRetryLoop()
    {
        if (spawnRetryRoutine != null || spawnRetryTimeout <= 0f) return;
        spawnRetryRoutine = StartCoroutine(SpawnRetryLoop());
    }

    private void StopSpawnRetryLoop()
    {
        if (spawnRetryRoutine == null) return;
        StopCoroutine(spawnRetryRoutine);
        spawnRetryRoutine = null;
    }

    private IEnumerator SpawnRetryLoop()
    {
        float interval = Mathf.Max(0.05f, spawnRetryInterval);
        float elapsed = 0f;
        while (!serverAppliedSpawn && elapsed < spawnRetryTimeout)
        {
            yield return new WaitForSeconds(interval);
            elapsed += interval;
            if (serverAppliedSpawn) break;
            TryApplySpawnPosition();
        }

        spawnRetryRoutine = null;
        if (!serverAppliedSpawn && isActiveAndEnabled)
        {
            Debug.LogWarning($"[PlayerSpawnSetter] El jugador {OwnerClientId} sigue sin spawn válido tras {spawnRetryTimeout}s de reintentos.");
        }
    }

    private void ApplySpawnPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        if (cc != null && IsOwner) cc.enabled = true;

        // Fija el suelo autorizado para el bloqueo de altura en juego/tutorial.
        PlayerMovementManager pm = GetComponent<PlayerMovementManager>();
        if (pm != null) pm.SetAuthorizedGroundY(position.y);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spawnHeightOffset < -5f) spawnHeightOffset = -5f;
        if (spawnHeightOffset > 10f) spawnHeightOffset = 10f;
        if (groundProbeHeight < 5f) groundProbeHeight = 5f;
        if (fallbackSpawnOffset < 0f) fallbackSpawnOffset = 0f;
        if (maxSpawnAttempts < 1) maxSpawnAttempts = 1;
        if (minSpawnDistance < 0f) minSpawnDistance = 0f;
        if (spawnRetryTimeout < 0f) spawnRetryTimeout = 0f;
        if (spawnRetryInterval < 0.05f) spawnRetryInterval = 0.05f;
        if (obstructionCheckHalfExtents.x < 0.01f) obstructionCheckHalfExtents.x = 0.01f;
        if (obstructionCheckHalfExtents.y < 0.01f) obstructionCheckHalfExtents.y = 0.01f;
        if (obstructionCheckHalfExtents.z < 0.01f) obstructionCheckHalfExtents.z = 0.01f;
    }
#endif
}
