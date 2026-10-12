using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent diagonal wipe transition made of 2 panels (images).
/// Place ONE copy of the "Trancision" prefab in the initial scene only
/// (e.g. ConnectionScene). It survives with DontDestroyOnLoad and covers
/// every later scene change, so no per-scene copy is needed.
/// Flow: Close (panels slide together diagonally, covering the screen)
/// -> load scene (covered, no flash) -> Open (panels slide apart diagonally).
/// </summary>
public class DiagonalSceneTransition : MonoBehaviour
{
    public static DiagonalSceneTransition Instance { get; private set; }

    [Header("Panels (Obj1 containers)")]
    [Tooltip("First panel container (Obj1). Its Image child is the diagonal bar.")]
    [SerializeField] private RectTransform panelA;
    [Tooltip("Second panel container (Obj1 (1)). Moves in the opposite diagonal direction.")]
    [SerializeField] private RectTransform panelB;

    [Header("Positions")]
    [Tooltip("Centered (together) position for panel A. Both panels together cover the screen.")]
    [SerializeField] private Vector2 closedPositionA = Vector2.zero;
    [Tooltip("Centered (together) position for panel B.")]
    [SerializeField] private Vector2 closedPositionB = Vector2.zero;
    [Tooltip("Diagonal offset applied to panel A when open (e.g. up-left).")]
    [SerializeField] private Vector2 openOffsetA = new Vector2(-1800f, 1800f);
    [Tooltip("Diagonal offset applied to panel B when open (e.g. down-right, opposite of A).")]
    [SerializeField] private Vector2 openOffsetB = new Vector2(1800f, -1800f);

    [Header("Timing")]
    [SerializeField] private float openDuration = 0.8f;
    [SerializeField] private float closeDuration = 0.8f;
    [SerializeField] private float openDelay = 0.15f;
    [Tooltip("Hold delay once fully closed (squared) before loading the next scene.")]
    [SerializeField] private float closedHoldDelay = 0.3f;

    [Header("Per-Scene Cover")]
    [Tooltip("Extra covered wait after loading these scenes (on top of Open Delay). Use it to hide spawn/setup acomodo, e.g. MainScene while players teleport to spawn points.")]
    [SerializeField] private System.Collections.Generic.List<SceneCoverExtension> extendedCoverScenes = new System.Collections.Generic.List<SceneCoverExtension>
    {
        new SceneCoverExtension { sceneName = "Lobby", extraDelay = 0.5f },
    };

    [Header("Behaviour")]
    [Tooltip("If true, the first scene starts closed and automatically opens.")]
    [SerializeField] private bool playOpenOnStart = true;
    [Tooltip("If true, scenes loaded directly (bypassing TransitionTo) still start covered and then open.")]
    [SerializeField] private bool coverExternalLoads = true;
    [Tooltip("If true, the Canvas is disabled once fully open so it never blocks input.")]
    [SerializeField] private bool disableCanvasWhenOpen = true;
    [Tooltip("Canvas sorting order used while transitioning (kept on top of all UI).")]
    [SerializeField] private int transitionSortingOrder = 100;

    [Header("Transition Sound")]
    [Tooltip("Sonido al cerrar (cubrir pantalla) en cada cambio de escena. Se escucha en 2D y sobrevive a la carga.")]
    [SerializeField] private AudioClip closeSound;
    [Tooltip("Sonido al abrir (revelar la nueva escena). Si esta vacio, no suena nada al abrir.")]
    [SerializeField] private AudioClip openSound;
    [Tooltip("Volumen del sonido de transicion (0 a 1).")]
    [Range(0f, 1f)]
    [SerializeField] private float transitionSoundVolume = 0.8f;

    [Header("Network Pre-Cover")]
    [Tooltip("Margen extra (segundos) que el servidor espera tras su propio cierre para dar tiempo a que los clientes terminen su animacion de cierre antes de cargar la escena.")]
    [SerializeField] private float clientCoverMargin = 0.6f;
    [Tooltip("Escenas de ORIGEN cuyas cargas de red entrantes no muestran transicion (corte directo). Vacia = todos los saltos muestran transicion. Ej: si el boton de Conectar ya cubre la pantalla, no hace falta omitir nada.")]
    [SerializeField] private System.Collections.Generic.List<string> noCoverFromScenes = new System.Collections.Generic.List<string>();

