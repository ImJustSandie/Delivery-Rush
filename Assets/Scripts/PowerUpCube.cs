using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Efectos disponibles del power-up. Random elige uno de los dos al usarlo (servidor).</summary>
public enum PowerUpEffect
{
    SlowOthers = 0,
    BoostSelf = 1,
    Random = 2
}

/// <summary>
/// Recolectable raro (power-up). Misma lógica de aparición que el cubo normal
/// (lo instancia <see cref="CollectibleSpawnManager"/> con ~10% de probabilidad).
/// Al recogerse se sortea su tipo (ralentizar o velocidad, según
/// <see cref="CollectibleSpawnManager.SlowPowerUpChance"/>) y va al inventario
/// de power-ups del jugador (<see cref="PlayerMovementManager.AddPowerUpServerSide"/>).
/// El jugador lo guarda hasta usarlo con un botón de la UI.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PowerUpCube : NetworkBehaviour
{
    [Tooltip("Tiempo de protección tras spawnear para no ser recolectado instantáneamente.")]
    [SerializeField] private float collectProtectionTime = 0.5f;

    private Collider solidCollider;
    private Collider interactionCollider;
    private float spawnTime;
    private bool isCollected;

    public event Action<PlayerMovementManager> Collected;

    public bool IsCollected => isCollected;

    private void Awake()
    {
        spawnTime = Time.time;
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
                trigger.size = boxSolid.size * 1.5f;
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

    private void OnTriggerEnter(Collider other)
    {
        TryCollectFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollectFromCollider(other);
    }

    private void TryCollectFromCollider(Collider other)
    {
        // Solo el servidor procesa triggers (los clientes usan proximidad desde el Player).
        if (IsSpawned && !IsServer) return;
        if (isCollected) return;
        if (Time.time - spawnTime < collectProtectionTime) return;

        PlayerMovementManager player = other.GetComponentInParent<PlayerMovementManager>();
        if (player == null) return;

        TryCollect(player);
    }

    /// <summary>
    /// Intenta recolectar. En servidor ejecuta directo; en cliente pide vía RPC del cubo
    /// (solo funciona si el invocador tiene permiso; el path fiable para clientes
    /// es <see cref="PlayerMovementManager"/> por proximidad).
    /// </summary>
    public bool TryCollect(PlayerMovementManager player)
    {
        if (isCollected || player == null) return false;
        if (!player.CanCarryPowerUp) return false;

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

        PlayerMovementManager targetPlayer = null;
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerObj))
        {
            targetPlayer = playerObj.GetComponent<PlayerMovementManager>();
            if (targetPlayer == null) return false;
            if (!targetPlayer.CanCarryPowerUp)
            {
                Debug.Log($"[PowerUpCube] {name} no recolectado: inventario de power-ups lleno de {targetPlayer.name}.");
                return false;
            }
        }
        else
        {
            Debug.LogWarning($"[PowerUpCube] No se encontró Player {playerNetworkObjectId} para recolectar {name}.");
            return false;
        }

        isCollected = true;

        // Sorteo al recoger: el tipo queda fijado en el inventario (no al usarlo).
        // La probabilidad se administra en CollectibleSpawnManager (slowPowerUpChance).
        float slowChance = 0.5f;
        if (CollectibleSpawnManager.Instance != null)
            slowChance = CollectibleSpawnManager.Instance.SlowPowerUpChance;
        PowerUpEffect rolled = UnityEngine.Random.value < slowChance ? PowerUpEffect.SlowOthers : PowerUpEffect.BoostSelf;

        bool added = targetPlayer.AddPowerUpServerSide(rolled, 1);
        if (!added)
        {
            isCollected = false;
            Debug.Log($"[PowerUpCube] {name} recolección cancelada: inventario de power-ups lleno.");
            return false;
        }
        Debug.Log($"[PowerUpCube] {targetPlayer.name} recogió power-up {rolled} (ralentizar={targetPlayer.SlowPowerUpCount} velocidad={targetPlayer.BoostPowerUpCount}).");
        Collected?.Invoke(targetPlayer);

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
        else
            Destroy(gameObject);
        return true;
    }
}
