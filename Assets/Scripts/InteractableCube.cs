using System;
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
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        spawnTime = Time.time;
        CollectibleSpawnManager.Instance?.Register(this);
    }

    public override void OnNetworkDespawn()
    {
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