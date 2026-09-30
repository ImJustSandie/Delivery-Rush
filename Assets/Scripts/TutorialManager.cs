using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gestiona el modo tutorial ("Cómo jugar") solo con código, sin modificar escenas ni prefabs.
/// La escena de tutorial es una copia de la escena de juego (TutorialScene) que se juega
/// en un entorno completamente OFFLINE:
/// - No se inicia Host ni Server ni Client (sin red, sin IP, sin Lobby).
/// - Se instancia un jugador local a partir del PlayerPrefab del NetworkManager.
/// - Los objetos se generan por Instanciación directa (CollectibleSpawnManager en modo offline).
/// - No hay timer (MatchTimerManager solo corre en MainScene; además se oculta la UI del Timer).
/// - Jugador extra opcional (dummy, vinculado en el objeto TutorialSetup de la escena
///   con el componente TutorialSceneSetup, o por fallback con "Dummy" en su nombre):
///   queda quieto, hereda solo lo visual del usuario, muestra el nombre configurado
///   y es el blanco del power-up de congelar (el congelar nunca afecta al que lo usa).
/// - Un botón permite salir y volver a la escena inicial (ConnectionScene).
///
/// PASOS MANUALES EN EL EDITOR (el código no puede hacerlos):
/// 1. Agregar Assets/Scenes/TutorialScene.unity a la lista de escenas en Build
///    (File > Build Profiles > Scene List). Si no, la carga fallará.
///    NOTA: ConnectionUIHandler ya reasigna ComoJugar_btn -> OpenTutorialScene por código,
///    no hace falta tocar el OnClick en el Inspector.
/// 2. (Opcional) Si quieres tu propio botón de salir en TutorialScene, crea un Button
///    llamado "TutorialExitButton" (o "Volver_btn" / "Salir_btn" / "ExitButton");
///    este script lo detecta y lo conecta automáticamente. Si no existe, se crea
///    uno por defecto en tiempo de ejecución.
/// 3. (Opcional) Para el dummy: en TutorialScene crea un GameObject "TutorialSetup",
///    agrégale el componente TutorialSceneSetup y arrastra ahí el jugador de prueba.
///    Sin ese vínculo igual funciona por fallback (cualquier jugador extra en escena).
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    /// <summary>True mientras el jugador está en el tutorial.</summary>
    public static bool IsTutorial { get; private set; }

    /// <summary>
    /// True cuando el tutorial corre sin red (NetworkManager ausente o no escuchando).
    /// Los scripts de gameplay usan esto para activar sus rutas locales offline.
    /// </summary>
    public static bool IsOfflineTutorial
    {
        get
        {
            if (!IsTutorial) return false;
            return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
        }
    }

    /// <summary>
    /// Nombre que identifica al jugador de prueba del tutorial. El usuario lo coloca
    /// en la escena y este manager lo configura por código (visuales + nombre).
    /// Se puede personalizar por Inspector en <see cref="TutorialSceneSetup"/>.
    /// </summary>
    public const string DummyDisplayName = "Dummy";

    /// <summary>True si el objeto es el dummy del tutorial (su nombre contiene "Dummy").</summary>
    public static bool IsDummyName(GameObject go)
    {
        return go != null && go.name.IndexOf(DummyDisplayName, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// True si el objeto es dummy del tutorial: vinculado en <see cref="TutorialSceneSetup"/>
    /// o por nombre. Útil en Start() de otros scripts (el flag aún no está asignado).
    /// </summary>
    public static bool IsTutorialDummyObject(GameObject go)
    {
        if (go == null) return false;
        var setup = TutorialSceneSetup.Instance;
        if (setup != null && setup.IsExplicitDummyObject(go)) return true;
        return IsDummyName(go);
    }

    /// <summary>
    /// True si el componente pertenece al dummy del tutorial: flag, vínculo explícito o nombre.
    /// </summary>
    public static bool IsTutorialDummyPlayer(PlayerMovementManager pm)
    {
        if (pm == null) return false;
        if (pm.IsTutorialDummy) return true;
        var setup = TutorialSceneSetup.Instance;
        if (setup != null && setup.IsExplicitDummy(pm)) return true;
        return IsDummyName(pm.gameObject);
    }

    /// <summary>
    /// Nombre visible del dummy: el configurado en <see cref="TutorialSceneSetup"/> o "Dummy".
    /// </summary>
    public static string ResolveDummyDisplayName()
    {
        var setup = TutorialSceneSetup.Instance;
        return setup != null ? setup.DummyDisplayName : DummyDisplayName;
    }

    [Header("Scene Config")]
    [Tooltip("Nombre de la escena de tutorial (copia de la escena de juego).")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";

    [Tooltip("Nombre de la escena inicial (menú principal) a la que se regresa al salir.")]
    [SerializeField] private string connectionSceneName = "ConnectionScene";

    [Header("Exit Button")]
    [Tooltip("Texto del botón de salir creado automáticamente.")]
    [SerializeField] private string exitButtonLabel = "Volver";

    [Tooltip("Nombres de botón reconocidos como botón de salir manual (se conectan solos).")]
    [SerializeField] private string[] manualExitButtonNames = { "TutorialExitButton", "Volver_btn", "Salir_btn", "ExitButton" };

    [Tooltip("Nombre del objeto de UI del timer que se oculta en el tutorial.")]
    [SerializeField] private string timerObjectName = "Timer";

    private GameObject autoExitCanvas;
    private GameObject offlinePlayer;
    private bool offlinePlayerInstantiated;
    private Coroutine enterRoutine;

    // Se ejecuta antes de cargar la primera escena: garantiza que el manager
    // exista sin necesidad de agregarlo a mano en ninguna escena.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("TutorialManager");
        go.AddComponent<TutorialManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // ── Entrada al tutorial (llamado desde el menú) ─────────────────────────

    /// <summary>
    /// Entrada estática al tutorial. Puede llamarse desde cualquier script
    /// (por ejemplo, ConnectionUIHandler.OpenTutorialScene).
    /// </summary>
    public static void OpenTutorial(string sceneName = "TutorialScene")
    {
        EnsureInstance();
        Instance.OpenTutorialInternal(string.IsNullOrEmpty(sceneName) ? "TutorialScene" : sceneName);
    }

    private void OpenTutorialInternal(string sceneName)
    {
        tutorialSceneName = sceneName;
        IsTutorial = true;

        // Modo 100% offline: si había una sesión de red activa, cerrarla y NO iniciar otra.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[TutorialManager] Había una sesión de red activa, se cerrará para entrar al tutorial offline.");
            NetworkManager.Singleton.Shutdown();
        }

        if (NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.ResetGame(true);

        Debug.Log($"[TutorialManager] Cargando escena de tutorial offline: {sceneName}");
        SceneManager.LoadScene(sceneName);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsTutorial) return;

        if (scene.name == tutorialSceneName)
        {
            // Esperar un frame para que los Start() de la escena se registren
            // antes de instanciar el jugador offline.
            if (enterRoutine != null) StopCoroutine(enterRoutine);
            enterRoutine = StartCoroutine(EnterOfflineRoutine());
        }
        else if (scene.name == connectionSceneName)
        {
            // Al volver al menú, limpiar restos del jugador offline y del botón automático.
            CleanupOfflinePlayer();
            CleanupAutoExitButton();
        }
    }

    private IEnumerator EnterOfflineRoutine()
    {
        yield return null; // un frame: deja que la escena termine de inicializarse

        // Seguridad: nunca debe haber red en el tutorial.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SpawnOfflinePlayer();
        SetupTutorialDummies();
        HideTimerUI();
        WireOrCreateExitButton();
        enterRoutine = null;
        Debug.Log("[TutorialManager] Tutorial offline iniciado: jugador local, objetos por instanciación directa, sin timer, sin red.");
    }

    /// <summary>
    /// Instancia el jugador local sin red a partir del PlayerPrefab configurado
    /// en el NetworkManager. Si la escena ya trae un Player (no dummy), lo reutiliza.
    /// El posicionamiento lo hace PlayerSpawnSetter en modo offline (Start).
    /// </summary>
    private void SpawnOfflinePlayer()
    {
        if (offlinePlayer != null)
        {
            ApplySavedCustomization(offlinePlayer);
            return;
        }

        // Reutilizar un Player de la escena, pero NUNCA el dummy (es el rival de prueba).
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pm == null) continue;
            if (IsTutorialDummyObject(pm.gameObject)) continue;
            offlinePlayer = pm.gameObject;
            offlinePlayer.SetActive(true);
            Debug.Log("[TutorialManager] Reutilizando Player existente en TutorialScene para modo offline.");
            ApplySavedCustomization(offlinePlayer);
            return;
        }

        GameObject prefab = null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig.PlayerPrefab != null)
            prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab.gameObject;

        if (prefab == null)
        {
            Debug.LogError("[TutorialManager] No se encontró PlayerPrefab en NetworkManager.NetworkConfig. No se puede crear el jugador offline.");
            return;
        }

        offlinePlayer = Instantiate(prefab);
        offlinePlayer.name = "TutorialPlayer (Offline)";
        offlinePlayerInstantiated = true;
        Debug.Log("[TutorialManager] Jugador offline instanciado desde PlayerPrefab (sin Network Spawn).");
        ApplySavedCustomization(offlinePlayer);
    }

    /// <summary>
    /// Configura los jugadores extra de la escena como dummies de prueba:
    /// heredan solo lo visual del jugador local y muestran el nombre del setup.
    /// Son el blanco del power-up de congelar en el tutorial.
    /// Prioridad: 1) vinculados en <see cref="TutorialSceneSetup"/>, 2) resto de
    /// jugadores extra en la escena (fallback por código si el setup está vacío).
    /// </summary>
    private void SetupTutorialDummies()
    {
        if (offlinePlayer == null) return;
        PlayerMovementManager localPM = offlinePlayer.GetComponent<PlayerMovementManager>();
        CharacterCustomizationPreview localPreview = offlinePlayer.GetComponent<CharacterCustomizationPreview>();

        var setup = TutorialSceneSetup.Instance;
        string displayName = setup != null ? setup.DummyDisplayName : DummyDisplayName;
        bool copyVisuals = setup == null || setup.CopyVisualsFromLocalPlayer;
        System.Collections.Generic.List<Vector3> patrolRoute = setup != null ? setup.GetPatrolRouteWorldPositions() : null;
        // Factor sobre la velocidad viva del personaje (1 = igual que un personaje normal).
        float patrolSpeedFactor = setup != null ? setup.DummyPatrolSpeedFactor : 1f;
        float patrolArrive = setup != null ? setup.DummyPatrolArriveDistance : 0.6f;

        var configured = new HashSet<PlayerMovementManager>();
        string activeScene = SceneManager.GetActiveScene().name;

        // 1) Dummies vinculados explícitamente en el Inspector (no requieren nombre).
        if (setup != null)
        {
            foreach (PlayerMovementManager explicitDummy in setup.DummyPlayers)
            {
                if (explicitDummy == null || explicitDummy.gameObject == offlinePlayer) continue;
                if (!explicitDummy.gameObject.scene.IsValid() || !explicitDummy.gameObject.scene.isLoaded) continue;
                ConfigureTutorialDummy(explicitDummy, localPM, localPreview, displayName, copyVisuals, patrolRoute, patrolSpeedFactor, patrolArrive);
                configured.Add(explicitDummy);
            }
        }

        // 2) Fallback: cualquier otro jugador extra en la escena también es dummy.
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (pm == null || pm.gameObject == offlinePlayer || configured.Contains(pm)) continue;
            if (!pm.gameObject.scene.IsValid() || !pm.gameObject.scene.isLoaded) continue;
            if (!string.Equals(pm.gameObject.scene.name, activeScene)) continue;
            ConfigureTutorialDummy(pm, localPM, localPreview, displayName, copyVisuals, patrolRoute, patrolSpeedFactor, patrolArrive);
        }
    }

    private void ConfigureTutorialDummy(PlayerMovementManager dummyPM, PlayerMovementManager localPM, CharacterCustomizationPreview localPreview, string displayName, bool copyVisuals, System.Collections.Generic.List<Vector3> patrolRoute, float patrolSpeedFactor, float patrolArrive)
    {
        GameObject dummy = dummyPM.gameObject;
        dummyPM.IsTutorialDummy = true;
        dummy.SetActive(true);

        // Heredar únicamente lo visual del jugador local (materiales de skin).
        CharacterCustomizationPreview dummyPreview = dummy.GetComponent<CharacterCustomizationPreview>();
        if (copyVisuals && dummyPreview != null && localPreview != null)
        {
            dummyPreview.CopyVisualsFrom(localPreview);
        }
        else
        {
            // Sin previews cruzados: aplicar la misma skin guardada (mismo resultado visual).
            var sync = dummy.GetComponent<NetworkPlayerSkinSynchronizer>();
            if (sync != null) sync.ApplyOfflineCustomization();
            else if (dummyPreview != null) dummyPreview.LoadSavedSkinsFromPlayerPrefs();
        }

        // Nombre visible: el del setup (por defecto "Dummy"), nunca el del usuario.
        if (dummyPreview != null)
            dummyPreview.ApplyPlayerName(displayName);
        LobbyPlayerDisplay display = dummy.GetComponent<LobbyPlayerDisplay>();
        if (display != null)
        {
            display.DisplayNameOverride = displayName;
            display.UpdatePlayerLabel();
        }

        // Ruta de patrulla 1 → 2 → … → N → 1 (foto de posiciones tomada del setup).
        dummyPM.SetDummyPatrolRoute(patrolRoute, patrolSpeedFactor, patrolArrive);

        Debug.Log($"[TutorialManager] Dummy del tutorial configurado: {dummy.name} (visuales del usuario, nombre '{displayName}', puntos de patrulla: {(patrolRoute != null ? patrolRoute.Count : 0)}).");
    }

    /// <summary>
    /// Aplica skin y nombre guardados en PlayerPrefs al jugador offline.
    /// Refuerzo por orden de ejecución: los Start/OnEnable del jugador también lo hacen,
    /// pero aquí se garantiza aunque el prefab instanciado active sus scripts antes.
    /// </summary>
    private void ApplySavedCustomization(GameObject player)
    {
        if (player == null) return;
        var sync = player.GetComponent<NetworkPlayerSkinSynchronizer>();
        if (sync != null)
            sync.ApplyOfflineCustomization();
        else
        {
            var preview = player.GetComponent<CharacterCustomizationPreview>();
            if (preview != null)
                preview.LoadSavedSkinsFromPlayerPrefs();
            var display = player.GetComponent<LobbyPlayerDisplay>();
            if (display != null)
                display.UpdatePlayerLabel();
        }
    }

    private void CleanupOfflinePlayer()
    {
        // Solo se destruye el jugador instanciado por código. Los objetos de la escena
        // (incluido el dummy) mueren con la descarga de la escena al salir.
        if (offlinePlayer != null && offlinePlayerInstantiated)
        {
            Destroy(offlinePlayer);
        }
        offlinePlayer = null;
        offlinePlayerInstantiated = false;
    }

    /// <summary>Oculta la UI del temporizador en el tutorial (el timer ya está inactivo fuera de MainScene).</summary>
    private void HideTimerUI()
    {
        GameObject timer = GameObject.Find(timerObjectName);
        if (timer != null)
        {
            timer.SetActive(false);
            Debug.Log("[TutorialManager] UI del Timer oculta para el tutorial.");
        }
    }

    // ── Botón de salir ──────────────────────────────────────────────────────

    /// <summary>
    /// Si el usuario creó su propio botón de salir (por nombre), lo conecta;
    /// si no, crea uno por defecto en tiempo de ejecución.
    /// </summary>
    private void WireOrCreateExitButton()
    {
        foreach (string buttonName in manualExitButtonNames)
        {
            GameObject found = GameObject.Find(buttonName);
            if (found == null) continue;
            Button manualButton = found.GetComponentInChildren<Button>(true);
            if (manualButton == null) continue;
            manualButton.onClick.RemoveAllListeners();
            manualButton.onClick.AddListener(ExitTutorial);
            Debug.Log($"[TutorialManager] Botón de salir manual conectado: {buttonName}");
            return;
        }

        CreateAutoExitButton();
    }

    private void CreateAutoExitButton()
    {
        if (autoExitCanvas != null) return;

        autoExitCanvas = new GameObject("TutorialExitCanvas");
        Canvas canvas = autoExitCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        autoExitCanvas.AddComponent<CanvasScaler>();
        autoExitCanvas.AddComponent<GraphicRaycaster>();

        GameObject buttonGO = new GameObject("TutorialExitButton");
        buttonGO.transform.SetParent(autoExitCanvas.transform, false);

        Image image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);

        Button button = buttonGO.AddComponent<Button>();
        button.onClick.AddListener(ExitTutorial);

        RectTransform rect = buttonGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-20f, -20f);
        rect.sizeDelta = new Vector2(180f, 60f);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        TMP_Text label = textGO.AddComponent<TextMeshProUGUI>();
        label.text = exitButtonLabel;
        label.fontSize = 28;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Debug.Log("[TutorialManager] Botón de salir automático creado (esquina superior derecha).");
    }

    private void CleanupAutoExitButton()
    {
        if (autoExitCanvas != null)
        {
            Destroy(autoExitCanvas);
            autoExitCanvas = null;
        }
    }

    // ── Salida del tutorial ─────────────────────────────────────────────────

    /// <summary>
    /// Sale del tutorial y regresa a la escena inicial.
    /// Conéctalo al OnClick de tu propio botón o usa el botón automático.
    /// </summary>
    public void ExitTutorial()
    {
        IsTutorial = false;
        CleanupAutoExitButton();
        CleanupOfflinePlayer();

        if (enterRoutine != null)
        {
            StopCoroutine(enterRoutine);
            enterRoutine = null;
        }

        Debug.Log("[TutorialManager] Saliendo del tutorial offline, regresando al menú...");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        if (NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.ResetGame(true);

        if (SceneManager.GetActiveScene().name != connectionSceneName)
            SceneManager.LoadScene(connectionSceneName);
    }

    /// <summary>Salida estática por si se necesita desde código sin referencia a la instancia.</summary>
    public static void ExitToMenu()
    {
        if (Instance != null) Instance.ExitTutorial();
        else SceneManager.LoadScene("ConnectionScene");
    }
}
