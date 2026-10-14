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

    [Header("Configuración Cuenta Regresiva")]
    [SerializeField] private int startCountdownFrom = 3;
    [SerializeField] private float stepDuration = 1.0f;
    [SerializeField] private float hideDelayAfterGo = 1.2f;
    [SerializeField] private string mainSceneName = "MainScene";

    [Header("Configuración Tutorial Express")]
    [Tooltip("Si es true, muestra los paneles del tutorial express (Objetivo y Tiempo) antes del conteo.")]
    [SerializeField] private bool enableExpressTutorial = true;
    [Tooltip("Duración de cada panel en segundos antes de avanzar automáticamente.")]
    [SerializeField] private float tutorialStepDuration = 3.5f;
    [Tooltip("Pausa breve tras el tutorial antes de iniciar el 3, 2, 1.")]
    [SerializeField] private float tutorialTransitionDelay = 0.35f;

    [Header("Espera de Cortina / Transición")]
    [Tooltip("Tiempo de espera adicional tras abrir la cortina para asegurar que todos los clientes la abrieron.")]
    [SerializeField] private float delayAfterTransitionOpen = 0.4f;
    [Tooltip("Tiempo de seguridad máximo para esperar a la cortina antes de forzar el inicio.")]
    [SerializeField] private float maxTransitionWaitTimeout = 8.0f;

    [Header("Estado Sincronizado Cuenta Regresiva (-1: Inactivo, 3..1: Números, 0: GO!)")]
    private NetworkVariable<int> currentCountdown = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Estado Sincronizado Tutorial (-1: Inactivo, 0: Objetivo, 1: Tiempo)")]
    private NetworkVariable<int> currentTutorialStep = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int CurrentCountdownValue => currentCountdown.Value;
    public bool IsCountdownActive => currentCountdown.Value >= 0;
    public int CurrentTutorialStepValue => currentTutorialStep.Value;
    public bool IsTutorialActive => currentTutorialStep.Value >= 0;
    public bool IsPreMatchActive => IsTutorialActive || (IsCountdownActive && CurrentCountdownValue > 0);
    public float TutorialStepDuration => tutorialStepDuration;

    public static event Action<int> OnCountdownTick;
    public static event Action OnCountdownFinished;
    public static event Action<int> OnTutorialStepChanged;
    public static event Action OnTutorialFinished;

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
        currentTutorialStep.OnValueChanged += OnTutorialStepValueChanged;

        // Sincronizar estado inicial para clientes al unirse o aparecer en red
        OnTutorialStepValueChanged(-1, currentTutorialStep.Value);
        OnCountdownValueChanged(-1, currentCountdown.Value);

        if (IsServer && IsInMainScene())
        {
            StartCoroutine(StartCountdownRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        currentCountdown.OnValueChanged -= OnCountdownValueChanged;
        currentTutorialStep.OnValueChanged -= OnTutorialStepValueChanged;
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
                currentTutorialStep.Value = -1;
                currentCountdown.Value = -1;
            }
        }
    }

    private bool IsInMainScene() => SceneManager.GetActiveScene().name == mainSceneName;

    private void OnTutorialStepValueChanged(int previous, int current)
    {
        if (current >= 0)
        {
            PlayerMovementManager.AllowMovement = false;
        }
        else if (current == -1 && currentCountdown.Value <= 0)
        {
            // Solo si tampoco hay cuenta regresiva activa en 3..1
            if (currentCountdown.Value == 0 || currentCountdown.Value == -1)
            {
                PlayerMovementManager.AllowMovement = true;
            }
        }

        OnTutorialStepChanged?.Invoke(current);
        if (previous >= 0 && current < 0)
        {
            OnTutorialFinished?.Invoke();
        }
    }

    private void OnCountdownValueChanged(int previous, int current)
    {
        // Bloquear movimiento en TODOS los clientes mientras dure el tutorial o el conteo (3, 2, 1)
        // Desbloquear cuando llegue a 0 ("¡A JUGAR!")
        if (current > 0 || currentTutorialStep.Value >= 0)
        {
            PlayerMovementManager.AllowMovement = false;
        }
        else if (current == 0 || (current == -1 && currentTutorialStep.Value < 0))
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
    /// Corrutina ejecutada por el Servidor para controlar la secuencia del Tutorial Express y el 3, 2, 1, ¡GO!
    /// </summary>
    public IEnumerator StartCountdownRoutine()
    {
        // 1. Bloquear movimiento de los jugadores mientras dure el tutorial y el conteo
        PlayerMovementManager.AllowMovement = false;

        // Esperar a que la cortina (DiagonalSceneTransition) esté completamente abierta
        if (DiagonalSceneTransition.Instance != null)
        {
            float transitionWait = 0f;
            while (!DiagonalSceneTransition.Instance.IsOpen && transitionWait < maxTransitionWaitTimeout)
            {
                yield return null;
                transitionWait += Time.unscaledDeltaTime;
            }
        }
        else
        {
            // Breve espera por defecto si no hay cortina
            yield return new WaitForSeconds(0.6f);
        }

        // Margen extra tras abrir la cortina para asegurar sincronización visual en todos los clientes
        if (delayAfterTransitionOpen > 0f)
        {
            yield return new WaitForSeconds(delayAfterTransitionOpen);
        }

        // 2. Tutorial Express (Panel 0: Objetivo, Panel 1: Tiempo de juego)
        if (enableExpressTutorial)
        {
            // Paso 0: Objetivo principal
            currentTutorialStep.Value = 0;
            yield return new WaitForSeconds(tutorialStepDuration);

            // Paso 1: Tiempo y victoria
            currentTutorialStep.Value = 1;
            yield return new WaitForSeconds(tutorialStepDuration);

            // Fin del tutorial express
            currentTutorialStep.Value = -1;
            yield return new WaitForSeconds(tutorialTransitionDelay);
        }

        // 3. Conteo regresivo: 3, 2, 1, 0 (¡GO!)
        for (int i = startCountdownFrom; i >= 0; i--)
        {
            currentCountdown.Value = i;
            yield return new WaitForSeconds(stepDuration);
        }

        // 4. ¡GO! alcanzado: Desbloquear movimiento e iniciar temporizador de partida
        PlayerMovementManager.AllowMovement = true;

        if (MatchTimerManager.Instance != null)
        {
            MatchTimerManager.Instance.StartTimer();
        }

        // 5. Ocultar la UI tras una breve pausa
        yield return new WaitForSeconds(hideDelayAfterGo);
        currentCountdown.Value = -1;
    }
}
