using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class InteractableCube : NetworkBehaviour
{
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 1f, 1.5f);
    [SerializeField] private float interactionRange = 1.3f;
    [SerializeField] private float carrySmoothTime = 8f;
    [SerializeField] private float maxPickupDistance = 3f;

    private Collider solidCollider;
    private Collider interactionCollider;
    private Collider playerCollider;
    private Rigidbody cubeRigidbody;

    private bool isCarried; // legacy, ya no se usa para carga pero se mantiene para compatibilidad
    private Transform carrier;
    private bool isCollected;

    public event Action<bool> IsBeingCarriedChanged;
    public event Action<bool> IsGroundedChanged;
    public event Action<CubeNetworkState> NetworkStateChanged;
    public event Action<PlayerMovementManager> Collected;

    public bool IsBeingCarried => isCarried;
    public bool IsGrounded { get; private set; }
    public bool IsCollected => isCollected;

    private bool lastCarrying;
    private bool groundTouchThisStep;
    private CubeNetworkState lastPublishedCubeState;
    private bool hasPublishedCubeState;
    private float spawnTime;
    [Tooltip("Tiempo de protección tras spawnear para no ser recolectado instantáneamente si spawnea sobre el jugador.")]
    [SerializeField] private float collectProtectionTime = 0.5f;

    [Header("Collect Audio")]
    [Tooltip("Sonido al recolectar. Se asigna desde el prefab.")]
    [SerializeField] private AudioClip collectSound;
    [Tooltip("Volumen del sonido de recolección (0 a 1).")]
    [Range(0f, 1f)]
    [SerializeField] private float collectSoundVolume = 1f;

    [Header("Model Variants")]
    [Tooltip("Modelos visuales hijos entre los que se sortea uno al instanciar (ej. los 8 modelos del prefab). Si se deja vacío, se detectan solos los hijos directos desactivados por defecto.")]
    [SerializeField] private List<GameObject> modelVariants = new List<GameObject>();

    [Tooltip("Índice de la variante visible (-1 = sin sortear). Solo el servidor escribe, todos aplican.")]
    private NetworkVariable<int> activeVariantIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly List<GameObject> resolvedVariants = new List<GameObject>();
    private bool variantsResolved;

    private void Awake()
    {
        spawnTime = Time.time;
        cubeRigidbody = GetComponent<Rigidbody>();
        foreach (Collider c in GetComponents<Collider>())
        {
            if (!c.isTrigger && solidCollider == null)
                solidCollider = c;
            else if (c.isTrigger && interactionCollider == null)
                interactionCollider = c;
        }

        if (solidCollider == null)
            solidCollider = gameObject.AddComponent<BoxCollider>();

        if (interactionCollider == null)
        {
            BoxCollider trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            if (solidCollider is BoxCollider boxSolid)
                trigger.size = boxSolid.size * interactionRange;
            interactionCollider = trigger;
        }

        EnsureFloatAnimation();
    }

    /// <summary>
    /// Añade por código la animación de flotar + girar si el prefab no la trae.
    /// Evita tocar el prefab/escena: funciona para instancias ya colocadas y spawneadas.
    /// </summary>
    private void EnsureFloatAnimation()
    {
        if (GetComponent<CollectibleFloatAnimation>() == null)
            gameObject.AddComponent<CollectibleFloatAnimation>();
    }

    // ── Variantes visuales (un modelo visible por instancia) ────────────────

    /// <summary>Sorteo previo al Spawn de red (solo servidor): el índice queda listo y viaja replicado con el spawn.</summary>
    public void RollVariantServerSide()
    {
        if (IsSpawned) return;
        int index = DrawRandomVariantIndex();
        if (index < 0) return;
        activeVariantIndex.Value = index;
        ApplyVariant(index);
    }

    private void OnVariantIndexChanged(int previous, int current)
    {
        ApplyVariant(current);
    }

    /// <summary>Sortea el índice de la variante a mostrar. Retorna -1 si no hay variantes.</summary>
    private int DrawRandomVariantIndex()
    {
        ResolveVariants();
        if (resolvedVariants.Count == 0) return -1;
        return UnityEngine.Random.Range(0, resolvedVariants.Count);
    }

    /// <summary>Activa solo la variante indicada y apaga las demás. Sin variantes, no hace nada.</summary>
    private void ApplyVariant(int index)
    {
        ResolveVariants();
        if (resolvedVariants.Count == 0) return;
        if (index < 0 || index >= resolvedVariants.Count)
        {
            index = UnityEngine.Random.Range(0, resolvedVariants.Count);
        }
        for (int i = 0; i < resolvedVariants.Count; i++)
        {
            if (resolvedVariants[i] != null)
                resolvedVariants[i].SetActive(index == i);
        }
    }

    /// <summary>
    /// Resuelve los modelos una sola vez: los asignados en el prefab o, si la
    /// lista está vacía, los hijos directos desactivados por defecto.
    /// Se cachea porque al activar una variante dejaría de detectarse.
    /// </summary>
    private void ResolveVariants()
    {
        if (variantsResolved) return;
        variantsResolved = true;
        foreach (GameObject go in modelVariants)
        {
            if (go != null && !resolvedVariants.Contains(go))
                resolvedVariants.Add(go);
        }
        if (resolvedVariants.Count > 0) return;
        foreach (Transform child in transform)
        {
            if (child != null && !child.gameObject.activeSelf)
                resolvedVariants.Add(child.gameObject);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        spawnTime = Time.time;
        GetComponent<CollectibleFloatAnimation>()?.NotifySpawned();
        CollectibleSpawnManager.Instance?.Register(this);
        activeVariantIndex.OnValueChanged += OnVariantIndexChanged;
        // El servidor sortea si aún no hay índice (p.ej. objeto colocado en escena); los clientes aplican el replicado.
        if (IsServer && activeVariantIndex.Value < 0)
        {
            activeVariantIndex.Value = DrawRandomVariantIndex();
        }
        ApplyVariant(activeVariantIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        activeVariantIndex.OnValueChanged -= OnVariantIndexChanged;
        CollectibleSpawnManager.Instance?.Unregister(this);
        base.OnNetworkDespawn();
    }

    private void OnEnable()
    {
        // Fallback para objetos en escena antes de OnNetworkSpawn o modo offline
        // HashSet evita duplicados, así que es seguro llamar aquí también
        CollectibleSpawnManager.Instance?.Register(this);
    }

    private void OnDisable()
    {
        CollectibleSpawnManager.Instance?.Unregister(this);
    }

    private void OnDestroy()
    {
        CollectibleSpawnManager.Instance?.Unregister(this);
    }

    private void Start()
    {
        RefreshCarriedState();
        RefreshGroundedState(groundTouchThisStep || cubeRigidbody == null);
        PublishCubeState(true);
        // Sin red (tutorial offline u objeto aún no spawneado): sorteo local.
        // En red el servidor corrige con el índice autoritativo en OnNetworkSpawn.
        if (!IsSpawned)
        {
            ApplyVariant(DrawRandomVariantIndex());
        }
    }

    private void FixedUpdate()
    {
        if (isCarried)
        {
            SetGrounded(false);
        }
        else if (cubeRigidbody == null)
        {
            SetGrounded(true);
        }
        else
        {
            SetGrounded(groundTouchThisStep);
        }
        groundTouchThisStep = false;

        PublishCubeState();
    }

    private void OnCollisionEnter(Collision collision)
    {
        RegisterGroundContacts(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RegisterGroundContacts(collision);
    }

    private void RegisterGroundContacts(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (Mathf.Abs(contact.normal.y) > 0.5f)
            {
                groundTouchThisStep = true;
                break;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollectFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollectFromCollider(other);
    }

    private void OnTriggerExit(Collider other)
    {
        // Sin lógica de rango: la recolección es automática al tocar la hitbox.
        // Se deja vacío por compatibilidad.
        if (other == playerCollider) playerCollider = null;
    }

    private void TryCollectFromCollider(Collider other)
    {
        // Solo procesar detección por trigger en el servidor/host.
        // Los clientes usan detección por proximidad desde PlayerMovementManager
        // para evitar problemas de ownership en los RPCs.
        if (IsSpawned && !IsServer) return;

        if (isCollected || isCarried) return;
        if (Time.time - spawnTime < collectProtectionTime) return;

        PlayerMovementManager player = other.GetComponentInParent<PlayerMovementManager>();
        if (player == null) return;

        playerCollider = other;
        TryCollect(player);
    }

    private void LateUpdate()
    {
        if (!isCarried || carrier == null) return;

        Vector3 target = carrier.TransformPoint(holdOffset);
        transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-carrySmoothTime * Time.deltaTime));
        PublishCubeState();
    }

    // ── Nueva lógica: recolección automática al tocar la hitbox ──────────────

    /// <summary>Intenta recolectar automáticamente al pasar por la hitbox. Solo requiere contacto trigger.</summary>
    public bool TryCollect(PlayerMovementManager player)
    {
        if (isCollected || player == null) return false;
        if (player.IsInventoryFull) return false;

        if (TutorialManager.IsTutorial && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            return PerformCollectOffline(player);
        }

        if (IsServer)
        {
            return PerformCollect(player.NetworkObjectId);
        }
        else
        {
            RequestCollectServerRpc(player.NetworkObjectId);
            return true;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCollectServerRpc(ulong playerNetworkObjectId)
    {
        PerformCollect(playerNetworkObjectId);
    }

    /// <summary>Reproduce el sonido de recolección SOLO en el cliente que recolectó (3D posicional en el punto de recolección).</summary>
    [Rpc(SendTo.SpecifiedInParams)]
    private void PlayCollectSoundClientRpc(Vector3 collectPosition, RpcParams rpcParams = default)
    {
        PlayCollectSoundAt(collectSound, collectPosition, collectSoundVolume);
    }

    /// <summary>Reproduce un clip puntual sin depender del GameObject (sobrevive al despawn/destroy). Respeta el volumen de efectos.</summary>
    public static void PlayCollectSoundAt(AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null) return;
        GameAudioManager.PlaySfxAtPoint(clip, position, volume);
    }

    private bool PerformCollect(ulong playerNetworkObjectId)
    {
        if (isCollected) return false;

        // Validar límite antes de marcar como recolectado
        PlayerMovementManager targetPlayer = null;
        NetworkObject playerObj = null;
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out playerObj))
        {
            targetPlayer = playerObj.GetComponent<PlayerMovementManager>();
            if (targetPlayer != null && targetPlayer.IsInventoryFull)
            {
                Debug.Log($"[InteractableCube] {name} no recolectado: inventario lleno de {targetPlayer.name} ({targetPlayer.CollectedCount}/{targetPlayer.MaxCollected}).");
                return false;
            }
        }
        else
        {
            Debug.LogWarning($"[InteractableCube] No se encontró Player {playerNetworkObjectId} para recolectar {name}.");
            return false;
        }

        isCollected = true;

        bool added = targetPlayer.AddCollectedServerSide(1);
        if (!added)
        {
            // Límite alcanzado entre validación y escritura (race condition) -> cancelar
            isCollected = false;
            Debug.Log($"[InteractableCube] {name} recolección cancelada: inventario lleno.");
            return false;
        }
        Collected?.Invoke(targetPlayer);

        // Sonido solo para quien recolectó, antes de desaparecer (el objeto se destruye y no podria reproducirlo).
        PlayCollectSoundClientRpc(transform.position, RpcTarget.Single(playerObj.OwnerClientId, RpcTargetUse.Temp));

        // Desaparecer automáticamente (Network despawn). Libera slot en CollectibleSpawnManager.
        PublishCubeState(true);
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
        return true;
    }

    /// <summary>
    /// Recolección offline (tutorial sin red): usa la referencia directa al jugador,
    /// sin NetworkObjectId ni RPCs. Destruye el objeto con Destroy normal.
    /// </summary>
    private bool PerformCollectOffline(PlayerMovementManager targetPlayer)
    {
        if (isCollected || targetPlayer == null) return false;
        if (TutorialManager.IsTutorialDummyPlayer(targetPlayer))
        {
            // Excepción del tutorial: el dummy recoge constantemente lo que colisiona
            // sin que se le cuente el inventario (siempre tiene espacio, nunca entrega).
            isCollected = true;
            PublishCubeState(true);
            Destroy(gameObject);
            return true;
        }
        if (targetPlayer.IsInventoryFull) return false;

        isCollected = true;
        bool added = targetPlayer.AddCollected(1);
        if (!added)
        {
            isCollected = false;
            return false;
        }
        Collected?.Invoke(targetPlayer);
        PublishCubeState(true);
        PlayCollectSoundAt(collectSound, transform.position, collectSoundVolume);
        Destroy(gameObject);
        return true;
    }

    // ── Legacy: pickup manual ya no se usa, se mantiene por compatibilidad ──────

    [Obsolete("Usa TryCollect: la recolección ahora es automática por trigger.")]
    public bool TryPickUp(PlayerMovementManager player)
    {
        return TryCollect(player);
    }

    [Obsolete("Ya no hay drop manual, el objeto desaparece al recolectarse.")]
    public void Drop() { }

    /// <summary>Ignora o restaura colisiones entre todos los colliders del jugador y el solidCollider del cubo.</summary>
    private void IgnorePlayerCollision(NetworkObject playerObj, bool ignore)
    {
        if (solidCollider == null) return;
        foreach (Collider col in playerObj.GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger)
                Physics.IgnoreCollision(col, solidCollider, ignore);
        }
    }

    public CubeNetworkState GetNetworkState()
    {
        return new CubeNetworkState
        {
            entityId = gameObject.name,
            isBeingCarried = isCarried,
            isGrounded = IsGrounded,
            position = transform.position
        };
    }

    private void SetGrounded(bool grounded)
    {
        if (IsGrounded == grounded) return;
        IsGrounded = grounded;
        IsGroundedChanged?.Invoke(grounded);
    }

    private void RefreshGroundedState(bool grounded)
    {
        SetGrounded(grounded);
    }

    private void RefreshCarriedState()
    {
        if (lastCarrying == isCarried) return;
        lastCarrying = isCarried;
        IsBeingCarriedChanged?.Invoke(isCarried);
    }

    private void PublishCubeState(bool force = false)
    {
        RefreshCarriedState();
        CubeNetworkState state = GetNetworkState();
        if (!force && hasPublishedCubeState && !CubeStateDiffers(lastPublishedCubeState, state))
            return;

        lastPublishedCubeState = state;
        hasPublishedCubeState = true;
        NetworkStateChanged?.Invoke(state);
        NetworkEventBus.Publish(state);
    }

    private static bool CubeStateDiffers(CubeNetworkState previous, CubeNetworkState current)
    {
        if (previous.isBeingCarried != current.isBeingCarried) return true;
        if (previous.isGrounded != current.isGrounded) return true;
        if (Vector3.Distance(previous.position, current.position) > 0.001f) return true;
        return false;
    }
}