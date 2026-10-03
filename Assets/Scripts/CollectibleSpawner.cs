using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hitbox spawner para recolectables.
/// Colocar este componente en un GameObject con Collider en modo Trigger.
/// Cuando un jugador entra en la hitbox, solicita al <see cref="CollectibleSpawnManager"/>
/// que instancie un recolectable (respetando el límite global).
/// Requiere que el Manager administre el conteo y el límite.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CollectibleSpawner : MonoBehaviour
{
    [Tooltip("Cooldown entre intentos de spawn provocados por esta hitbox (segundos).")]
    [SerializeField] private float spawnCooldown = 1f;

    [Tooltip("Si es true, también reacciona a OnTriggerStay (útil si el jugador permanece dentro).")]
    [SerializeField] private bool spawnOnStay = false;

    [Tooltip("Solo jugadores con PlayerMovementManager activan el spawner.")]
    [SerializeField] private bool requirePlayer = true;

    [Tooltip("Si es true, desactiva visual o lógica cuando se alcanza el límite (opcional).")]
    [SerializeField] private bool logWhenLimitReached = true;

    private float lastSpawnTime = -999f;
    private Collider triggerCollider;

    /// <summary>Devuelve un punto aleatorio dentro del volumen de la hitbox trigger.</summary>
    public Vector3 GetRandomSpawnPosition()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        // BoxCollider: cálculo preciso en espacio local (respeta rotación y escala)
        if (triggerCollider is BoxCollider box)
        {
            Vector3 localRandom = new Vector3(
                Random.Range(-box.size.x * 0.5f, box.size.x * 0.5f),
                Random.Range(-box.size.y * 0.5f, box.size.y * 0.5f),
                Random.Range(-box.size.z * 0.5f, box.size.z * 0.5f)
            );
            Vector3 worldCenter = box.transform.TransformPoint(box.center);
            Vector3 rotatedOffset = box.transform.rotation * Vector3.Scale(localRandom, box.transform.lossyScale);
            // Fallback si lossyScale es 0 (raro): usar size directo
            return worldCenter + rotatedOffset;
        }

        // SphereCollider, CapsuleCollider, etc.: muestreo dentro de bounds con validación
        Bounds bounds = triggerCollider.bounds;
        for (int i = 0; i < 30; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z)
            );
            // ClosestPoint == candidate si está dentro (con tolerancia)
            Vector3 closest = triggerCollider.ClosestPoint(candidate);
            if (Vector3.Distance(closest, candidate) < 0.01f)
                return candidate;
        }
        // Fallback: centro
        return bounds.center;
    }

    public Quaternion GetSpawnRotation() => transform.rotation;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
        {
            Debug.LogWarning($"[CollectibleSpawner] El Collider de {name} no está en modo Trigger. Se forzará a Trigger para funcionar como hitbox.");
            triggerCollider.isTrigger = true;
        }
    }

    private void OnEnable()
    {
        CollectibleSpawnManager manager = CollectibleSpawnManager.Instance;
        if (manager != null) manager.RegisterSpawner(this);
    }

    private void OnDisable()
    {
        CollectibleSpawnManager manager = CollectibleSpawnManager.Instance;
        if (manager != null) manager.UnregisterSpawner(this);
    }

    private void Start()
    {
        // Registro tardío por si el Manager se creó después
        CollectibleSpawnManager manager = CollectibleSpawnManager.Instance;
        if (manager != null) manager.RegisterSpawner(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryRequestSpawn(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!spawnOnStay) return;
        TryRequestSpawn(other);
    }

    private void TryRequestSpawn(Collider other)
    {
        if (Time.time - lastSpawnTime < spawnCooldown) return;

        if (requirePlayer)
        {
            // Acepta tanto el collider del jugador como hijos
            PlayerMovementManager player = other.GetComponentInParent<PlayerMovementManager>();
            if (player == null) return;
        }

        CollectibleSpawnManager manager = CollectibleSpawnManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[CollectibleSpawner] No hay CollectibleSpawnManager en la escena.");
            return;
        }

        // Solo el servidor debe instanciar NetworkObjects; en tutorial offline se permite local.
        if (!IsServerLike())
            return;

        if (!manager.CanSpawn)
        {
            if (logWhenLimitReached)
                Debug.Log($"[CollectibleSpawner] Límite alcanzado ({manager.ActiveCount}/{manager.MaxInstances}), no se genera más en {name}.");
            return;
        }

        bool spawned = manager.RequestSpawnFromSpawner(this);
        if (spawned)
            lastSpawnTime = Time.time;
    }

    /// <summary>Permite forzar un spawn desde código (respeta límite y cooldown).</summary>
    public bool TryForceSpawn()
    {
        if (Time.time - lastSpawnTime < spawnCooldown) return false;
        CollectibleSpawnManager manager = CollectibleSpawnManager.Instance;
        if (manager == null || !manager.CanSpawn) return false;
        if (!IsServerLike()) return false;
        bool ok = manager.RequestSpawnFromSpawner(this);
        if (ok) lastSpawnTime = Time.time;
        return ok;
    }

    /// <summary>En red exige ser servidor; en tutorial offline siempre permite (instanciación local).</summary>
    private bool IsServerLike()
    {
        if (TutorialManager.IsTutorial && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            return true;
        if (NetworkManager.Singleton == null) return true;
        return NetworkManager.Singleton.IsServer;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spawnCooldown < 0f) spawnCooldown = 0f;
    }

    private void Reset()
    {
        // Asegurar trigger al crear el objeto
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }
#endif
}
