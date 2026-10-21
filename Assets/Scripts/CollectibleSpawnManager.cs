using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Objeto dorado (muy raro)")]
    [Tooltip("Material dorado que se aplica como skin al objeto normal (Golden.mat). Si queda vacío se crea uno dorado en runtime.")]
    [SerializeField] private Material goldenMaterial;
    [Tooltip("Probabilidad de que un spawn sea un objeto DORADO en vez de normal (0.01 = 1%). Se sortea ANTES que el power-up.")]
    [Range(0f, 1f)]
    [SerializeField] private float goldenChance = 0.01f;
    [Tooltip("Número máximo de objetos dorados simultáneos en escena (requisito: solo uno a la vez).")]
    [SerializeField] private int maxGolden = 1;
    [Tooltip("Puntos directos al marcador que otorga el objeto dorado a quien lo recolecta (no pasa por inventario).")]
    [SerializeField] private int goldenPoints = 5;
    [Tooltip("Segundos de partida que deben transcurrir ANTES de que pueda aparecer un objeto dorado (se cuenta desde el inicio real de la partida, tras la cuenta regresiva).")]
    [SerializeField] private float goldenSpawnDelay = 20f;

    public int GoldenPoints => goldenPoints;

    [Header("Aviso UI de objeto dorado")]
    [Tooltip("Imagen que se muestra temporalmente (con fade in/out) cuando aparece un objeto dorado. Asignar la Image en un Canvas de la escena.")]
    [SerializeField] private Image goldenNotificationImage;
    [Tooltip("Segundos que la imagen permanece TOTALMENTE visible antes del fade out.")]
    [SerializeField] private float goldenNotifyDuration = 3f;
    [Tooltip("Duración del fade in de la imagen (segundos).")]
    [SerializeField] private float goldenNotifyFadeIn = 0.4f;
    [Tooltip("Duración del fade out de la imagen (segundos).")]
    [SerializeField] private float goldenNotifyFadeOut = 0.6f;
    [Tooltip("Escala inicial al comenzar la animación del aviso dorado (ej. 0.2 = 20% de su tamaño).")]
    [SerializeField] private float goldenNotifyStartScale = 0.2f;
    [Tooltip("Factor de rebote elástico (overshoot) antes de asentarse en su tamaño normal (ej. 1.15 = 115%).")]
    [SerializeField] private float goldenNotifyOvershootScale = 1.15f;

    [Header("Evento dorado (última fase de la partida)")]
    [Tooltip("Segundos restantes de partida a los que empieza el evento dorado. Ej: 20 = arranca faltando 20s para el final.")]
    [SerializeField] private float goldenEventStartRemaining = 20f;
    [Tooltip("Probabilidad de que cada spawn sea dorado mientras el evento esté activo (0.6 = 60%).")]
    [Range(0f, 1f)]
    [SerializeField] private float goldenEventChance = 0.6f;
    [Tooltip("Imagen del evento dorado. Aparece con fade al iniciar el evento, permanece un tiempo y desaparece con fade out.")]
    [SerializeField] private Image goldenEventImage;
    [Tooltip("Duración del fade in de la imagen del evento (segundos).")]
    [SerializeField] private float goldenEventFadeIn = 0.6f;
    [Tooltip("Segundos que la imagen del evento permanece TOTALMENTE visible antes del fade out.")]
    [SerializeField] private float goldenEventDuration = 3f;
    [Tooltip("Duración del fade out de la imagen del evento (segundos).")]
    [SerializeField] private float goldenEventFadeOut = 0.6f;
    [Tooltip("Escala inicial al comenzar la animación del evento dorado (ej. 0.2 = 20% de su tamaño).")]
    [SerializeField] private float goldenEventStartScale = 0.2f;
    [Tooltip("Factor de rebote elástico (overshoot) del evento antes de asentarse en su tamaño normal (ej. 1.15 = 115%).")]
    [SerializeField] private float goldenEventOvershootScale = 1.15f;

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
    [Tooltip("Capas cuyos TRIGGERS también bloquean el spawn (ej. volumen invisible de zona prohibida con IsTrigger activado). Los triggers de los CollectibleSpawner siempre se ignoran.")]
    [SerializeField] private LayerMask blockerTriggerLayers = 1 << 6; // Capa 6 = Obstacles (ver TagManager.asset)

    private readonly HashSet<InteractableCube> trackedInstances = new HashSet<InteractableCube>();
    private readonly HashSet<PowerUpCube> trackedPowerUps = new HashSet<PowerUpCube>();
    private readonly HashSet<InteractableCube> trackedGolden = new HashSet<InteractableCube>();
    private readonly List<CollectibleSpawner> registeredSpawners = new List<CollectibleSpawner>();

    private float nextAutoSpawnTime;

    // ── Estado del aviso UI dorado ────────────────────────────────────────
    private Coroutine goldenNotificationRoutine;
    private CanvasGroup goldenNotificationGroup;
    private Vector3 goldenNotificationBaseScale = Vector3.one;
    private bool goldenNotificationBaseScaleCached;

    private Coroutine goldenEventFadeRoutine;
    private CanvasGroup goldenEventGroup;
    private Vector3 goldenEventBaseScale = Vector3.one;
    private bool goldenEventBaseScaleCached;

    private bool goldenEventActive;
    // Fallback cuando no hay MatchTimerManager (tutorial offline)
    private float fallbackMatchStartTime;
    private bool fallbackMatchClockStarted;

    /// <summary>True mientras dure el evento dorado (últimos goldenEventStartRemaining segundos de partida).</summary>
    public bool IsGoldenEventActive => goldenEventActive;

    /// <summary>True cuando ya transcurrieron <see cref="goldenSpawnDelay"/> segundos de partida real (tras el 3-2-1-GO).</summary>
    public bool IsGoldenUnlocked
    {
        get
        {
            MatchTimerManager timer = MatchTimerManager.Instance;
            if (timer != null)
            {
                // La partida corre en serio desde que el MatchTimer arranca (después de la cuenta regresiva)
                if (!timer.IsRunning) return false;
                float elapsed = timer.Duration - timer.RemainingTime;
                return elapsed >= goldenSpawnDelay;
            }

            // Fallback offline (tutorial / sin MatchTimerManager): medir desde el arranque del manager
            if (!fallbackMatchClockStarted)
            {
                fallbackMatchClockStarted = true;
                fallbackMatchStartTime = Time.time;
            }
            return Time.time - fallbackMatchStartTime >= goldenSpawnDelay;
        }
    }

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

    public int ActiveGoldenCount
    {
        get
        {
            trackedGolden.RemoveWhere(c => c == null);
            return trackedGolden.Count;
        }
    }

    /// <summary>Solo puede haber maxGolden dorados a la vez; durante el evento dorado NO hay límite.</summary>
    public bool CanSpawnGolden => CanSpawn && (IsGoldenEventActive || ActiveGoldenCount < maxGolden);

    /// <summary>Material dorado para la skin. Si no se asignó en el Inspector, crea uno dorado URP en runtime.</summary>
    public Material GoldenMaterial
    {
        get
        {
            if (goldenMaterial != null) return goldenMaterial;
            goldenMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "GoldenRuntime"
            };
            goldenMaterial.SetColor("_BaseColor", new Color(1f, 0.82f, 0.01f, 1f));
            goldenMaterial.SetFloat("_Metallic", 1f);
            goldenMaterial.SetFloat("_Smoothness", 0.85f);
            return goldenMaterial;
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

        PrepareGoldenNotification();
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

        HideGoldenEventImage();
        if (goldenNotificationRoutine != null)
        {
            StopCoroutine(goldenNotificationRoutine);
            goldenNotificationRoutine = null;
        }
        if (goldenNotificationImage != null)
        {
            if (goldenNotificationBaseScaleCached)
                goldenNotificationImage.rectTransform.localScale = goldenNotificationBaseScale;
            if (goldenNotificationGroup != null)
                goldenNotificationGroup.alpha = 0f;
            goldenNotificationImage.gameObject.SetActive(false);
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
        UpdateGoldenEventState();

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
        if (cube.IsGolden) trackedGolden.Add(cube);
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
        trackedGolden.Remove(cube);
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
    /// de las <see cref="obstacleLayers"/>, o dentro de un Trigger de las
    /// <see cref="blockerTriggerLayers"/> (volumen invisible de zona prohibida).
    /// La primera pasada usa <see cref="QueryTriggerInteraction.Ignore"/> para que
    /// las hitbox triggers de los spawners no bloqueen el spawn; la segunda pasa
    /// explorando triggers solo de <see cref="blockerTriggerLayers"/>, excluyendo
    /// cualquier collider que pertenezca a un <see cref="CollectibleSpawner"/>.
    /// </summary>
    public bool IsPositionBlocked(Vector3 position)
    {
        Collider[] hits = Physics.OverlapBox(
            position,
            obstructionCheckHalfExtents,
            Quaternion.identity,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0) return true;

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

    /// <summary>
    /// Decide si este spawn debe ser un objeto dorado.
    /// NORMAL (antes del evento): muy raro (<see cref="goldenChance"/>) y requiere
    /// el delay de partida (<see cref="goldenSpawnDelay"/>) y el límite <see cref="maxGolden"/>.
    /// EVENTO (últimos <see cref="goldenEventStartRemaining"/>s): probabilidad <see cref="goldenEventChance"/>
    /// y SIN límite de dorados simultáneos. Se sortea ANTES que el power-up.
    /// </summary>
    private bool RollIsGolden()
    {
        if (collectiblePrefab == null) return false;

        float chance;
        if (IsGoldenEventActive)
        {
            // Condicional evento: 60% de dorado y sin límite de cantidad
            chance = goldenEventChance;
        }
        else
        {
            // Condicional: no aparece comida dorada hasta pasados goldenSpawnDelay segundos de partida
            if (!IsGoldenUnlocked) return false;
            chance = goldenChance;
        }

        if (chance <= 0f) return false;
        if (!CanSpawnGolden) return false;
        return UnityEngine.Random.value < chance;
    }

    /// <summary>Sortea el tipo de spawn: primero dorado (muy raro), después power-up; el resto es normal.</summary>
    private void RollSpawnType(out bool isPowerUp, out bool isGolden)
    {
        isGolden = RollIsGolden();
        isPowerUp = !isGolden && RollIsPowerUp();
    }

    /// <summary>Instancia el prefab elegido (dorado / power-up / normal) y lo registra. Asume límite y bloqueo ya validados.</summary>
    private bool SpawnChosenPrefab(Vector3 position, Quaternion rotation, bool isPowerUp, bool isGolden)
    {
        NetworkObject prefabToSpawn = isPowerUp ? powerUpPrefab : collectiblePrefab;
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("[CollectibleSpawnManager] Prefab no asignado (revisa collectiblePrefab / powerUpPrefab).");
            return false;
        }

        NetworkObject instance = Instantiate(prefabToSpawn, position, rotation);
        // Sorteo del modelo visible antes del Spawn para que el índice viaje replicado desde el inicio.
        if (instance != null)
        {
            InteractableCube cubePre = instance.GetComponent<InteractableCube>();
            if (cubePre != null)
            {
                // El dorado es un objeto normal con skin cambiada: mismo sorteo de variante.
                if (IsNetworkServer && !isPowerUp)
                    cubePre.RollVariantServerSide();
                if (isGolden)
                    cubePre.ApplyGoldenServerSide();
            }
        }
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
            if (cube != null && cube.IsGolden && !trackedGolden.Contains(cube))
                trackedGolden.Add(cube);
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

        RollSpawnType(out bool isPowerUp, out bool isGolden);
        return SpawnChosenPrefab(position, rotation, isPowerUp, isGolden);
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
        RollSpawnType(out bool isPowerUp, out bool isGolden);
        return SpawnChosenPrefab(position, rotation, isPowerUp, isGolden);
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

    // ── Evento dorado (última fase de la partida) ──────────────────────────

    /// <summary>
    /// Refresca el estado del evento dorado cada frame. La partida se mide con
    /// MatchTimerManager (replicado a todos los clientes), así que el evento
    /// arranca/termina de forma consistente en servidor y clientes.
    /// </summary>
    private void UpdateGoldenEventState()
    {
        bool active = false;
        MatchTimerManager timer = MatchTimerManager.Instance;
        if (timer != null && timer.IsRunning)
        {
            float remaining = timer.RemainingTime;
            active = 0f < remaining && remaining <= goldenEventStartRemaining;
        }

        if (active == goldenEventActive) return;
        goldenEventActive = active;
        if (active)
        {
            ShowGoldenEventImage();
            ConvertHalfObjectsToGolden();
        }
        else HideGoldenEventImage();
    }

    /// <summary>
    /// Al iniciar el evento, la MITAD de los objetos normales ya presentes en el mapa
    /// se convierten en dorados al azar (solo servidor; el flag dorado se replica vía
    /// NetworkVariable y cada cliente aplica la skin con ApplyGoldenSkin).
    /// Los nuevos spawns durante el evento salen por <see cref="goldenEventChance"/>.
    /// </summary>
    private void ConvertHalfObjectsToGolden()
    {
        if (!IsServer) return;

        // Objetos normales actuales (descarta destruidos, dorados previos y ya recolectados)
        trackedInstances.RemoveWhere(c => c == null);
        List<InteractableCube> normalCubes = new List<InteractableCube>();
        foreach (InteractableCube cube in trackedInstances)
        {
            if (cube == null || cube.IsGolden || cube.IsCollected) continue;
            normalCubes.Add(cube);
        }
        if (normalCubes.Count == 0)
        {
            Debug.Log("[CollectibleSpawnManager] Evento dorado iniciado sin objetos normales que convertir.");
            return;
        }

        int toConvert = Mathf.Min(normalCubes.Count, Mathf.Max(1, normalCubes.Count / 2));
        for (int i = 0; i < toConvert; i++)
        {
            int pick = UnityEngine.Random.Range(0, normalCubes.Count);
            InteractableCube target = normalCubes[pick];
            normalCubes.RemoveAt(pick);
            if (target == null || target.IsCollected) continue;
            target.ApplyGoldenServerSide();
            if (target.IsGolden && !trackedGolden.Contains(target))
                trackedGolden.Add(target);
        }
        Debug.Log($"[CollectibleSpawnManager] Evento dorado: convertidos {toConvert} objetos normal(es) en dorados.");
    }

    /// <summary>Muestra temporalmente la imagen del evento con fade in, espera y fade out (igual que el aviso de dorado).</summary>
    private void ShowGoldenEventImage()
    {
        if (goldenEventImage == null) return;
        if (!isActiveAndEnabled) return;
        CacheBaseScales();
        if (goldenEventFadeRoutine != null)
        {
            StopCoroutine(goldenEventFadeRoutine);
            if (goldenEventBaseScaleCached)
                goldenEventImage.rectTransform.localScale = goldenEventBaseScale;
        }
        goldenEventFadeRoutine = StartCoroutine(GoldenEventFadeRoutine());
    }

    private System.Collections.IEnumerator GoldenEventFadeRoutine()
    {
        CanvasGroup group = EnsureCanvasGroup(goldenEventImage, ref goldenEventGroup);
        CacheBaseScales();
        goldenEventImage.gameObject.SetActive(true);
        goldenEventGroup.interactable = false;
        goldenEventGroup.blocksRaycasts = false;

        float fadeIn = Mathf.Max(0f, goldenEventFadeIn);
        float hold = Mathf.Max(0f, goldenEventDuration);
        float fadeOut = Mathf.Max(0f, goldenEventFadeOut);
        RectTransform rt = goldenEventImage.rectTransform;

        if (fadeIn > 0f)
        {
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(t / fadeIn);
                group.alpha = progress;
                float scaleMul = EvaluatePopScale(progress, goldenEventStartScale, goldenEventOvershootScale);
                rt.localScale = goldenEventBaseScale * scaleMul;
                yield return null;
            }
        }
        group.alpha = 1f;
        rt.localScale = goldenEventBaseScale;

        if (hold > 0f)
            yield return new WaitForSeconds(hold);

        if (fadeOut > 0f)
        {
            float t = 0f;
            while (t < fadeOut)
            {
                t += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(t / fadeOut);
                group.alpha = 1f - progress;
                float exitScaleMul = Mathf.Lerp(1f, 0.85f, progress);
                rt.localScale = goldenEventBaseScale * exitScaleMul;
                yield return null;
            }
        }
        group.alpha = 0f;
        rt.localScale = goldenEventBaseScale;
        goldenEventImage.gameObject.SetActive(false);
        goldenEventFadeRoutine = null;
    }

    /// <summary>Oculta la imagen del evento y cancela su fade en curso (p.ej. si el evento se desactiva al volver al Lobby).</summary>
    private void HideGoldenEventImage()
    {
        if (goldenEventFadeRoutine != null)
        {
            StopCoroutine(goldenEventFadeRoutine);
            goldenEventFadeRoutine = null;
        }
        if (goldenEventImage != null)
        {
            if (goldenEventBaseScaleCached)
                goldenEventImage.rectTransform.localScale = goldenEventBaseScale;
            goldenEventImage.gameObject.SetActive(false);
        }
        if (goldenEventGroup != null)
        {
            goldenEventGroup.alpha = 0f;
        }
    }

    // ── Utilidades ─────────────────────────────────────────────────────────

    /// <summary>
    /// Guarda la escala local configurada originalmente en el RectTransform de ambas imágenes.
    /// </summary>
    private void CacheBaseScales()
    {
        if (goldenNotificationImage != null && !goldenNotificationBaseScaleCached)
        {
            Vector3 s = goldenNotificationImage.rectTransform.localScale;
            goldenNotificationBaseScale = (s != Vector3.zero) ? s : Vector3.one;
            goldenNotificationBaseScaleCached = true;
        }
        if (goldenEventImage != null && !goldenEventBaseScaleCached)
        {
            Vector3 s = goldenEventImage.rectTransform.localScale;
            goldenEventBaseScale = (s != Vector3.zero) ? s : Vector3.one;
            goldenEventBaseScaleCached = true;
        }
    }

    /// <summary>
    /// Calcula la escala dinámica tipo 'pop / overshoot' (empieza pequeño, sobrepasa ligeramente y se asienta suavemente).
    /// </summary>
    private static float EvaluatePopScale(float progress, float startScale, float overshootScale)
    {
        progress = Mathf.Clamp01(progress);
        if (progress < 0.7f)
        {
            float subT = progress / 0.7f;
            float smooth = Mathf.Sin(subT * Mathf.PI * 0.5f);
            return Mathf.Lerp(startScale, overshootScale, smooth);
        }
        else
        {
            float subT = (progress - 0.7f) / 0.3f;
            float smooth = Mathf.Sin(subT * Mathf.PI * 0.5f);
            return Mathf.Lerp(overshootScale, 1f, smooth);
        }
    }

    /// <summary>
    /// Prepara los CanvasGroup de ambas imágenes (aviso y evento): alpha 0 y sin bloquear raycasts.
    /// Crea el CanvasGroup si la Image no lo trae (todo por código, sin tocar la escena).
    /// </summary>
    private void PrepareGoldenNotification()
    {
        CacheBaseScales();
        if (goldenNotificationImage != null)
        {
            CanvasGroup group = EnsureCanvasGroup(goldenNotificationImage, ref goldenNotificationGroup);
            group.alpha = 0f;
            goldenNotificationImage.rectTransform.localScale = goldenNotificationBaseScale;
            goldenNotificationImage.gameObject.SetActive(false);
        }
        if (goldenEventImage != null)
        {
            CanvasGroup group = EnsureCanvasGroup(goldenEventImage, ref goldenEventGroup);
            group.alpha = 0f;
            goldenEventImage.rectTransform.localScale = goldenEventBaseScale;
            goldenEventImage.gameObject.SetActive(false);
        }
    }

    /// <summary>Devuelve el CanvasGroup de la Image, creándolo si falta, y desactiva su interacción/raycast.</summary>
    private CanvasGroup EnsureCanvasGroup(Image image, ref CanvasGroup cached)
    {
        if (cached == null)
        {
            cached = image.GetComponent<CanvasGroup>();
            if (cached == null)
                cached = image.gameObject.AddComponent<CanvasGroup>();
            cached.interactable = false;
            cached.blocksRaycasts = false;
        }
        return cached;
    }

    /// <summary>
    /// Activa temporalmente la imagen de aviso con fade in, escala pop dinámica, espera y fade out.
    /// Lo llaman los InteractableCubes dorados desde OnNetworkSpawn/local (funciona en servidor, clientes y offline).
    /// </summary>
    public void NotifyGoldenAppeared()
    {
        if (goldenNotificationImage == null) return;
        // Durante el evento dorado, los objetos dorados NO disparan esta notificación
        if (IsGoldenEventActive) return;
        if (!isActiveAndEnabled) return;
        CacheBaseScales();
        EnsureCanvasGroup(goldenNotificationImage, ref goldenNotificationGroup);
        if (goldenNotificationRoutine != null)
        {
            StopCoroutine(goldenNotificationRoutine);
            if (goldenNotificationBaseScaleCached)
                goldenNotificationImage.rectTransform.localScale = goldenNotificationBaseScale;
        }
        goldenNotificationRoutine = StartCoroutine(GoldenNotificationFadeRoutine());
    }

    private IEnumerator GoldenNotificationFadeRoutine()
    {
        EnsureCanvasGroup(goldenNotificationImage, ref goldenNotificationGroup);
        CacheBaseScales();
        goldenNotificationImage.gameObject.SetActive(true);
        goldenNotificationGroup.interactable = false;
        goldenNotificationGroup.blocksRaycasts = false;

        float fadeIn = Mathf.Max(0f, goldenNotifyFadeIn);
        float hold = Mathf.Max(0f, goldenNotifyDuration);
        float fadeOut = Mathf.Max(0f, goldenNotifyFadeOut);
        RectTransform rt = goldenNotificationImage.rectTransform;

        if (fadeIn > 0f)
        {
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(t / fadeIn);
                goldenNotificationGroup.alpha = progress;
                float scaleMul = EvaluatePopScale(progress, goldenNotifyStartScale, goldenNotifyOvershootScale);
                rt.localScale = goldenNotificationBaseScale * scaleMul;
                yield return null;
            }
        }
        goldenNotificationGroup.alpha = 1f;
        rt.localScale = goldenNotificationBaseScale;

        if (hold > 0f)
            yield return new WaitForSeconds(hold);

        if (fadeOut > 0f)
        {
            float t = 0f;
            while (t < fadeOut)
            {
                t += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(t / fadeOut);
                goldenNotificationGroup.alpha = 1f - progress;
                float exitScaleMul = Mathf.Lerp(1f, 0.85f, progress);
                rt.localScale = goldenNotificationBaseScale * exitScaleMul;
                yield return null;
            }
        }
        goldenNotificationGroup.alpha = 0f;
        rt.localScale = goldenNotificationBaseScale;
        goldenNotificationImage.gameObject.SetActive(false);
        goldenNotificationRoutine = null;
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
        if (goldenChance < 0f) goldenChance = 0f;
        if (goldenChance > 1f) goldenChance = 1f;
        if (maxGolden < 0) maxGolden = 0;
        if (goldenPoints < 0) goldenPoints = 0;
        if (goldenSpawnDelay < 0f) goldenSpawnDelay = 0f;
        if (goldenNotifyDuration < 0f) goldenNotifyDuration = 0f;
        if (goldenNotifyFadeIn < 0f) goldenNotifyFadeIn = 0f;
        if (goldenNotifyFadeOut < 0f) goldenNotifyFadeOut = 0f;
        if (goldenNotifyStartScale < 0f) goldenNotifyStartScale = 0f;
        if (goldenNotifyOvershootScale < 1f) goldenNotifyOvershootScale = 1f;
        if (goldenEventStartRemaining < 0f) goldenEventStartRemaining = 0f;
        if (goldenEventFadeIn < 0f) goldenEventFadeIn = 0f;
        if (goldenEventDuration < 0f) goldenEventDuration = 0f;
        if (goldenEventFadeOut < 0f) goldenEventFadeOut = 0f;
        if (goldenEventStartScale < 0f) goldenEventStartScale = 0f;
        if (goldenEventOvershootScale < 1f) goldenEventOvershootScale = 1f;
        if (initialSpawnCount < 0) initialSpawnCount = 0;
        if (autoSpawnInterval < 0f) autoSpawnInterval = 0f;
        if (maxSpawnAttempts < 1) maxSpawnAttempts = 1;
        if (obstructionCheckHalfExtents.x < 0.01f) obstructionCheckHalfExtents.x = 0.01f;
        if (obstructionCheckHalfExtents.y < 0.01f) obstructionCheckHalfExtents.y = 0.01f;
        if (obstructionCheckHalfExtents.z < 0.01f) obstructionCheckHalfExtents.z = 0.01f;
    }
#endif
}