    public bool IsTransitioning { get; private set; }
    public bool IsClosed { get; private set; }

    /// <summary>True once the open animation fully finished (scene revealed, input free).</summary>
    public bool IsOpen => !IsClosed && !IsTransitioning;

    /// <summary>Invoked when the open animation fully finishes.</summary>
    public static event Action OnTransitionOpened;

    private Canvas canvas;
    private Coroutine runningRoutine;
    private bool pendingOpenAfterLoad;
    private bool skipCoverOnce;
    private int openHoldCount;
    private AudioSource sfxSource;
    private bool netcodeHooksAttached;
    private const string PreCloseMessageName = "DiagTrans_PreClose";

    private void Awake()
    {
        // Single persistent instance: destroy any duplicate (e.g. prefab left in another scene).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = transitionSortingOrder;
        }

        FixRootRect();
        AutoFindPanelsIfNeeded();
        EnsureSfxSource();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryAttachNetcodeHooks();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        DetachNetcodeHooks();
    }

    private void Start()
    {
        TryAttachNetcodeHooks();
        if (playOpenOnStart)
        {
            SetClosedImmediate();
            Open();
        }
    }

    private void OnDestroy()
    {
        DetachNetcodeHooks();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Engancha los eventos de carga de escena de Netcode (si hay NetworkManager).
    /// Idempotente: el NetworkManager puede crearse despues que la transicion,
    /// asi que se reintenta en Start, OnEnable y cada OnSceneLoaded.
    /// </summary>
    private void TryAttachNetcodeHooks()
    {
        if (netcodeHooksAttached) return;
        if (Instance != null && Instance != this) return;
        NetworkManager netManager = NetworkManager.Singleton;
        if (netManager == null || netManager.SceneManager == null) return;
        netManager.SceneManager.OnLoad += HandleNetcodeLoadStarted;
        netManager.SceneManager.OnSynchronize += HandleNetcodeSynchronize;
        netManager.CustomMessagingManager.RegisterNamedMessageHandler(PreCloseMessageName, HandlePreCloseMessage);
        netcodeHooksAttached = true;
    }

    private void DetachNetcodeHooks()
    {
        if (!netcodeHooksAttached) return;
        netcodeHooksAttached = false;
        NetworkManager netManager = NetworkManager.Singleton;
        if (netManager == null) return;
        if (netManager.SceneManager != null)
        {
            netManager.SceneManager.OnLoad -= HandleNetcodeLoadStarted;
            netManager.SceneManager.OnSynchronize -= HandleNetcodeSynchronize;
        }
        if (netManager.CustomMessagingManager != null)
        {
            netManager.CustomMessagingManager.UnregisterNamedMessageHandler(PreCloseMessageName);
        }
    }

    /// <summary>
    /// Pre-cubre la pantalla en los clientes cuando el servidor inicia una carga
    /// de escena por red (Lobby -&gt; MainScene, etc.). Solo el servidor anima el
    /// cierre via CloseAndLoadScene; los clientes reciben la carga sincronizada
    /// sin aviso previo y antes solo veian el corte seco + apertura. Al cubrir
    /// aqui, el OnSceneLoaded posterior revela con la apertura normal.
    /// </summary>
    private void HandleNetcodeLoadStarted(ulong clientId, string sceneName, LoadSceneMode loadSceneMode, AsyncOperation asyncOperation)
    {
        if (Instance != null && Instance != this) return;
        if (loadSceneMode != LoadSceneMode.Single) return;
        // El servidor ya se cubrio con su propia animacion de cierre.
        if (pendingOpenAfterLoad) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return;
        if (TrySkipCoverFromHere()) return;
        if (IsClosed) return;
        SetClosedImmediate();
        PlayCloseSound();
    }

    /// <summary>
    /// Pre-cubre cuando un cliente recien conectado sincroniza la escena actual
    /// (ej. entrar con codigo y aparecer directo en el Lobby del host).
    /// </summary>
    private void HandleNetcodeSynchronize(ulong clientId)
    {
        if (Instance != null && Instance != this) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return;
        if (NetworkManager.Singleton != null && clientId != NetworkManager.Singleton.LocalClientId) return;
        if (TrySkipCoverFromHere()) return;
        if (IsClosed) return;
        SetClosedImmediate();
        PlayCloseSound();
    }

    /// <summary>
    /// Si la escena activa (origen del salto de red) esta en noCoverFromScenes,
    /// desactiva la transicion para este salto: garantiza pantalla abierta, sin
    /// sonido, y marca skipCoverOnce para que el OnSceneLoaded posterior tampoco
    /// cubra. Retorna true cuando se omitio.
    /// </summary>
    private bool TrySkipCoverFromHere()
    {
        if (!IsSkipSourceScene(SceneManager.GetActiveScene().name)) return false;
        skipCoverOnce = true;
        SetOpenImmediate();
        return true;
    }

    private bool IsSkipSourceScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || noCoverFromScenes == null) return false;
        foreach (string entry in noCoverFromScenes)
        {
            if (!string.IsNullOrEmpty(entry)
                && string.Equals(entry.Trim(), sceneName.Trim(), System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Aviso previo del servidor: un cambio de escena con animacion va a empezar.
    /// Los clientes reproducen su propia animacion de cierre (wipe diagonal +
    /// sonido) para que vean lo mismo que el host en vez del corte seco.
    /// El servidor no necesita este mensaje (el maneja su propio cierre).
    /// </summary>
    private void HandlePreCloseMessage(ulong senderClientId, FastBufferReader messagePayload)
    {
        if (Instance != null && Instance != this) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return;
        if (IsSkipSourceScene(SceneManager.GetActiveScene().name)) return;
        if (IsClosed || IsTransitioning) return;
        Close();
    }

    /// <summary>
    /// El servidor avisa a todos los clientes de que empieza un cambio de escena
    /// animado, para que cubran su pantalla en paralelo. Sin red o sin ser
    /// servidor, no hace nada.
    /// </summary>
    private void BroadcastPreClose()
    {
        NetworkManager netManager = NetworkManager.Singleton;
        if (netManager == null || !netManager.IsListening || !netManager.IsServer) return;
        if (netManager.CustomMessagingManager == null) return;
        FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        try
        {
            writer.WriteByteSafe(0);
            netManager.CustomMessagingManager.SendNamedMessageToAll(
                PreCloseMessageName, writer, NetworkDelivery.ReliableSequenced);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DiagonalSceneTransition] No se pudo avisar el pre-cierre a clientes: " + e.Message);
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// The prefab was saved with root scale 0 and zero-size rect, which hides it.
    /// Force fullscreen overlay so the transition is always visible.
    /// </summary>
    private void FixRootRect()
    {
        transform.localScale = Vector3.one;

        RectTransform root = transform as RectTransform;
        if (root != null)
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Tries to locate the two Obj1 containers when they were not assigned in the Inspector.
    /// </summary>
    private void AutoFindPanelsIfNeeded()
    {
        if (panelA != null && panelB != null) return;

        RectTransform[] children = GetComponentsInChildren<RectTransform>(true);
        System.Collections.Generic.List<RectTransform> candidates = new System.Collections.Generic.List<RectTransform>();
        foreach (RectTransform child in children)
        {
            if (child == transform) continue;
            // Obj1 containers are the direct children of the root.
            if (child.parent == transform)
            {
                candidates.Add(child);
            }
        }

        if (panelA == null && candidates.Count > 0) panelA = candidates[0];
        if (panelB == null && candidates.Count > 1) panelB = candidates[1];

        if (panelA == null || panelB == null)
        {
            Debug.LogWarning("[DiagonalSceneTransition] Assign Panel A and Panel B (the two Obj1 containers) in the Inspector.");
        }
    }

    /// <summary>
    /// Crea el AudioSource 2D persistente para el sonido de transicion.
    /// Al estar en el mismo GameObject con DontDestroyOnLoad, el sonido
    /// no se corta cuando la escena se descarga (a diferencia de un
    /// OneShot temporal creado en la escena anterior).
    /// </summary>
    private void EnsureSfxSource()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
        }
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.volume = transitionSoundVolume;
    }

    private void PlayCloseSound()
    {
        PlayTransitionClip(closeSound);
    }

    private void PlayOpenSound()
    {
        PlayTransitionClip(openSound);
    }

    private void PlayTransitionClip(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }
        if (sfxSource == null)
        {
            EnsureSfxSource();
        }
        if (sfxSource == null)
        {
            return;
        }
        sfxSource.volume = Mathf.Clamp01(transitionSoundVolume);
        sfxSource.PlayOneShot(clip);
    }

    /// <summary>
    /// Cambia el sonido de cierre por codigo (util si el clip se carga en runtime).
    /// </summary>
    public void SetCloseSound(AudioClip clip)
    {
        closeSound = clip;
    }

    /// <summary>
    /// Cambia el sonido de apertura por codigo.
    /// </summary>
    public void SetOpenSound(AudioClip clip)
    {
        openSound = clip;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        if (Instance != this) return;

        // El NetworkManager puede haberse creado despues que la transicion.
        TryAttachNetcodeHooks();

        // Salto omitido (origen en noCoverFromScenes, ej. ClientConnectScene):
        // corte directo sin cubrir ni animar.
        if (skipCoverOnce)
        {
            skipCoverOnce = false;
            SetOpenImmediate();
            return;
        }

        if (pendingOpenAfterLoad)
        {
            // Load requested via CloseAndLoadScene: we are already closed (covered).
            pendingOpenAfterLoad = false;
            Open();
        }
        else if (coverExternalLoads)
        {
            // Scene loaded directly without transition: snap covered, then open.
            SetClosedImmediate();
            Open();
        }
    }

    /// <summary>
    /// Instantly places both panels together (closed, covering the scene).
    /// </summary>
    public void SetClosedImmediate()
    {
        StopRunning();
        SetCanvasVisible(true);
        if (panelA != null) panelA.anchoredPosition = closedPositionA;
        if (panelB != null) panelB.anchoredPosition = closedPositionB;
        IsClosed = true;
    }

    /// <summary>
    /// Instantly moves both panels to their open (off-screen diagonal) positions.
    /// </summary>
    public void SetOpenImmediate()
    {
        StopRunning();
        if (panelA != null) panelA.anchoredPosition = closedPositionA + openOffsetA;
        if (panelB != null) panelB.anchoredPosition = closedPositionB + openOffsetB;
        IsClosed = false;
        if (disableCanvasWhenOpen) SetCanvasVisible(false);
    }

    /// <summary>
    /// Slides both panels apart diagonally to reveal the scene.
    /// </summary>
    public void Open(Action onComplete = null)
    {
        StopRunning();
        runningRoutine = StartCoroutine(OpenRoutine(onComplete));
    }

    /// <summary>
    /// Slides both panels back together diagonally to cover the scene.
    /// </summary>
    public void Close(Action onComplete = null)
    {
        StopRunning();
        runningRoutine = StartCoroutine(CloseRoutine(onComplete));
    }

    /// <summary>
    /// Closes (covers the screen with a diagonal animation), loads the scene
    /// while covered, and opens again in the new scene.
    /// Uses Netcode scene load when acting as server, otherwise regular SceneManager.
    /// </summary>
    public void CloseAndLoadScene(string sceneName)
    {
        if (IsTransitioning)
        {
            Debug.LogWarning("[DiagonalSceneTransition] Transition already in progress, ignoring request.");
            return;
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[DiagonalSceneTransition] Empty scene name, ignoring request.");
            return;
        }

        runningRoutine = StartCoroutine(CloseThenLoadRoutine(sceneName));
    }

    /// <summary>
    /// Retains the transition closed: a pending Open waits until every hold is
    /// released. Use it to hide async scene setup (e.g. lobby player layout)
    /// instead of guessing fixed delays. Always paired with ReleaseOpen.
    /// </summary>
    public void HoldOpen()
    {
        openHoldCount++;
    }

    /// <summary>
    /// Releases one HoldOpen. When no holds remain, a waiting Open proceeds.
    /// </summary>
    public void ReleaseOpen()
    {
        openHoldCount = Mathf.Max(0, openHoldCount - 1);
    }

    /// <summary>Static HoldOpen that safely no-ops when there is no instance.</summary>
    public static void Hold()
    {
        if (Instance != null) Instance.HoldOpen();
    }

    /// <summary>Static ReleaseOpen that safely no-ops when there is no instance.</summary>
    public static void Release()
    {
        if (Instance != null) Instance.ReleaseOpen();
    }

    /// <summary>
    /// Static shortcut: transition to another scene from anywhere (e.g. UI buttons).
    /// Handles close animation + hold + load + reopen internally, so buttons
    /// don't need per-button transition setup. Falls back to an immediate
    /// (netcode-aware) load if no transition instance exists.
    /// </summary>
    public static void TransitionTo(string sceneName)
    {
        if (!TryTransitionTo(sceneName))
        {
            Debug.LogWarning("[DiagonalSceneTransition] No instance in scene, loading directly: " + sceneName);
            LoadDirect(sceneName);
        }
    }

    /// <summary>
    /// Starts a transitioned load if a transition instance exists.
    /// Returns true when the transition took over (scene loads after close + hold),
    /// false when the caller must load directly.
    /// </summary>
    public static bool TryTransitionTo(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || Instance == null) return false;
        Instance.CloseAndLoadScene(sceneName);
        return true;
    }

    /// <summary>
    /// Variante que nunca se ignora: resetea cualquier transicion en curso
    /// (apertura a medias, hold retenido) y ejecuta el cambio con animacion.
    /// Usarla en redirecciones que deben funcionar siempre (ej. entrar/salir
    /// del tutorial), donde un "ignorado por transicion en curso" deja al
    /// usuario atascado sin respuesta.
    /// </summary>
    public static void TransitionToForce(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[DiagonalSceneTransition] Empty scene name, ignoring forced request.");
            return;
        }
        if (Instance == null)
        {
            Debug.LogWarning("[DiagonalSceneTransition] No instance in scene, loading directly: " + sceneName);
            LoadDirect(sceneName);
            return;
        }
        Instance.ForceLoadScene(sceneName);
    }

    /// <summary>
    /// Resetea el estado (cancela animacion en curso y holds) y arranca
    /// CloseAndLoadScene garantizado.
    /// </summary>
    public void ForceLoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        openHoldCount = 0;
        pendingOpenAfterLoad = false;
        SetOpenImmediate();
        CloseAndLoadScene(sceneName);
    }

    private static void LoadDirect(string sceneName)
    {
        if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    private IEnumerator CloseThenLoadRoutine(string sceneName)
    {
        IsTransitioning = true;
        pendingOpenAfterLoad = true;
        SetCanvasVisible(true);
        // Avisar primero para que los clientes animen su cierre en paralelo
        // al nuestro (ver HandlePreCloseMessage).
        BroadcastPreClose();
        PlayCloseSound();

        if (!IsClosed)
        {
            Vector2 startA = panelA != null ? panelA.anchoredPosition : closedPositionA + openOffsetA;
            Vector2 startB = panelB != null ? panelB.anchoredPosition : closedPositionB + openOffsetB;

            yield return AnimatePanels(startA, closedPositionA, startB, closedPositionB, Mathf.Max(0.01f, closeDuration));
        }
        else
        {
            // Already covered: snap exactly and hold one frame so it renders before loading.
            if (panelA != null) panelA.anchoredPosition = closedPositionA;
            if (panelB != null) panelB.anchoredPosition = closedPositionB;
            yield return null;
        }

        IsClosed = true;

        // Hold with panels fully closed (squared) so the cover settles
        // before the scene actually starts loading.
        if (closedHoldDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(closedHoldDelay);
        }

        // En red y como servidor, dar margen a que los clientes terminen su
        // animacion de cierre (avisada por BroadcastPreClose) antes de cargar,
        // para que no vean el corte de escena a mitad del wipe.
        NetworkManager netManager = NetworkManager.Singleton;
        if (clientCoverMargin > 0f && netManager != null && netManager.IsListening && netManager.IsServer)
        {
            yield return new WaitForSecondsRealtime(clientCoverMargin);
        }

        // Keep IsTransitioning true across the load; OnSceneLoaded -> Open() resets it.
        // NOTE: for synchronous loads, OnSceneLoaded fires inside LoadSceneInternal,
        // which calls Open() -> StopRunning() stops this routine, so code below may not run.
        LoadSceneInternal(sceneName);

        // Reached only for async loads (Netcode): wait for OnSceneLoaded to open.
        runningRoutine = null;
        yield break;
    }

    private IEnumerator OpenRoutine(Action onComplete)
    {
        IsTransitioning = true;
        SetCanvasVisible(true);

        float totalOpenDelay = openDelay + GetExtraCoverDelay(SceneManager.GetActiveScene().name);
        if (totalOpenDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(totalOpenDelay);
        }

        // A scene can retain the transition closed until its async setup is done
        // (Hold/Release). Timeout prevents wedging the transition shut forever.
        float holdWaited = 0f;
        const float holdTimeout = 10f;
        while (openHoldCount > 0 && holdWaited < holdTimeout)
        {
            yield return null;
            holdWaited += Time.unscaledDeltaTime;
        }
        openHoldCount = 0;

        PlayOpenSound();
        Vector2 startA = panelA != null ? panelA.anchoredPosition : closedPositionA;
        Vector2 startB = panelB != null ? panelB.anchoredPosition : closedPositionB;
        Vector2 endA = closedPositionA + openOffsetA;
        Vector2 endB = closedPositionB + openOffsetB;

        yield return AnimatePanels(startA, endA, startB, endB, Mathf.Max(0.01f, openDuration));

        IsClosed = false;
        IsTransitioning = false;
        runningRoutine = null;

        if (disableCanvasWhenOpen) SetCanvasVisible(false);
        onComplete?.Invoke();
        OnTransitionOpened?.Invoke();
    }

    private IEnumerator CloseRoutine(Action onComplete)
    {
        IsTransitioning = true;
        SetCanvasVisible(true);
        PlayCloseSound();

        Vector2 startA = panelA != null ? panelA.anchoredPosition : closedPositionA + openOffsetA;
        Vector2 startB = panelB != null ? panelB.anchoredPosition : closedPositionB + openOffsetB;

        yield return AnimatePanels(startA, closedPositionA, startB, closedPositionB, Mathf.Max(0.01f, closeDuration));

        IsClosed = true;
        IsTransitioning = false;
        runningRoutine = null;
        onComplete?.Invoke();
    }

    private IEnumerator AnimatePanels(Vector2 startA, Vector2 endA, Vector2 startB, Vector2 endB, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            if (panelA != null) panelA.anchoredPosition = Vector2.Lerp(startA, endA, eased);
            if (panelB != null) panelB.anchoredPosition = Vector2.Lerp(startB, endB, eased);
            yield return null;
        }

        if (panelA != null) panelA.anchoredPosition = endA;
        if (panelB != null) panelB.anchoredPosition = endB;
    }

    private void LoadSceneInternal(string sceneName)
    {
        if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    private void SetCanvasVisible(bool visible)
    {
        if (canvas != null)
        {
            canvas.enabled = visible;
        }
    }

    private void StopRunning()
    {
        if (runningRoutine != null)
        {
            StopCoroutine(runningRoutine);
            runningRoutine = null;
        }
        IsTransitioning = false;
    }

    private float GetExtraCoverDelay(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || extendedCoverScenes == null) return 0f;
        foreach (SceneCoverExtension entry in extendedCoverScenes)
        {
            if (entry != null && entry.sceneName == sceneName)
            {
                return Mathf.Max(0f, entry.extraDelay);
            }
        }
        return 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        transitionSoundVolume = Mathf.Clamp01(transitionSoundVolume);
        if (openDuration < 0f) openDuration = 0f;
        if (closeDuration < 0f) closeDuration = 0f;
        if (clientCoverMargin < 0f) clientCoverMargin = 0f;
    }
#endif
}

/// <summary>
/// Extra covered wait after loading a scene, to hide spawn/setup before revealing.
/// </summary>
[System.Serializable]
public class SceneCoverExtension
{
    public string sceneName = "Lobby";
    public float extraDelay = 0.5f;
}
