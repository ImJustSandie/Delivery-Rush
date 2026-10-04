using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Edificio con hitbox trigger donde el jugador entrega los objetos que lleva.
/// Colocar en el GameObject del edificio con un Collider en modo Trigger.
/// Al entrar el jugador, vacía su inventario (collectedCount -> deliveredScore) y le permite seguir recogiendo.
/// Solo el servidor valida la entrega, pero el cliente la solicita vía PlayerMovementManager.TryDeposit().
/// </summary>
[RequireComponent(typeof(Collider))]
public class DeliveryBuilding : MonoBehaviour
{
    [Header("Entrega")]
    [Tooltip("Si es true, entrega automáticamente al entrar en la hitbox.")]
    [SerializeField] private bool depositOnEnter = true;
    [Tooltip("Si es true, también entrega en OnTriggerStay (útil si el jugador permanece dentro y recoge más luego).")]
    [SerializeField] private bool depositOnStay = false;
    [Tooltip("Cooldown por jugador entre entregas (segundos). Evita spam si stay está activo.")]
    [SerializeField] private float depositCooldown = 0.5f;
    [Tooltip("Solo jugadores con PlayerMovementManager activan la entrega.")]
    [SerializeField] private bool requirePlayer = true;

    [Header("Feedback")]
    [Tooltip("Log cuando un jugador entrega.")]
    [SerializeField] private bool logOnDeposit = true;

    /// <summary>Evento emitido cuando un jugador realiza un depósito de puntos exitoso.</summary>
    public event System.Action<PlayerMovementManager> OnDepositSuccessful;

    private Collider triggerCollider;
    private readonly Dictionary<ulong, float> lastDepositByPlayer = new Dictionary<ulong, float>();

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
        {
            Debug.LogWarning($"[DeliveryBuilding] El Collider de {name} no está en modo Trigger. Se forzará a Trigger.");
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!depositOnEnter) return;
        TryDepositFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!depositOnStay) return;
        TryDepositFromCollider(other);
    }

    private void TryDepositFromCollider(Collider other)
    {
        PlayerMovementManager player = requirePlayer
            ? other.GetComponentInParent<PlayerMovementManager>()
            : other.GetComponentInParent<PlayerMovementManager>();

        if (player == null) return;
        if (player.CollectedCount <= 0) return;

        // Cooldown por jugador
        ulong playerId = 0;
        var netObj = player.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) playerId = netObj.NetworkObjectId;

        if (lastDepositByPlayer.TryGetValue(playerId, out float lastTime))
        {
            if (Time.time - lastTime < depositCooldown) return;
        }

        bool deposited = player.TryDeposit();
        if (deposited)
        {
            lastDepositByPlayer[playerId] = Time.time;
            OnDepositSuccessful?.Invoke(player);
            if (logOnDeposit)
                Debug.Log($"[DeliveryBuilding] {player.name} entregó en {name} -> puntuación {player.Score} | inventario {player.CollectedCount}/{player.MaxCollected}");
        }
    }

    /// <summary>Permite forzar entrega desde código/UI.</summary>
    public bool TryForceDeposit(PlayerMovementManager player)
    {
        if (player == null || player.CollectedCount <= 0) return false;
        bool deposited = player.TryDeposit();
        if (deposited)
        {
            OnDepositSuccessful?.Invoke(player);
        }
        return deposited;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (depositCooldown < 0f) depositCooldown = 0f;
    }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }
#endif
}
