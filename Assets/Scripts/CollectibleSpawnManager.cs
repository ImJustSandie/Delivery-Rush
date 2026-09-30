using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manager central que administra la instanciación de recolectables.
/// Cuenta cuántas instancias hay activas y respeta un límite máximo.
/// Solo el servidor puede instanciar NetworkObjects.
/// </summary>
public class CollectibleSpawnManager : MonoBehaviour
{
    public static CollectibleSpawnManager Instance { get; private set; }

    [Header("Prefab")]
    [Tooltip("Prefab del recolectable (debe tener NetworkObject + InteractableCube). Asignar Object.prefab.")]
    [SerializeField] private NetworkObject collectiblePrefab;

    [Header("Power-Up (raro)")]
    [Tooltip("Prefab del power-up (debe tener NetworkObject + PowerUpCube). Misma lógica de spawn que el objeto normal.")]
    [SerializeField] private NetworkObject powerUpPrefab;
    [Tooltip("Probabilidad de que cada spawn sea un power-up en vez de un objeto normal (0.1 = 10%).")]
    [Range(0f, 1f)]
    [SerializeField] private float powerUpChance = 0.1f;
    [Tooltip("Número máximo de power-ups simultáneos en escena (además del límite total).")]
    [SerializeField] private int maxPowerUps = 2;
    [Tooltip("Probabilidad de que un power-up RECOGIDO sea de ralentizar (SlowOthers). El resto será de velocidad (BoostSelf). El sorteo ocurre al recoger, no al usar. Ej: 0.5 = mitad y mitad.")]
    [Range(0f, 1f)]
    [SerializeField] private float slowPowerUpChance = 0.5f;

    public float SlowPowerUpChance => slowPowerUpChance;

    [Header("Límites")]
    [Tooltip("Número máximo de instancias simultáneas en escena. No se generan más si se alcanza.")]
    [SerializeField] private int maxInstances = 5;
    [Tooltip("Si es true, al iniciar el servidor se generan instancias hasta llegar al límite (o initialSpawnCount).")]
    [SerializeField] private bool spawnOnStart = true;
    [Tooltip("Cantidad a generar al inicio. Si es 0 usa maxInstances.")]
    [SerializeField] private int initialSpawnCount = 0;

    [Header("Respawn automático")]
    [Tooltip("Intervalo en segundos para reintentar spawn automático (0 = desactivado). Solo en servidor. Si es 0 igual respawnea al recolectar si respawnOnCollected está activo.")]
    [SerializeField] private float autoSpawnInterval = 2f;
    [Tooltip("Si el auto-spawn debe elegir un spawner aleatorio como punto de aparición.")]
    [SerializeField] private bool useSpawnersAsSpawnPoints = true;
    [Tooltip("Si al recolectar/destruir un objeto se genera otro automáticamente para reponer hasta el límite.")]
    [SerializeField] private bool respawnOnCollected = true;
    [Tooltip("Retardo en segundos antes de reponer tras recolectar.")]
    [SerializeField] private float respawnDelay = 1f;

    [Header("Puntos de spawn")]
    [Tooltip("Puntos fijos donde instanciar. Si está vacío y useSpawnersAsSpawnPoints es true, usa la posición de cada CollectibleSpawner.")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("Offset vertical al instanciar para evitar solapamiento con el suelo.")]
    [SerializeField] private float spawnHeightOffset = 0.5f;

