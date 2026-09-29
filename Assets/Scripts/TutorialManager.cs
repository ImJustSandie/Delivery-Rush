using System.Collections;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gestiona el modo tutorial ("Cómo jugar") solo con código, sin modificar escenas ni prefabs.
/// La escena de tutorial es una copia de la escena de juego (TutorialScene) donde:
/// - Los objetos se generan de forma normal (se inicia un Host local en solitario,
///   por lo que CollectibleSpawnManager y el resto de lógica de servidor funcionan igual).
/// - No hay timer (MatchTimerManager solo corre en MainScene; además se oculta la UI del Timer).
/// - No hay más jugadores (se bloquean nuevas conexiones tras iniciar el Host en solitario).
/// - Un botón permite salir y volver a la escena inicial (ConnectionScene).
///
/// PASOS MANUALES EN EL EDITOR (el código no puede hacerlos):
/// 1. Agregar Assets/Scenes/TutorialScene.unity a la lista de escenas en Build
///    (File > Build Profiles > Scene List). Si no, la carga fallará.
/// 2. En ConnectionScene, asignar al botón ComoJugar_btn el evento OnClick ->
///    ConnectionUIHandler.OpenTutorialScene (arrastrar el objeto ConnectionUIHandler).
/// 3. (Opcional) Si quieres tu propio botón de salir en TutorialScene, crea un Button
///    llamado "TutorialExitButton" (o "Volver_btn" / "Salir_btn" / "ExitButton");
///    este script lo detecta y lo conecta automáticamente. Si no existe, se crea
///    uno por defecto en tiempo de ejecución.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    /// <summary>True mientras el jugador está en el tutorial.</summary>
    public static bool IsTutorial { get; private set; }

    [Header("Scene Config")]
    [Tooltip("Nombre de la escena de tutorial (copia de la escena de juego).")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";

    [Tooltip("Nombre de la escena inicial (menú principal) a la que se regresa al salir.")]
    [SerializeField] private string connectionSceneName = "ConnectionScene";

    [Header("Solo Host Config")]
    [Tooltip("Puerto usado para el Host local en solitario.")]
    [SerializeField] private ushort port = 7777;

    [Header("Exit Button")]
    [Tooltip("Texto del botón de salir creado automáticamente.")]
    [SerializeField] private string exitButtonLabel = "Volver";

    [Tooltip("Nombres de botón reconocidos como botón de salir manual (se conectan solos).")]
    [SerializeField] private string[] manualExitButtonNames = { "TutorialExitButton", "Volver_btn", "Salir_btn", "ExitButton" };

    [Tooltip("Nombre del objeto de UI del timer que se oculta en el tutorial.")]
    [SerializeField] private string timerObjectName = "Timer";

    private GameObject autoExitCanvas;
    private Coroutine startHostRoutine;

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

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[TutorialManager] Había una sesión de red activa, se cerrará para entrar al tutorial.");
            NetworkManager.Singleton.Shutdown();
        }

        if (NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.ResetGame(true);

        Debug.Log($"[TutorialManager] Cargando escena de tutorial: {sceneName}");
        SceneManager.LoadScene(sceneName);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsTutorial) return;

        if (scene.name == tutorialSceneName)
        {
            // Esperar un frame para que los Start() de la escena (CollectibleSpawnManager,
            // MatchTimerManager, etc.) se registren antes de iniciar el Host local.
            if (startHostRoutine != null) StopCoroutine(startHostRoutine);
            startHostRoutine = StartCoroutine(EnterTutorialRoutine());
        }
        else if (scene.name == connectionSceneName)
        {
            // Al volver al menú, limpiar restos del botón automático por si acaso.
            CleanupAutoExitButton();
        }
    }

    private IEnumerator EnterTutorialRoutine()
    {
        yield return null; // un frame: deja que la escena termine de inicializarse
        StartSoloHost();
        HideTimerUI();
        WireOrCreateExitButton();
        startHostRoutine = null;
    }

    /// <summary>
    /// Inicia un Host local en solitario para que toda la lógica de servidor
    /// (spawn de objetos, recolección, entregas) funcione como en la partida normal,
    /// pero sin timer (inactivo fuera de MainScene) y sin más jugadores
    /// (se bloquean nuevas conexiones).
    /// </summary>
    private void StartSoloHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[TutorialManager] NetworkManager.Singleton no encontrado. El tutorial necesita el NetworkManager persistente del menú.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            string localIP = LobbyUIHandler.GetLocalIPAddress();
            transport.SetConnectionData(localIP, port, "0.0.0.0");
            Debug.Log($"[TutorialManager] Host de tutorial en {localIP}:{port} (solo local).");
        }

        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.ConfigureConnectionApproval();
            NetworkGameManager.Instance.ResetGame();
        }

        bool ok = NetworkManager.Singleton.StartHost();
        if (!ok)
        {
            Debug.LogError("[TutorialManager] No se pudo iniciar el Host de tutorial.");
            return;
        }

        // Bloquear nuevas conexiones: no habrá más jugadores.
        if (NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.StartGame();

        Debug.Log("[TutorialManager] Tutorial iniciado: objetos normales, sin timer, sin más jugadores.");
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

        if (startHostRoutine != null)
        {
            StopCoroutine(startHostRoutine);
            startHostRoutine = null;
        }

        Debug.Log("[TutorialManager] Saliendo del tutorial, regresando al menú...");
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
