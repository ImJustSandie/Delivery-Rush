using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gestor de la cuenta regresiva sincronizada (3, 2, 1, ¡GO!) antes de iniciar la partida.
/// Ubicar este script en la carpeta Assets/Scripts/Contador inicial.
/// </summary>
public class MatchCountdownManager : NetworkBehaviour
{
    public static MatchCountdownManager Instance { get; private set; }

    [Header("Configuración")]
    [SerializeField] private int startCountdownFrom = 3;
    [SerializeField] private float stepDuration = 1.0f;
    [SerializeField] private float hideDelayAfterGo = 1.2f;
    [SerializeField] private string mainSceneName = "MainScene";

    [Header("Estado Sincronizado (-1: Inactivo, 3..1: Números, 0: GO!)")]
    private NetworkVariable<int> currentCountdown = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int CurrentCountdownValue => currentCountdown.Value;
    public bool IsCountdownActive => currentCountdown.Value >= 0;

    public static event Action<int> OnCountdownTick;
    public static event Action OnCountdownFinished;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        var no = GetComponent<NetworkObject>();
        if (no == null)
        {
            gameObject.AddComponent<NetworkObject>();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentCountdown.OnValueChanged += OnCountdownValueChanged;

        // Sincronizar estado inicial para clientes al unirse o aparecer en red
        OnCountdownValueChanged(-1, currentCountdown.Value);

        if (IsServer && IsInMainScene())
        {
            StartCoroutine(StartCountdownRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        currentCountdown.OnValueChanged -= OnCountdownValueChanged;
        base.OnNetworkDespawn();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == mainSceneName && IsServer)
        {
            if (IsSpawned)
            {
                StartCoroutine(StartCountdownRoutine());
            }
        }
        else if (scene.name != mainSceneName)
        {
            PlayerMovementManager.AllowMovement = true;
            if (IsServer && IsSpawned)
            {
                currentCountdown.Value = -1;
            }
        }
    }

    private bool IsInMainScene() => SceneManager.GetActiveScene().name == mainSceneName;

    private void OnCountdownValueChanged(int previous, int current)
    {
        // Bloquear movimiento en TODOS los clientes mientras dure el conteo (3, 2, 1)
        // Desbloquear cuando llegue a 0 ("¡A JUGAR!") o sea -1 (inactivo)
        if (current > 0)
        {
            PlayerMovementManager.AllowMovement = false;
        }
        else if (current == 0 || current == -1)
        {
            PlayerMovementManager.AllowMovement = true;
        }

        OnCountdownTick?.Invoke(current);
        if (current == 0)
        {
            OnCountdownFinished?.Invoke();
        }
    }

    /// <summary>
    /// Corrutina ejecutada por el Servidor para controlar la secuencia de 3, 2, 1, ¡GO!
    /// </summary>
    public IEnumerator StartCountdownRoutine()
    {
        // 1. Bloquear movimiento de los jugadores mientras dure el conteo
        PlayerMovementManager.AllowMovement = false;

        // Breve espera para que escena y Netcode terminen de spawnear objetos
        yield return new WaitForSeconds(0.6f);

        // 2. Conteo regresivo: 3, 2, 1, 0 (¡GO!)
        for (int i = startCountdownFrom; i >= 0; i--)
        {
            currentCountdown.Value = i;
            yield return new WaitForSeconds(stepDuration);
        }

        // 3. ¡GO! alcanzado: Desbloquear movimiento e iniciar temporizador de partida
        PlayerMovementManager.AllowMovement = true;

        if (MatchTimerManager.Instance != null)
        {
            MatchTimerManager.Instance.StartTimer();
        }

        // 4. Ocultar la UI tras una breve pausa
        yield return new WaitForSeconds(hideDelayAfterGo);
        currentCountdown.Value = -1;
    }
}