    [Header("Validación de obstáculos")]
    [Tooltip("Capas consideradas obstáculos (edificios, muros, etc.). El punto de spawn se descarta si cae dentro de un Collider sólido de estas capas. IMPORTANTE: excluye la capa del suelo si el chequeo toca el piso, o usa una capa propia para edificios/obstáculos.")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [Tooltip("Medio tamaño de la caja de comprobación (debe aproximar el tamaño del recolectable).")]
    [SerializeField] private Vector3 obstructionCheckHalfExtents = new Vector3(0.4f, 0.4f, 0.4f);
    [Tooltip("Número máximo de posiciones aleatorias a probar antes de rendirse y no spawnear (evita spawnear dentro de edificios/obstáculos).")]
    [SerializeField] private int maxSpawnAttempts = 10;

    private readonly HashSet<InteractableCube> trackedInstances = new HashSet<InteractableCube>();
    private readonly HashSet<PowerUpCube> trackedPowerUps = new HashSet<PowerUpCube>();
    private readonly List<CollectibleSpawner> registeredSpawners = new List<CollectibleSpawner>();

    private float nextAutoSpawnTime;

    public int ActiveCount
    {
        get
        {
            // Limpieza de referencias nulas (objetos destruidos sin Unregister)
            trackedInstances.RemoveWhere(c => c == null);
            trackedPowerUps.RemoveWhere(p => p == null);
            return trackedInstances.Count + trackedPowerUps.Count;
        }
    }

    public int ActivePowerUpCount
    {
        get
        {
            trackedPowerUps.RemoveWhere(p => p == null);
            return trackedPowerUps.Count;
        }
    }

    public int MaxInstances => maxInstances;
    public bool CanSpawn => ActiveCount < maxInstances;
    public bool CanSpawnPowerUp => CanSpawn && ActivePowerUpCount < maxPowerUps;
    public int RemainingSlots => Mathf.Max(0, maxInstances - ActiveCount);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[CollectibleSpawnManager] Ya existe una instancia en {Instance.name}, destruyendo {name}.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Migración: si quedó 0 por defecto antiguo, activar respawn periódico
        if (autoSpawnInterval == 0f && respawnOnCollected)
            autoSpawnInterval = 2f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            // Si ya somos servidor (host iniciado antes de cargar escena)
            if (NetworkManager.Singleton.IsServer && spawnOnStart)
            {
                // Delay un frame para que los spawners se registren
                Invoke(nameof(SpawnInitialBatch), 0.5f);
            }
            // Tutorial offline: no hay OnServerStarted, generar directo.
            else if (TutorialManager.IsTutorial && !NetworkManager.Singleton.IsListening && spawnOnStart)
            {
                Invoke(nameof(SpawnInitialBatch), 0.5f);
            }

            // Registrar instancias ya colocadas en escena (modo retrocompatibilidad)
            RegisterScenePlacedInstances();
        }
        else if (spawnOnStart)
        {
            Debug.LogWarning("[CollectibleSpawnManager] NetworkManager.Singleton no encontrado, el conteo funcionará sin red.");
            RegisterScenePlacedInstances();
            // Sin NetworkManager también generar lote inicial offline.
            if (TutorialManager.IsTutorial)
                Invoke(nameof(SpawnInitialBatch), 0.5f);
        }
    }

    private void OnEnable()
    {
        RegisterScenePlacedInstances();
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        }
    }

    private void HandleServerStarted()
    {
        RegisterScenePlacedInstances();
        if (spawnOnStart)
            Invoke(nameof(SpawnInitialBatch), 0.5f);
    }

    private void Update()
    {
        if (autoSpawnInterval <= 0f) return;
        if (!IsServer) return;
        if (Time.time < nextAutoSpawnTime) return;

        nextAutoSpawnTime = Time.time + autoSpawnInterval;
        TrySpawnRandom();
    }

    // ── Registro de instancias ──────────────────────────────────────────────

    public void Register(InteractableCube cube)
    {
        if (cube == null) return;
        if (trackedInstances.Add(cube))
        {
            // Debug.Log($"[CollectibleSpawnManager] Registrado {cube.name} -> {ActiveCount}/{maxInstances}");
        }
    }

    /// <summary>Registra un power-up instanciado.</summary>
    public void Register(PowerUpCube powerUp)
    {
        if (powerUp == null) return;
        trackedPowerUps.Add(powerUp);
    }

    public void Unregister(InteractableCube cube)
    {
        if (cube == null) return;
        bool removed = trackedInstances.Remove(cube);
        if (removed)
        {
            // Debug.Log($"[CollectibleSpawnManager] Desregistrado {cube.name} -> {ActiveCount}/{maxInstances}");
            // Reponer automáticamente para mantener el límite
            if (respawnOnCollected && CanSpawn && IsServer)
            {
                if (respawnDelay <= 0f)
                    TrySpawnRandom();
                else
                    Invoke(nameof(TrySpawnRandomDelayed), respawnDelay);
            }
            // Si hay autoSpawnInterval, asegurar próximo tick
            if (autoSpawnInterval > 0f && IsServer)
                nextAutoSpawnTime = Time.time + autoSpawnInterval;
        }
    }

    /// <summary>Desregistra un power-up recolectado/destruido y repone otro objeto.</summary>
    public void Unregister(PowerUpCube powerUp)
    {
        if (powerUp == null) return;
        bool removed = trackedPowerUps.Remove(powerUp);
        if (removed && respawnOnCollected && CanSpawn && IsServer)
        {
            if (respawnDelay <= 0f)
                TrySpawnRandom();
            else
                Invoke(nameof(TrySpawnRandomDelayed), respawnDelay);
            if (autoSpawnInterval > 0f)
                nextAutoSpawnTime = Time.time + autoSpawnInterval;
        }
    }

    private bool IsServer
    {
        get
        {
            // Tutorial offline: actuar como servidor local aunque no haya red.
            if (TutorialManager.IsTutorial && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
                return true;
            if (NetworkManager.Singleton == null) return true; // modo offline/editor
            return NetworkManager.Singleton.IsServer;
        }
    }

    /// <summary>True si hay red activa como servidor; false en tutorial offline.</summary>
    private bool IsNetworkServer
    {
        get
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;
        }
    }

    private void TrySpawnRandomDelayed()
    {
        if (!CanSpawn) return;
        TrySpawnRandom();
    }

    /// <summary>Busca cubos y power-ups ya colocados manualmente en la escena para contarlos.</summary>
    private void RegisterScenePlacedInstances()
    {
        foreach (InteractableCube cube in FindObjectsByType<InteractableCube>(FindObjectsSortMode.None))
        {
            // Solo contar los que ya están spawneados / activos en escena
            // Evitar duplicar
            if (!trackedInstances.Contains(cube))
                trackedInstances.Add(cube);
        }
        foreach (PowerUpCube powerUp in FindObjectsByType<PowerUpCube>(FindObjectsSortMode.None))
        {
            if (!trackedPowerUps.Contains(powerUp))
                trackedPowerUps.Add(powerUp);
        }
    }

    // ── Registro de spawners (hitbox) ──────────────────────────────────────

    public void RegisterSpawner(CollectibleSpawner spawner)
    {
        if (spawner == null || registeredSpawners.Contains(spawner)) return;
        registeredSpawners.Add(spawner);
    }

    public void UnregisterSpawner(CollectibleSpawner spawner)
    {
        registeredSpawners.Remove(spawner);
    }

    // ── API de spawn ───────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve true si la posición cae dentro de un Collider sólido (no Trigger)
    /// de las <see cref="obstacleLayers"/>. Usa OverlapBox con
    /// <see cref="QueryTriggerInteraction.Ignore"/> para que las hitbox triggers
    /// de los spawners no bloqueen el spawn.
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
    /// Decide si este spawn debe ser power-up (10% por defecto) o cubo normal.
    /// Respeta el límite de power-ups: si está lleno, sale cubo normal.
    /// </summary>
    private bool RollIsPowerUp()
    {
        if (powerUpPrefab == null) return false;
        if (ActivePowerUpCount >= maxPowerUps) return false;
        return UnityEngine.Random.value < powerUpChance;
    }

    /// <summary>Instancia el prefab elegido (power-up o normal) y lo registra. Asume límite y bloqueo ya validados.</summary>
    private bool SpawnChosenPrefab(Vector3 position, Quaternion rotation, bool isPowerUp)
    {
        NetworkObject prefabToSpawn = isPowerUp ? powerUpPrefab : collectiblePrefab;
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("[CollectibleSpawnManager] Prefab no asignado (revisa collectiblePrefab / powerUpPrefab).");
            return false;
        }

        NetworkObject instance = Instantiate(prefabToSpawn, position, rotation);
        // Solo hacer Spawn de red si hay sesión activa como servidor; en offline queda como objeto local.
        if (instance != null && IsNetworkServer)
        {
            instance.Spawn(true);
        }

        if (isPowerUp)
        {
            PowerUpCube powerUp = instance.GetComponent<PowerUpCube>();
            if (powerUp != null && !trackedPowerUps.Contains(powerUp))
                trackedPowerUps.Add(powerUp);
        }
        else
        {
            // El InteractableCube se registrará solo en OnNetworkSpawn/OnEnable;
            // por si el prefab no llama a Register (p.ej. sin red), lo registramos aquí
            InteractableCube cube = instance.GetComponent<InteractableCube>();
            if (cube != null && !trackedInstances.Contains(cube))
                trackedInstances.Add(cube);
        }
        return true;
    }

    /// <summary>
    /// Intenta instanciar en una posición/rotación concreta.
    /// Respeta el límite y solo funciona en servidor.
    /// Descarta la posición si está dentro de un obstáculo.
    /// Cada spawn tiene <see cref="powerUpChance"/> de ser power-up.
    /// </summary>
    public bool TrySpawnAt(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            return false;
        }

        if (!CanSpawn) return false;
        if (collectiblePrefab == null)
        {
            Debug.LogWarning("[CollectibleSpawnManager] collectiblePrefab no asignado. Asignar Object.prefab en el Inspector.");
            return false;
        }

        position.y += spawnHeightOffset;

        // Condicional: no generar dentro de edificios ni obstáculos.
        // Si la posición cae dentro de una caja de colisión sólida, se descarta.
        if (IsPositionBlocked(position))
        {
            Debug.Log($"[CollectibleSpawnManager] Spawn descartado en {position}: dentro de un obstáculo.");
            return false;
        }

        return SpawnChosenPrefab(position, rotation, RollIsPowerUp());
    }

    /// <summary>Intenta spawnear en un spawnPoint aleatorio o en un punto aleatorio dentro de una hitbox. Reintenta en otro sitio si cae dentro de un obstáculo.</summary>
    public bool TrySpawnRandom()
    {
        if (!CanSpawn) return false;

        int attempts = Mathf.Max(1, maxSpawnAttempts);

        // Prioridad 1: spawnPoints fijos (posición exacta)
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            for (int i = 0; i < attempts; i++)
            {
                Transform chosen = spawnPoints[Random.Range(0, spawnPoints.Length)];
                Vector3 pos = chosen != null ? chosen.position : transform.position;
                Quaternion rot = chosen != null ? chosen.rotation : Quaternion.identity;
                // Probar otro punto si este está dentro de un obstáculo
                Vector3 testPos = pos + Vector3.up * spawnHeightOffset;
                if (IsPositionBlocked(testPos)) continue;
                if (TrySpawnAt(pos, rot)) return true;
            }
            Debug.Log($"[CollectibleSpawnManager] No se encontró punto libre tras {attempts} intentos (todos dentro de obstáculos).");
            return false;
        }

        // Prioridad 2: punto aleatorio dentro del volumen de un spawner
        if (useSpawnersAsSpawnPoints && registeredSpawners.Count > 0)
        {
            for (int i = 0; i < attempts; i++)
            {
                CollectibleSpawner s = registeredSpawners[Random.Range(0, registeredSpawners.Count)];
                if (s == null) continue;
                // RequestSpawnFromSpawner ya reintenta posiciones dentro del volumen,
                // aquí variamos además de spawner para cubrir mejor el mapa.
                if (RequestSpawnFromSpawner(s)) return true;
            }
            Debug.Log($"[CollectibleSpawnManager] No se encontró punto libre en spawners tras {attempts} intentos.");
            return false;
        }

        return TrySpawnAt(transform.position, Quaternion.identity);
    }

    /// <summary>Solicitado por un CollectibleSpawner (hitbox). Genera en punto aleatorio dentro del volumen, reintentando en otro sitio si cae dentro de un obstáculo.</summary>
    public bool RequestSpawnFromSpawner(CollectibleSpawner spawner)
    {
        if (spawner == null) return false;
        if (!CanSpawn) return false;
        int attempts = Mathf.Max(1, maxSpawnAttempts);
        Quaternion rot = spawner.GetSpawnRotation();
        for (int i = 0; i < attempts; i++)
        {
            Vector3 pos = spawner.GetRandomSpawnPosition();
            // Condicional: si está dentro de una caja de colisión, generar en otro sitio
            if (IsPositionBlocked(pos)) continue;
            // Punto ya está dentro del volumen, no sumar spawnHeightOffset extra
            if (TrySpawnAtExact(pos, rot)) return true;
            else return false; // límite alcanzado o error: no seguir intentando
        }
        Debug.Log($"[CollectibleSpawnManager] Spawn de {spawner.name} descartado tras {attempts} intentos: todos dentro de obstáculos.");
        return false;
    }

    private bool TrySpawnAtExact(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            return false;
        }
        if (!CanSpawn) return false;
        if (collectiblePrefab == null && powerUpPrefab == null)
        {
            Debug.LogWarning("[CollectibleSpawnManager] Ningún prefab asignado (collectiblePrefab / powerUpPrefab).");
            return false;
        }
        // Condicional: no generar dentro de edificios ni obstáculos
        if (IsPositionBlocked(position))
        {
            Debug.Log($"[CollectibleSpawnManager] Spawn descartado en {position}: dentro de un obstáculo.");
            return false;
        }
        return SpawnChosenPrefab(position, rotation, RollIsPowerUp());
    }

    private Transform GetRandomSpawnPoint()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        if (useSpawnersAsSpawnPoints && registeredSpawners.Count > 0)
        {
            CollectibleSpawner s = registeredSpawners[Random.Range(0, registeredSpawners.Count)];
            return s != null ? s.transform : null;
        }

        return null;
    }

    private void SpawnInitialBatch()
    {
        if (!IsServer) return;

        // Reintentar si aún no hay spawners registrados pero se requieren
        if (useSpawnersAsSpawnPoints && registeredSpawners.Count == 0 && (spawnPoints == null || spawnPoints.Length == 0))
        {
            // Buscar spawners que aún no se registraron (orden de ejecución)
            foreach (CollectibleSpawner s in FindObjectsByType<CollectibleSpawner>(FindObjectsSortMode.None))
                RegisterSpawner(s);

            if (registeredSpawners.Count == 0)
            {
                Debug.LogWarning("[CollectibleSpawnManager] No hay CollectibleSpawner en escena, reintentando spawn inicial en 0.5s.");
                Invoke(nameof(SpawnInitialBatch), 0.5f);
                return;
            }
        }

        if (collectiblePrefab == null && powerUpPrefab == null)
        {
            Debug.LogError("[CollectibleSpawnManager] Ningún prefab asignado. Asigna Object.prefab en collectiblePrefab y/o powerUpPrefab -> no se puede spawnear.");
            return;
        }

        int toSpawn = initialSpawnCount > 0 ? initialSpawnCount : maxInstances;
        toSpawn = Mathf.Min(toSpawn, RemainingSlots);
        int spawned = 0;
        for (int i = 0; i < toSpawn; i++)
        {
            if (TrySpawnRandom()) spawned++;
            else break;
        }
        Debug.Log($"[CollectibleSpawnManager] Spawn inicial: {spawned}/{toSpawn} | Activos: {ActiveCount}/{maxInstances}");

        // Si faltan por límite y no se pudo (prefab null / sin spawners), reintentar
        if (RemainingSlots > 0 && spawned < toSpawn)
        {
            Invoke(nameof(SpawnInitialBatch), 0.5f);
        }

        if (autoSpawnInterval > 0f)
            nextAutoSpawnTime = Time.time + autoSpawnInterval;
    }

    // ── Utilidades ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxInstances < 1) maxInstances = 1;
        if (maxPowerUps < 0) maxPowerUps = 0;
        if (powerUpChance < 0f) powerUpChance = 0f;
        if (powerUpChance > 1f) powerUpChance = 1f;
        if (slowPowerUpChance < 0f) slowPowerUpChance = 0f;
        if (slowPowerUpChance > 1f) slowPowerUpChance = 1f;
        if (initialSpawnCount < 0) initialSpawnCount = 0;
        if (autoSpawnInterval < 0f) autoSpawnInterval = 0f;
        if (maxSpawnAttempts < 1) maxSpawnAttempts = 1;
        if (obstructionCheckHalfExtents.x < 0.01f) obstructionCheckHalfExtents.x = 0.01f;
        if (obstructionCheckHalfExtents.y < 0.01f) obstructionCheckHalfExtents.y = 0.01f;
        if (obstructionCheckHalfExtents.z < 0.01f) obstructionCheckHalfExtents.z = 0.01f;
    }
#endif
}
