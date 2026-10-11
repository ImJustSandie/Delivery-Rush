using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Autoridad local del AudioListener del jugador (prefab Repartidores_Animacion).
/// El prefab trae su propio AudioListener con el componente desactivado por defecto:
/// cada cliente activa UNICAMENTE el listener de su propio jugador en local,
/// todos los demas (replicas remotas, dummy de tutorial, Lobby/Podio) quedan apagados.
/// Solo codigo: no requiere cambios en escenas, prefabs ni Inspector.
/// </summary>
public class PlayerLocalAudioListener : NetworkBehaviour
{
    [Header("Scene Names")]
    [Tooltip("Escena donde el audio lo pone la Main Camera del Lobby, nunca los jugadores.")]
    [SerializeField] private string lobbySceneName = "Lobby";
    [Tooltip("Escena donde el audio lo pone la camara del podio, nunca los jugadores.")]
    [SerializeField] private string podiumSceneName = "Podio";

    private void Awake()
    {
        // Apagado inmediato para que nunca haya N listeners activos al spawnear.
        // Funciona aunque el prefab tenga el listener activado por defecto.
        SetOwnListenersEnabled(false);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyAudioAuthority();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        ApplyAudioAuthority();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ApplyAudioAuthority();
        // El owner local impone exclusividad: apaga listeners remotos que
        // pudieran haber quedado encendidos por orden de spawn.
        if (IsLocalPlayer())
        {
            EnforceSingleLocalListener();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyAudioAuthority();
        if (IsLocalPlayer())
        {
            EnforceSingleLocalListener();
        }
    }

    /// <summary>
    /// True si este jugador es el usuario local de este cliente:
    /// owner de red, o jugador no-dummy en tutorial offline (sin red).
    /// </summary>
    private bool IsLocalPlayer()
    {
        if (IsDummyPlayer())
        {
            return false;
        }
        if (IsSpawned)
        {
            return IsOwner;
        }
        // Sin spawn de red (tutorial offline): el unico jugador real es local.
        return TutorialManager.IsTutorial
            && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);
    }

    private bool IsDummyPlayer()
    {
        PlayerMovementManager pm = GetComponent<PlayerMovementManager>();
        if (pm != null && TutorialManager.IsTutorialDummyPlayer(pm))
        {
            return true;
        }
        return TutorialManager.IsTutorialDummyObject(gameObject);
    }

    /// <summary>
    /// Aplica la regla: solo el jugador local fuera de Lobby/Podio escucha.
    /// </summary>
    public void ApplyAudioAuthority()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        bool sceneTakesOverAudio = currentScene == lobbySceneName || currentScene == podiumSceneName;
        bool enable = !sceneTakesOverAudio && IsLocalPlayer();
        SetOwnListenersEnabled(enable);
    }

    private void SetOwnListenersEnabled(bool enabled)
    {
        AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
        foreach (AudioListener listener in listeners)
        {
            if (listener != null)
            {
                listener.enabled = enabled;
            }
        }
    }

    /// <summary>
    /// El cliente local apaga cualquier otro AudioListener de jugadores remotos
    /// en su vista local, garantizando un unico listener activo.
    /// En Lobby/Podio no toca la camara de la escena (solo jugadores).
    /// </summary>
    private void EnforceSingleLocalListener()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == lobbySceneName || currentScene == podiumSceneName)
        {
            return;
        }
        PlayerLocalAudioListener[] players = FindObjectsByType<PlayerLocalAudioListener>(FindObjectsSortMode.None);
        foreach (PlayerLocalAudioListener other in players)
        {
            if (other == null || other == this)
            {
                continue;
            }
            // Solo otros jugadores en red; en la vista local ninguno es owner.
            other.SetOwnListenersEnabled(false);
        }
    }
}
