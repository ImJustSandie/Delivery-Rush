using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementManager : NetworkBehaviour
{
    [SerializeField] private InputActionAsset actionsAsset;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private UnityEvent onInteract;

    [Header("Touch UI")]
    [Tooltip("Objeto del virtual pad (joystick) creado en el Canvas. Referencia para tenerlo localizado desde el player.")]
    [SerializeField] private GameObject virtualMovePadObject;
    [Tooltip("Boton de interactuar creado en el Canvas. Al asignarlo se suscribe su onClick automaticamente.")]
    [SerializeField] private Button interactButton;

    [Header("Camera Relative")]
    [Tooltip("Camara de referencia para el movimiento. Si se deja vacio se detecta la camara del jugador (PlayerCamera).")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Si es true, el jugador rota para mirar hacia la direccion de movimiento.")]
    [SerializeField] private bool faceMoveDirection = true;
    [SerializeField] private float turnSpeed = 10f;

    private Vector2 virtualMoveInput;

    private InputAction moveAction;
    private InputAction interactAction;

    private InteractableCube currentInteractable;
    private InteractableCube carriedInteractable;

    [Header("Collectibles")]
    [Tooltip("Número máximo de objetos que el jugador puede llevar a la vez.")]
    [SerializeField] private int maxCollected = 5;
    [Tooltip("Contador sincronizado de recolectables. Solo el servidor escribe.")]
    private NetworkVariable<int> collectedCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Power-Ups")]
    [Tooltip("Tope TOTAL del inventario de power-ups (cola FIFO: el primero que entra es el primero que sale).")]
    [SerializeField] private int maxPowerUps = 2;
    [Tooltip("Cola FIFO de power-ups (0=ralentizar, 1=velocidad). El tipo se sortea al recoger. Solo el servidor escribe.")]
    private NetworkList<int> powerUpQueue = new NetworkList<int>();
    [Tooltip("Efecto visual activo (0=ninguno, 1=boost, 2=slow). Lo escribe el servidor, todos los clientes lo muestran.")]
    private NetworkVariable<int> activeVisualEffect = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Efectos de Power-Up")]
    [Tooltip("Multiplicador aplicado a los rivales con SlowOthers (menor que 1 = ralentiza).")]
    [SerializeField] private float slowMultiplier = 0.5f;
    [Tooltip("Multiplicador aplicado al caster con BoostSelf (mayor que 1 = acelera).")]
    [SerializeField] private float boostMultiplier = 1.8f;
    [Tooltip("Duración en segundos del efecto de velocidad.")]
    [SerializeField] private float powerUpEffectDuration = 5f;
    [Tooltip("Efecto usado por el botón de power-up asignado directamente en este prefab. Random elige al azar entre los dos.")]
    [SerializeField] private PowerUpEffect powerUpButtonEffect = PowerUpEffect.Random;
    [Tooltip("Botón de UI para usar el power-up (opcional, alternativo a PowerUpUI). Al asignarlo se suscribe su onClick automáticamente.")]
    [SerializeField] private Button powerUpButton;

    [Header("Scoring")]
    [Tooltip("Puntuación total entregada en edificio. Solo el servidor escribe.")]
    private NetworkVariable<int> deliveredScore = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action<bool> IsMovingChanged;
    public event Action<bool> IsCarryingChanged;
    public event Action<int> CollectedCountChanged;
    public event Action<int> ScoreChanged;
    public event Action<int> PowerUpCountChanged;
    public event Action<PlayerNetworkState> NetworkStateChanged;

    public bool IsMoving { get; private set; }
    // Nuevo: IsCarrying ahora refleja si lleva al menos 1 recolectable. Se mantiene compatibilidad con código antiguo basado en carriedInteractable.
    public bool IsCarrying => CollectedCount > 0 || carriedInteractable != null;
    public int CollectedCount => collectedCount.Value;
    public int MaxCollected => maxCollected;
    public bool CanCollect => CollectedCount < maxCollected;
    public bool IsInventoryFull => CollectedCount >= maxCollected;
    public int Score => deliveredScore.Value;
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }

    // ── Inventario de power-ups: cola FIFO (el primero que entra es el primero que sale) ──
    public int SlowPowerUpCount => CountPowerUpType(PowerUpEffect.SlowOthers);
    public int BoostPowerUpCount => CountPowerUpType(PowerUpEffect.BoostSelf);
    public int PowerUpCount => powerUpQueue.Count;
    public int MaxPowerUps => maxPowerUps;
    public bool CanCarryPowerUp => powerUpQueue.Count < maxPowerUps;
    public bool HasPowerUp => powerUpQueue.Count > 0;
    public bool HasSlowPowerUp => ContainsPowerUpType(PowerUpEffect.SlowOthers);
    public bool HasBoostPowerUp => ContainsPowerUpType(PowerUpEffect.BoostSelf);
    /// <summary>Efecto al frente de la cola (el próximo en salir). Null si vacía.</summary>
    public PowerUpEffect? FrontPowerUp => powerUpQueue.Count > 0 ? (PowerUpEffect)Mathf.Clamp(powerUpQueue[0], 0, 1) : null;

    private int CountPowerUpType(PowerUpEffect type)
    {
        int count = 0;
        int target = (int)type;
        foreach (int entry in powerUpQueue)
        {
            if (entry == target) count++;
        }
        return count;
    }

    private bool ContainsPowerUpType(PowerUpEffect type)
    {
        int target = (int)type;
        foreach (int entry in powerUpQueue)
        {
            if (entry == target) return true;
        }
        return false;
    }

    public void SetMoveSpeed(float newSpeed)
    {
        moveSpeed = newSpeed;
        if (speedEffectCoroutine == null)
            baseMoveSpeed = newSpeed;
    }


    private bool lastCarrying;
    private int lastCollectedCount = -1;
    private int lastScore = -1;
    private int lastPowerUpCount = -1;
    private PlayerNetworkState lastPublishedPlayerState;

    // Estado del modificador temporal de velocidad (se aplica en el owner).
    private float baseMoveSpeed;
    private Coroutine speedEffectCoroutine;
    [SerializeField] private float gravity = -9.81f;
    private float verticalVelocity;

    [Header("Proximity Collection")]
    [Tooltip("Radio de detección para recoger collectibles automáticamente.")]
    [SerializeField] private float collectRadius = 1.5f;

    private float lastCollectAttemptTime;
    private const float CollectAttemptCooldown = 0.15f;

    [Header("Scene Camera Settings")]
    [Tooltip("Nombre de la escena de Lobby donde la cámara del personaje debe permanecer desactivada.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    private bool hasPublishedPlayerState;

    private CharacterController characterController;
    private Camera cachedPlayerCamera;

    private void Awake()
    {
        EnsureCharacterController();
        baseMoveSpeed = moveSpeed;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        EnsureCharacterController();
        UpdateCameraState();
        ResolveCameraTransform();
        collectedCount.OnValueChanged += OnCollectedCountChanged;
        deliveredScore.OnValueChanged += OnScoreChanged;
        powerUpQueue.OnListChanged += OnPowerUpQueueChanged;
        activeVisualEffect.OnValueChanged += OnVisualEffectChanged;
        // Sincronizar estado inicial
        OnCollectedCountChanged(0, collectedCount.Value);
        OnScoreChanged(0, deliveredScore.Value);
        RefreshPowerUpState();
        ApplyVisualEffectState(activeVisualEffect.Value);

        if (IsOwner)
        {
            SetupInputActions();
            // Asegurar que el controlador de cámara siga a este jugador local
            CameraYawPitchDragController camController = GetComponentInChildren<CameraYawPitchDragController>(true);
            if (camController != null)
            {
                camController.enabled = true;
                camController.SetTarget(transform);
            }
            if (characterController != null) characterController.enabled = true;
        }
        else
        {
            CameraYawPitchDragController camController = GetComponentInChildren<CameraYawPitchDragController>(true);
            if (camController != null) camController.enabled = false;

            // Desactivar CharacterController en instancias remotas para que
            // ClientNetworkTransform sincronice la posición sin conflictos.
            if (characterController != null) characterController.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        collectedCount.OnValueChanged -= OnCollectedCountChanged;
        deliveredScore.OnValueChanged -= OnScoreChanged;
        powerUpQueue.OnListChanged -= OnPowerUpQueueChanged;
        activeVisualEffect.OnValueChanged -= OnVisualEffectChanged;
        base.OnNetworkDespawn();
    }

    private void OnCollectedCountChanged(int previous, int current)
    {
        CollectedCountChanged?.Invoke(current);
        RefreshCarryingState();
        PublishPlayerState(true);
        Debug.Log($"[PlayerMovementManager] {name} recolectados: {current}");
    }

    private void OnScoreChanged(int previous, int current)
    {
        ScoreChanged?.Invoke(current);
        PublishPlayerState(true);
        Debug.Log($"[PlayerMovementManager] {name} puntuación: {current}");
    }

    private void OnPowerUpQueueChanged(NetworkListEvent<int> changeEvent)
    {
        RefreshPowerUpState();
    }

    private void RefreshPowerUpState()
    {
        PowerUpCountChanged?.Invoke(PowerUpCount);
        PublishPlayerState(true);
        Debug.Log($"[PlayerMovementManager] {name} power-ups: ralentizar={SlowPowerUpCount} velocidad={BoostPowerUpCount} (cola FIFO, tope {maxPowerUps})");
    }

    // ── Visuales de power-up (visibles para todos) ───────────────────────

    private void OnVisualEffectChanged(int previous, int current)
    {
        ApplyVisualEffectState(current);
    }

    private void ApplyVisualEffectState(int visual)
    {
        PowerUpVisuals visuals = GetComponent<PowerUpVisuals>();
        if (visuals == null) return;
        visuals.SetBoostActive(visual == 1);
        visuals.SetSlowActive(visual == 2);
    }

    /// <summary>Activa el visual en red durante `duration` segundos (solo servidor).</summary>
    public void SetVisualEffectServerSide(int visual, float duration)
    {
        if (!IsServer) return;
        activeVisualEffect.Value = visual;
        CancelInvoke(nameof(ClearVisualEffectServerSide));
        if (visual != 0 && duration > 0f)
            Invoke(nameof(ClearVisualEffectServerSide), duration);
    }

    private void ClearVisualEffectServerSide()
    {
        if (!IsServer) return;
        activeVisualEffect.Value = 0;
    }

    private void OnEnable()
    {
        if (interactButton != null)
            interactButton.onClick.AddListener(OnInteractButtonPressed);

        if (powerUpButton != null)
            powerUpButton.onClick.AddListener(OnPowerUpButtonPressed);

        if (IsSpawned)
        {
            UpdateCameraState();
            if (IsOwner)
            {
                SetupInputActions();
            }
        }
    }

    private void OnDisable()
    {
        if (interactButton != null)
            interactButton.onClick.RemoveListener(OnInteractButtonPressed);
        if (powerUpButton != null)
            powerUpButton.onClick.RemoveListener(OnPowerUpButtonPressed);
        if (moveAction != null)
        {
            moveAction.Disable();
            moveAction = null;
        }

        if (interactAction != null)
        {
            interactAction.performed -= HandleInteract;
            interactAction.Disable();
            interactAction = null;
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Limpiar referencia cacheada de cámara de la escena anterior para forzar re-resolución
        cameraTransform = null;

        // Reiniciar puntaje únicamente al volver al Lobby (para no borrar los puntos al entrar al Podio)
        if (IsSpawned && IsServer && scene.name == lobbySceneName)
        {
            ResetScoreServerSide();
        }

        // Si el PodiumManager desactivó este componente, reactivarlo al entrar en una nueva escena
        if (!enabled)
        {
            enabled = true;
            Debug.Log($"[PlayerMovementManager] {name} re-activado al cargar escena {scene.name}");
        }

        UpdateCameraState();
        ResolveCameraTransform();

        // Re-activar el controlador de cámara si fue desactivado (ej. por PodiumManager)
        if (IsOwner)
        {
            CameraYawPitchDragController camController = GetComponentInChildren<CameraYawPitchDragController>(true);
            if (camController != null)
            {
                string currentScene = scene.name;
                bool inLobby = (currentScene == lobbySceneName);
                camController.enabled = !inLobby;
                if (!inLobby) camController.SetTarget(transform);
            }

            SetupInputActions();
        }
    }

    /// <summary>
    /// Activa o desactiva la cámara del personaje según la escena activa y la autoridad (IsOwner).
    /// En el Lobby la cámara del personaje siempre está desactivada para usar la Main Camera del Lobby.
    /// En el juego (MainScene) la cámara se activa únicamente para el jugador local (IsOwner).
    /// </summary>
    private void UpdateCameraState()
    {
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool inLobby = (currentScene == lobbySceneName);

        Camera cam = GetComponentInChildren<Camera>(true);
        AudioListener listener = GetComponentInChildren<AudioListener>(true);

        if (!IsOwner || inLobby)
        {
            if (cam != null) cam.enabled = false;
            if (listener != null) listener.enabled = false;
        }
        else
        {
            if (cam != null) cam.enabled = true;
            if (listener != null) listener.enabled = true;
        }

        // Habilitar/deshabilitar los Canvases e interfaz del jugador según si es el dueño y está en partida (no en lobby)
        bool showUI = IsOwner && !inLobby;

        Canvas[] playerCanvases = GetComponentsInChildren<Canvas>(true);
        foreach (Canvas c in playerCanvases)
        {
            c.enabled = showUI;
        }

        if (virtualMovePadObject != null)
        {
            virtualMovePadObject.SetActive(showUI);
        }

        if (interactButton != null)
        {
            interactButton.gameObject.SetActive(showUI);
        }

        if (powerUpButton != null)
        {
            powerUpButton.gameObject.SetActive(showUI);
        }
    }

    private void SetupInputActions()
    {
        if (actionsAsset == null)
        {
            Debug.LogWarning($"{name}: asignar InputSystem_Actions a actionsAsset en el Inspector.");
            return;
        }

        if (moveAction == null)
            moveAction = actionsAsset.FindAction("Player/Move");
        if (interactAction == null)
            interactAction = actionsAsset.FindAction("Player/Interact");

        moveAction?.Enable();
        if (interactAction != null)
        {
            interactAction.Enable();
            interactAction.performed -= HandleInteract;
            interactAction.performed += HandleInteract;
        }
    }

    private void Start()
    {
        PublishPlayerState(true);
    }

    private Transform ResolveCameraTransform()
    {
        if (cameraTransform != null) return cameraTransform;

        // Prioridad 1: Cámara principal de la escena (Camera.main)
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
            return cameraTransform;
        }

        // Prioridad 2: Cámara hija del propio jugador (PlayerCamera del prefab)
        if (cachedPlayerCamera == null)
            cachedPlayerCamera = GetComponentInChildren<Camera>(true);
        if (cachedPlayerCamera != null && cachedPlayerCamera.enabled)
        {
            cameraTransform = cachedPlayerCamera.transform;
            return cameraTransform;
        }

        Camera anyCam = FindFirstObjectByType<Camera>();
        if (anyCam != null)
        {
            cameraTransform = anyCam.transform;
            return cameraTransform;
        }

        return null;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // En el lobby no se procesa movimiento ni gravedad del jugador
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == lobbySceneName)
        {
            if (characterController != null && characterController.enabled)
            {
                characterController.enabled = false;
            }
            return;
        }

        // Re-habilitar CharacterController si fue desactivado en el lobby
        if (characterController != null && !characterController.enabled)
        {
            characterController.enabled = true;
        }

        // Lazy resolve por si la cámara se instanció después
        if (cameraTransform == null)
            ResolveCameraTransform();

        Vector2 input = Vector2.zero;

        if (moveAction != null && moveAction.enabled)
            input += moveAction.ReadValue<Vector2>();

        input += virtualMoveInput;
        if (input.sqrMagnitude > 1f) input.Normalize();

        bool moving = input.sqrMagnitude >= 0.0001f;
        SetIsMoving(moving);
        RefreshCarryingState();

        Transform cam = ResolveCameraTransform();

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cam != null)
        {
            forward = cam.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            right = cam.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();
        }

        if (characterController != null && characterController.enabled)
        {
            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }

        Vector3 direction = right * input.x + forward * input.y;
        if (direction.sqrMagnitude > 1f) direction.Normalize();

        Vector3 velocity = direction * moveSpeed;
        velocity.y = verticalVelocity;

        if (characterController != null && characterController.enabled)
        {
            characterController.Move(velocity * Time.deltaTime);
        }
        else
        {
            transform.position += velocity * Time.deltaTime;
        }

        if (moving && faceMoveDirection && direction.sqrMagnitude >= 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRotation, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        RefreshCarryingState();
        PublishPlayerState();

        // Detectar collectibles cercanos por proximidad (funciona tanto en host como en cliente)
        TryCollectNearby();
    }

    /// <summary>
    /// Detecta collectibles y power-ups cercanos usando OverlapSphere y solicita la recolección.
    /// Esto reemplaza la dependencia en triggers que no funcionan de forma fiable para clientes.
    /// </summary>
    private void TryCollectNearby()
    {
        if (IsInventoryFull && !CanCarryPowerUp) return;
        if (Time.time - lastCollectAttemptTime < CollectAttemptCooldown) return;

        // Buscar colliders cercanos (tanto solid como trigger del recolectable)
        Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.5f, collectRadius);
        foreach (Collider hit in hits)
        {
            // Prioridad: power-up (inventario separado)
            PowerUpCube powerUp = hit.GetComponentInParent<PowerUpCube>();
            if (powerUp != null && !powerUp.IsCollected && CanCarryPowerUp)
            {
                NetworkObject powerNetObj = powerUp.GetComponent<NetworkObject>();
                if (powerNetObj != null && powerNetObj.IsSpawned)
                {
                    lastCollectAttemptTime = Time.time;
                    if (IsServer)
                        powerUp.TryCollect(this);
                    else
                        RequestCollectPowerUpServerRpc(powerNetObj.NetworkObjectId);
                    break;
                }
            }

            if (IsInventoryFull) continue;
            InteractableCube cube = hit.GetComponentInParent<InteractableCube>();
            if (cube == null || cube.IsCollected) continue;

            NetworkObject cubeNetObj = cube.GetComponent<NetworkObject>();
            if (cubeNetObj == null || !cubeNetObj.IsSpawned) continue;

            lastCollectAttemptTime = Time.time;

            if (IsServer)
            {
                cube.TryCollect(this);
            }
            else
            {
                // El RPC va en PlayerMovementManager (propiedad del cliente),
                // no en InteractableCube (propiedad del servidor), para
                // que el sistema de permisos de NGO lo permita.
                RequestCollectItemServerRpc(cubeNetObj.NetworkObjectId);
            }
            break; // Un intento por cooldown
        }
    }

    /// <summary>
    /// RPC llamado por el cliente dueño de este jugador para solicitar al servidor
    /// la recolección de un collectible específico. Al estar en el NetworkObject
    /// del propio jugador, no hay problemas de permisos de ownership.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void RequestCollectItemServerRpc(ulong collectibleNetworkObjectId)
    {
        if (IsInventoryFull) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(collectibleNetworkObjectId, out NetworkObject cubeObj))
            return;

        InteractableCube cube = cubeObj.GetComponent<InteractableCube>();
        if (cube == null || cube.IsCollected) return;

        // Validar proximidad en el servidor para evitar exploits
        float distance = Vector3.Distance(transform.position, cubeObj.transform.position);
        if (distance > collectRadius * 3f)
        {
            Debug.Log($"[PlayerMovementManager] {name} demasiado lejos del collectible {cube.name}: {distance:F1}m");
            return;
        }

        cube.TryCollect(this);
    }

    /// <summary>
    /// RPC llamado por el cliente dueño para solicitar la recolección de un power-up.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void RequestCollectPowerUpServerRpc(ulong powerUpNetworkObjectId)
    {
        if (!CanCarryPowerUp) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(powerUpNetworkObjectId, out NetworkObject powerObj))
            return;

        PowerUpCube powerUp = powerObj.GetComponent<PowerUpCube>();
        if (powerUp == null || powerUp.IsCollected) return;

        float distance = Vector3.Distance(transform.position, powerObj.transform.position);
        if (distance > collectRadius * 3f)
        {
            Debug.Log($"[PlayerMovementManager] {name} demasiado lejos del power-up {powerUp.name}: {distance:F1}m");
            return;
        }

        powerUp.TryCollect(this);
    }

    private void EnsureCharacterController()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
            characterController.center = new Vector3(0f, 1f, 0f);
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.skinWidth = 0.08f;
            characterController.minMoveDistance = 0.001f;
        }

        // Si queda un CapsuleCollider legacy solapado con el CharacterController, desactivarlo para evitar jitter
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null && characterController != null)
        {
            // Solo desactivar si ambos están en el mismo GameObject y el CC está activo
            if (capsule.enabled)
            {
                // Mantener trigger colliders intactos, solo solid
                if (!capsule.isTrigger)
                    capsule.enabled = false;
            }
        }
    }



    /// <summary>Llamado por el joystick virtual (evento Vector2) para mover al jugador.</summary>
    public void SetVirtualMove(Vector2 input)
    {
        if (input.sqrMagnitude > 1f) input.Normalize();
        virtualMoveInput = input;
    }

    /// <summary>Llamado por el boton UI de interactuar (onClick).</summary>
    public void OnInteractButtonPressed()
    {
        onInteract?.Invoke();
        HandleInteraction();
    }

    private void HandleInteract(InputAction.CallbackContext context)
    {
        onInteract?.Invoke();
        HandleInteraction();
    }

    private void HandleInteraction()
    {
        // Recolección ahora es automática por trigger (InteractableCube.TryCollect).
        // Se mantiene el hook por compatibilidad pero ya no hace pickup manual.
        // Si quieres mantener interacción legacy, descomenta el bloque TryPickUp.
        LogState();
    }

    private void DropCarried()
    {
        if (carriedInteractable == null) return;
        carriedInteractable.Drop();
        carriedInteractable = null;
        LogState();
    }

    private void LogState()
    {
        Debug.Log($"[PlayerMovementManager] recolectados: {CollectedCount} | cargando cubo (legacy): {carriedInteractable != null}");
    }

    /// <summary>Llamado por InteractableCube al ser pisado (hitbox trigger). Solo el servidor incrementa NetworkVariable.</summary>
    public bool AddCollected(int amount = 1)
    {
        if (amount <= 0) return false;
        if (IsInventoryFull)
        {
            Debug.Log($"[PlayerMovementManager] {name} inventario lleno ({CollectedCount}/{maxCollected}), no se puede recolectar.");
            return false;
        }
        if (IsServer)
        {
            int allowed = Mathf.Min(amount, maxCollected - collectedCount.Value);
            if (allowed <= 0) return false;
            collectedCount.Value += allowed;
            return true;
        }
        else
        {
            RequestAddCollectedServerRpc(amount);
            // Resultado real se valida en servidor; retorno optimista si aún hay espacio
            return true;
        }
    }

    /// <summary>Variante directa solo-servidor usada por InteractableCube.PerformCollect. Retorna false si inventario lleno.</summary>
    public bool AddCollectedServerSide(int amount = 1)
    {
        if (!IsServer) return false;
        if (IsInventoryFull) return false;
        int allowed = Mathf.Min(amount, maxCollected - collectedCount.Value);
        if (allowed <= 0) return false;
        collectedCount.Value += allowed;
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestAddCollectedServerRpc(int amount)
    {
        if (IsInventoryFull) return;
        int allowed = Mathf.Min(amount, maxCollected - collectedCount.Value);
        if (allowed <= 0) return;
        collectedCount.Value += allowed;
    }

    // ── Inventario de power-ups (el tipo se sortea al recoger) ────────────

    /// <summary>Variante solo-servidor. Encola power-ups del tipo indicado (FIFO). Retorna false si la cola está llena.</summary>
    public bool AddPowerUpServerSide(PowerUpEffect effect, int amount = 1)
    {
        if (!IsServer) return false;
        if (effect == PowerUpEffect.Random)
            effect = PowerUpEffect.BoostSelf;
        int allowed = Mathf.Min(amount, maxPowerUps - powerUpQueue.Count);
        if (allowed <= 0) return false;
        for (int i = 0; i < allowed; i++)
            powerUpQueue.Add((int)effect);
        return true;
    }

    /// <summary>Llamado por el botón de UI asignado en este prefab (usa <see cref="powerUpButtonEffect"/>).</summary>
    public void OnPowerUpButtonPressed()
    {
        TryUsePowerUp(powerUpButtonEffect);
    }

    /// <summary>
    /// Usa el power-up al frente de la cola FIFO. Solo el owner lo invoca (botón UI);
    /// el servidor valida, desencola y aplica el efecto.
    /// Con efecto fijo (SlowOthers/BoostSelf) solo sale si el frente es de ese tipo;
    /// con <see cref="PowerUpEffect.Random"/> sale el que esté al frente sea cual sea.
    /// </summary>
    public void TryUsePowerUp(PowerUpEffect effect)
    {
        if (!IsOwner) return;
        if (!HasPowerUp)
        {
            Debug.Log($"[PlayerMovementManager] {name} no tiene power-ups para usar.");
            return;
        }
        PowerUpEffect? front = FrontPowerUp;
        if (effect != PowerUpEffect.Random && front != effect)
        {
            Debug.Log($"[PlayerMovementManager] {name} el primero de la cola es {front} (pedido {effect}): sale el primero que entró.");
            return;
        }
        RequestUsePowerUpServerRpc((int)effect);
    }

    [Rpc(SendTo.Server)]
    private void RequestUsePowerUpServerRpc(int effectIndex)
    {
        if (powerUpQueue.Count <= 0) return;
        PowerUpEffect requested = (PowerUpEffect)Mathf.Clamp(effectIndex, 0, 2);
        PowerUpEffect front = (PowerUpEffect)Mathf.Clamp(powerUpQueue[0], 0, 1);
        // FIFO estricto: solo sale el frente; si el botón pide un tipo distinto, se deniega.
        if (requested != PowerUpEffect.Random && front != requested) return;

        powerUpQueue.RemoveAt(0);
        PowerUpEffect effect = front;
        Debug.Log($"[PlayerMovementManager] {name} usa power-up {effect} (cola restante: {powerUpQueue.Count}).");

        if (effect == PowerUpEffect.BoostSelf)
        {
            // Aumenta temporalmente la velocidad solo del caster + cambio de modelo visible para todos.
            ApplySpeedEffectClientRpc(boostMultiplier, powerUpEffectDuration);
            SetVisualEffectServerSide(1, powerUpEffectDuration);
        }
        else
        {
            // Ralentiza temporalmente a todos excepto al caster + cambio de textura visible para todos.
            foreach (PlayerMovementManager other in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
            {
                if (other == null || other == this) continue;
                other.ApplySpeedEffectClientRpc(slowMultiplier, powerUpEffectDuration);
                other.SetVisualEffectServerSide(2, powerUpEffectDuration);
            }
        }
    }

    /// <summary>
    /// Se ejecuta en el owner del jugador afectado y modifica su velocidad temporalmente.
    /// Invocado por el servidor sobre el NetworkObject del jugador objetivo.
    /// </summary>
    [Rpc(SendTo.Owner)]
    private void ApplySpeedEffectClientRpc(float multiplier, float duration)
    {
        if (!IsOwner) return;
        if (baseMoveSpeed <= 0f) baseMoveSpeed = moveSpeed;
        if (speedEffectCoroutine != null)
            StopCoroutine(speedEffectCoroutine);
        speedEffectCoroutine = StartCoroutine(SpeedEffectRoutine(multiplier, duration));
    }

    private IEnumerator SpeedEffectRoutine(float multiplier, float duration)
    {
        moveSpeed = baseMoveSpeed * multiplier;
        Debug.Log($"[PlayerMovementManager] {name} efecto de velocidad x{multiplier} durante {duration}s.");
        yield return new WaitForSeconds(duration);
        moveSpeed = baseMoveSpeed;
        speedEffectCoroutine = null;
        Debug.Log($"[PlayerMovementManager] {name} velocidad restaurada a {baseMoveSpeed}.");
    }

    // ── Entrega en edificio ───────────────────────────────────────────────

    /// <summary>Intenta entregar todo lo que lleva al edificio. Retorna true si había algo que entregar.</summary>
    public bool TryDeposit()
    {
        if (CollectedCount <= 0) return false;
        if (IsServer)
        {
            return DepositServerSide();
        }
        else
        {
            RequestDepositServerRpc();
            return true; // optimista, el servidor validará
        }
    }

    /// <summary>Versión solo-servidor. Mueve collectedCount -> deliveredScore y vacía inventario.</summary>
    public bool DepositServerSide()
    {
        if (!IsServer) return false;
        int amount = collectedCount.Value;
        if (amount <= 0) return false;
        collectedCount.Value = 0;
        deliveredScore.Value += amount;
        Debug.Log($"[PlayerMovementManager] {name} entregó {amount} -> puntuación {deliveredScore.Value}");
        return true;
    }

    /// <summary>Versión solo-servidor. Reinicia el puntaje entregado y el inventario a 0 para el nuevo juego.</summary>
    public void ResetScoreServerSide()
    {
        if (!IsServer) return;
        collectedCount.Value = 0;
        deliveredScore.Value = 0;
        powerUpQueue.Clear();
        activeVisualEffect.Value = 0;
        CancelInvoke(nameof(ClearVisualEffectServerSide));
        Debug.Log($"[PlayerMovementManager] Puntaje e inventario reiniciados a 0 para {name}");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDepositServerRpc()
    {
        DepositServerSide();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxCollected < 1) maxCollected = 1;
        if (maxPowerUps < 1) maxPowerUps = 1;
        if (moveSpeed < 0f) moveSpeed = 0f;
        if (turnSpeed < 0f) turnSpeed = 0f;
        if (slowMultiplier <= 0f) slowMultiplier = 0.5f;
        if (boostMultiplier <= 0f) boostMultiplier = 1.5f;
        if (powerUpEffectDuration <= 0f) powerUpEffectDuration = 5f;
    }
#endif

    public PlayerNetworkState GetNetworkState()
    {
        return new PlayerNetworkState
        {
            entityId = gameObject.name,
            isMoving = IsMoving,
            isCarrying = IsCarrying,
            collectedCount = CollectedCount,
            deliveredScore = Score,
            powerUpCount = PowerUpCount,
            slowPowerUpCount = SlowPowerUpCount,
            boostPowerUpCount = BoostPowerUpCount,
            activeVisualEffect = activeVisualEffect.Value,
            position = transform.position,
            rotation = transform.rotation
        };
    }

    private void SetIsMoving(bool moving)
    {
        if (IsMoving == moving) return;
        IsMoving = moving;
        IsMovingChanged?.Invoke(moving);
    }

    private void RefreshCarryingState()
    {
        bool carrying = IsCarrying;
        bool carryingChanged = lastCarrying != carrying;
        bool countChanged = lastCollectedCount != CollectedCount;
        bool scoreChanged = lastScore != Score;
        bool powerUpChanged = lastPowerUpCount != PowerUpCount;
        if (!carryingChanged && !countChanged && !scoreChanged && !powerUpChanged) return;
        lastCarrying = carrying;
        lastCollectedCount = CollectedCount;
        lastScore = Score;
        lastPowerUpCount = PowerUpCount;
        if (carryingChanged)
            IsCarryingChanged?.Invoke(carrying);
    }

    private void PublishPlayerState(bool force = false)
    {
        PlayerNetworkState state = GetNetworkState();
        if (!force && hasPublishedPlayerState && !PlayerStateDiffers(lastPublishedPlayerState, state))
            return;

        lastPublishedPlayerState = state;
        hasPublishedPlayerState = true;
        NetworkStateChanged?.Invoke(state);
        NetworkEventBus.Publish(state);
    }

    private static bool PlayerStateDiffers(PlayerNetworkState previous, PlayerNetworkState current)
    {
        if (previous.isMoving != current.isMoving) return true;
        if (previous.isCarrying != current.isCarrying) return true;
        if (previous.collectedCount != current.collectedCount) return true;
        if (previous.deliveredScore != current.deliveredScore) return true;
        if (previous.powerUpCount != current.powerUpCount) return true;
        if (previous.slowPowerUpCount != current.slowPowerUpCount) return true;
        if (previous.boostPowerUpCount != current.boostPowerUpCount) return true;
        if (previous.activeVisualEffect != current.activeVisualEffect) return true;
        if (Vector3.Distance(previous.position, current.position) > 0.001f) return true;
        if (Quaternion.Angle(previous.rotation, current.rotation) > 0.5f) return true;
        return false;
    }

    public void SetInteractableInRange(InteractableCube interactable, bool inRange)
    {
        if (inRange)
            currentInteractable = interactable;
        else if (currentInteractable == interactable)
            currentInteractable = null;
    }

    public void OnInteractableExitRange(InteractableCube interactable)
    {
        SetInteractableInRange(interactable, false);
        // No soltar aqui: al llevar el cubo, su trigger se mueve con el
        // y saldria del rango del jugador, lo que provocaba un Drop
        // inmediato (el "pequeno movimiento" reportado).
        // Soltar solo ocurre al pulsar Interact de nuevo.
    }
}