using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Animación procedural de flotación + giro para recolectables y power-ups.
/// - Flotación: movimiento vertical sinusoidal alrededor de la posición de spawn,
///   con el Rigidbody en modo sin gravedad (ignora la gravedad).
/// - Giro: rotación continua sobre su propio eje Y.
/// Cada instancia aleatoriza amplitud, frecuencia, velocidad de giro y fase
/// para que no todos se vean exactamente igual.
/// Este componente se añade automáticamente por código desde
/// <see cref="InteractableCube"/> y <see cref="PowerUpCube"/>, sin tocar prefabs.
/// </summary>
public class CollectibleFloatAnimation : MonoBehaviour
{
    [Header("Float (sine)")]
    [Tooltip("Rango aleatorio de amplitud vertical en metros (se sortea uno por instancia).")]
    [SerializeField] private Vector2 amplitudeRange = new Vector2(0.15f, 0.4f);
    [Tooltip("Rango aleatorio de frecuencia de flotación en ciclos por segundo (se sortea uno por instancia).")]
    [SerializeField] private Vector2 frequencyRange = new Vector2(0.6f, 1.6f);

    [Header("Spin (self axis)")]
    [Tooltip("Rango aleatorio de velocidad de giro en grados/segundo (valor absoluto).")]
    [SerializeField] private Vector2 spinSpeedRange = new Vector2(20f, 90f);
    [Tooltip("Si es true, la dirección de giro (horario/antihorario) también se sortea por instancia.")]
    [SerializeField] private bool randomizeSpinDirection = true;

    [Header("Physics")]
    [Tooltip("Si es true, desactiva la gravedad del Rigidbody por código para que flote.")]
    [SerializeField] private bool disableGravity = true;
    [Tooltip("Si es true, pone el Rigidbody en kinemático para que la física no lo tumbe ni lo hunda.")]
    [SerializeField] private bool makeKinematic = true;

    private float floatAmplitude;
    private float floatFrequency;
    private float spinSpeed;
    private float phase;
    private float elapsed;
    private Vector3 basePosition;
    private bool baseCaptured;

    private void Awake()
    {
        RandomizeParameters();
        ConfigureRigidbody();
    }

    private void Start()
    {
        CaptureBasePosition();
    }

    /// <summary>Sortea amplitud, frecuencia, velocidad de giro y fase. Un valor distinto por instancia.</summary>
    private void RandomizeParameters()
    {
        floatAmplitude = Random.Range(amplitudeRange.x, amplitudeRange.y);
        floatFrequency = Random.Range(frequencyRange.x, frequencyRange.y);
        spinSpeed = Random.Range(spinSpeedRange.x, spinSpeedRange.y);
        if (randomizeSpinDirection && Random.value < 0.5f)
            spinSpeed = -spinSpeed;
        phase = Random.Range(0f, Mathf.PI * 2f);
        elapsed = Random.Range(0f, 10f);
    }

    /// <summary>Ignora la gravedad por código para que el objeto flote.</summary>
    private void ConfigureRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) return;
        if (disableGravity)
            rb.useGravity = false;
        if (makeKinematic)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void CaptureBasePosition()
    {
        basePosition = transform.position;
        baseCaptured = true;
    }

    /// <summary>
    /// Re-ancla la posición base (llamar tras el spawn de red, por si la posición
    /// final se fijó después de Start). Lo invocan InteractableCube/PowerUpCube.
    /// </summary>
    public void NotifySpawned()
    {
        ConfigureRigidbody();
        CaptureBasePosition();
    }

    private void Update()
    {
        if (!baseCaptured)
            CaptureBasePosition();

        // Si nos quitaron el cubo dueño (p.ej. bloque de podio reutilizando el prefab),
        // desactivarse para no mover un objeto que ya no es recolectable.
        if (GetComponent<InteractableCube>() == null && GetComponent<PowerUpCube>() == null)
        {
            enabled = false;
            return;
        }

        // No animar mientras esté cargado por un jugador o ya recolectado.
        InteractableCube cube = GetComponent<InteractableCube>();
        if (cube != null && (cube.IsBeingCarried || cube.IsCollected))
            return;
        PowerUpCube powerUp = GetComponent<PowerUpCube>();
        if (powerUp != null && powerUp.IsCollected)
            return;
        // Si cuelga de un jugador (visual equipado), no pelear con su transform.
        if (GetComponentInParent<PlayerMovementManager>() != null && transform.parent != null)
            return;

        // Solo el servidor anima la posición autoritativa; los clientes la reciben
        // por replicación del NetworkObject (SynchronizeTransform). En offline/tutorial
        // sin red, se anima siempre en local.
        if (IsReplicatedClient())
            return;

        elapsed += Time.deltaTime;

        float yOffset = Mathf.Sin(elapsed * floatFrequency * Mathf.PI * 2f + phase) * floatAmplitude;
        transform.position = basePosition + Vector3.up * yOffset;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
    }

    private bool IsReplicatedClient()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return false;
        if (!TryGetComponent(out NetworkObject netObj))
            return false;
        if (!netObj.IsSpawned)
            return false;
        return !NetworkManager.Singleton.IsServer;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (amplitudeRange.x < 0f) amplitudeRange.x = 0f;
        if (amplitudeRange.y < amplitudeRange.x) amplitudeRange.y = amplitudeRange.x;
        if (frequencyRange.x < 0.01f) frequencyRange.x = 0.01f;
        if (frequencyRange.y < frequencyRange.x) frequencyRange.y = frequencyRange.x;
        if (spinSpeedRange.x < 0f) spinSpeedRange.x = 0f;
        if (spinSpeedRange.y < spinSpeedRange.x) spinSpeedRange.y = spinSpeedRange.x;
    }
#endif
}
