using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchTimerManager : NetworkBehaviour
{
    public static MatchTimerManager Instance { get; private set; }

    [Header("Config")]
    [SerializeField] private float matchDuration = 180f;
    [SerializeField] private string podiumSceneName = "Podium";
    [SerializeField] private string mainSceneName = "MainScene";
    [SerializeField] private bool autoStartOnMainScene = true;

    [Header("Estado (solo lectura)")]
    [SerializeField] private NetworkVariable<float> remainingTime = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] private NetworkVariable<bool> timerRunning = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] private NetworkVariable<double> netStartServerTime = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float serverStartTime;
    private bool hasEnded;
    private float localRemainingTime;
    private bool localRunning;

    public float RemainingTime
    {
        get
        {
            if (IsSpawned) return remainingTime.Value;
            // Fallback no-spawneado: usar local
            return localRemainingTime;
        }
    }
    public bool IsRunning => IsSpawned ? timerRunning.Value : localRunning;
    public float Duration => matchDuration;

    public event System.Action<float> RemainingTimeChanged;
    public event System.Action MatchEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // No hacer DontDestroyOnLoad si es un NetworkObject de escena (MainScene) — debe permanecer en la escena para que el spawn de escena funcione en clientes
        var no = GetComponent<NetworkObject>();
        if (no == null)
        {
            gameObject.AddComponent<NetworkObject>();
        }
        else if (!no.InScenePlaced)
        {
            DontDestroyOnLoad(gameObject);
        }
        // Si es scene object, no usar DontDestroyOnLoad (se re-crea al cargar MainScene)

        localRemainingTime = matchDuration;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        RemainingTimeChanged?.Invoke(RemainingTime);
        if (autoStartOnMainScene && IsInMainScene())
            Invoke(nameof(TryStartTimerFallback), 1f);
        // Intentar spawnear si somos server y aún no estamos spawneados
        Invoke(nameof(TryEnsureSpawned), 0.5f);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void TryEnsureSpawned()
    {
        if (IsSpawned) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        var no = GetComponent<NetworkObject>();
        if (no == null) return;
        if (no.IsSpawned) return;
        try { no.Spawn(); Debug.Log("[MatchTimerManager] NetworkObject spawneado manualmente."); } catch (System.Exception e) { Debug.LogWarning($"[MatchTimerManager] Spawn falló: {e.Message}"); }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        remainingTime.OnValueChanged += OnRemainingChanged;
        timerRunning.OnValueChanged += OnRunningChanged;
        netStartServerTime.OnValueChanged += OnStartTimeChanged;
        OnRemainingChanged(0f, remainingTime.Value);
        if (IsServer && localRunning)
        {
            remainingTime.Value = localRemainingTime;
            timerRunning.Value = true;
            netStartServerTime.Value = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : serverStartTime;
        }
        else if (IsServer && autoStartOnMainScene && IsInMainScene() && MatchCountdownManager.Instance == null)
        {
            StartTimer();
        }
    }

    public override void OnNetworkDespawn()
    {
        remainingTime.OnValueChanged -= OnRemainingChanged;
        timerRunning.OnValueChanged -= OnRunningChanged;
        netStartServerTime.OnValueChanged -= OnStartTimeChanged;
        base.OnNetworkDespawn();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Al volver al Lobby, restablecer hasEnded para que la siguiente partida pueda iniciar el timer
        if (NetworkGameManager.Instance != null && scene.name == NetworkGameManager.Instance.LobbySceneName)
        {
            hasEnded = false;
            localRunning = false;
            localRemainingTime = matchDuration;
            if (IsSpawned && IsServer)
            {
                timerRunning.Value = false;
                remainingTime.Value = matchDuration;
            }
            Debug.Log("[MatchTimerManager] Estado restablecido al volver al Lobby.");
            return;
        }

        // Solo el servidor inicia el timer al cargar MainScene
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer) return;

        if (scene.name == mainSceneName && autoStartOnMainScene)
        {
            hasEnded = false; // Resetear por si se vuelve a MainScene tras Podio
            Invoke(nameof(TryStartTimerFallback), 1f);
        }
        else if (scene.name == podiumSceneName)
        {
            if (IsSpawned && IsServer) timerRunning.Value = false;
            localRunning = false;
        }
    }

    private void TryStartTimerFallback()
    {
        if (hasEnded) return;
        if (!IsInMainScene()) return;
        // Solo el servidor debe iniciar el timer; clientes nunca inician por fallback
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (IsRunning) return;
        if (MatchCountdownManager.Instance != null && MatchCountdownManager.Instance.IsCountdownActive) return;
        StartTimer();
    }

    private bool IsInMainScene() => SceneManager.GetActiveScene().name == mainSceneName;

    private void OnRemainingChanged(float prev, float current) => RemainingTimeChanged?.Invoke(current);
    private void OnRunningChanged(bool prev, bool current) { if (!current && prev) MatchEnded?.Invoke(); }
    private void OnStartTimeChanged(double prev, double current)
    {
        // Clientes recalculan inmediatamente
        if (!IsServer && IsSpawned) { /* Update loop lo hará */ }
    }

    private void Update()
    {
        if (!IsInMainScene() || hasEnded) return;

        if (IsSpawned)
        {
            if (IsServer)
            {
                if (!timerRunning.Value) return;
                // Usar ServerTime para que host y clientes usen el mismo reloj
                double serverTime = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
                double start = netStartServerTime.Value;
                // Fallback si netStart no está inicializado (0) -> usar Time.time
                float elapsed = (start > 0.01) ? (float)(serverTime - start) : (Time.time - serverStartTime);
                float remaining = Mathf.Max(0f, matchDuration - elapsed);
                remainingTime.Value = remaining;
                if (remaining <= 0f) EndMatch();
            }
            else
            {
                // Cliente: predicción local basada en ServerTime para fluidez, sin esperar NetworkVariable
                if (!timerRunning.Value) return;
                double serverTime = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
                double start = netStartServerTime.Value;
                if (start <= 0.01) return; // aún no sincronizado
                float elapsed = (float)(serverTime - start);
                float remaining = Mathf.Max(0f, matchDuration - elapsed);
                // Actualizar UI local cada frame (no escribe NetworkVariable)
                RemainingTimeChanged?.Invoke(remaining);
                if (remaining <= 0f && !hasEnded)
                {
                    // El servidor hará LoadScene, cliente espera
                }
            }
        }
        else
        {
            // Fallback no-spawneado (host local sin NetworkObject)
            bool isServer = NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening;
            if (!isServer || !localRunning) return;
            float elapsed = Time.time - serverStartTime;
            float remaining = Mathf.Max(0f, matchDuration - elapsed);
            localRemainingTime = remaining;
            RemainingTimeChanged?.Invoke(localRemainingTime);
            if (remaining <= 0f) EndMatch();
        }
    }

    public void StartTimer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer) return;
        hasEnded = false;
        serverStartTime = Time.time;
        localRemainingTime = matchDuration;
        localRunning = true;
        if (IsSpawned)
        {
            remainingTime.Value = matchDuration;
            timerRunning.Value = true;
            double st = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
            netStartServerTime.Value = st;
        }
        else
        {
            RemainingTimeChanged?.Invoke(localRemainingTime);
        }
        TryEnsureSpawned();
        Debug.Log($"[MatchTimerManager] Partida iniciada: {matchDuration}s");
    }

    public void SetDuration(float seconds)
    {
        if (seconds < 5f) seconds = 5f;
        matchDuration = seconds;
        if (IsSpawned && IsServer && timerRunning.Value)
            remainingTime.Value = Mathf.Min(remainingTime.Value, matchDuration);
    }

    private void EndMatch()
    {
        if (hasEnded) return;
        hasEnded = true;
        if (IsSpawned) timerRunning.Value = false;
        localRunning = false;
        if (IsSpawned) remainingTime.Value = 0f;
        else localRemainingTime = 0f;
        RemainingTimeChanged?.Invoke(0f);
        MatchEnded?.Invoke();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            NetworkManager.Singleton.SceneManager.LoadScene(podiumSceneName, LoadSceneMode.Single);
        else if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            SceneManager.LoadScene(podiumSceneName);
    }

    public string GetFormattedTime()
    {
        float t = RemainingTime;
        int ti = Mathf.CeilToInt(t);
        return $"{ti/60:00}:{ti%60:00}";
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (matchDuration < 5f) matchDuration = 5f;
        if (string.IsNullOrEmpty(podiumSceneName)) podiumSceneName = "Podium";
        if (string.IsNullOrEmpty(mainSceneName)) mainSceneName = "MainScene";
    }
#endif
}
