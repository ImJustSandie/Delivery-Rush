using UnityEngine;

/// <summary>
/// Controla las animaciones de Idle y Run (Correr) del personaje/dummy basándose en la velocidad de movimiento.
/// </summary>
public class PlayerAnimationController : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Animator del modelo 3D. Si se deja nulo, intentará obtenerlo del mismo objeto o de sus hijos.")]
    [SerializeField] private Animator animator;

    [Tooltip("Referencia al PlayerMovementManager para consultar el estado de movimiento (opcional).")]
    [SerializeField] private PlayerMovementManager movementManager;

    [Header("Parámetros del Animator")]
    [Tooltip("Nombre del parámetro bool o float en el Animator Controller para controlar la animación (ej: IsMoving o Speed).")]
    [SerializeField] private string movingBoolParam = "IsMoving";
    [SerializeField] private string speedFloatParam = "Speed";

    [Header("Configuración")]
    [Tooltip("Velocidad mínima para considerar que el personaje se está moviendo (si no se usa PlayerMovementManager).")]
    [SerializeField] private float speedThreshold = 0.1f;

    private Vector3 lastPosition;
    private int movingBoolHash;
    private int speedFloatHash;
    private bool hasMovingBool;
    private bool hasSpeedFloat;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (movementManager == null)
        {
            movementManager = GetComponent<PlayerMovementManager>();
        }

        if (!string.IsNullOrEmpty(movingBoolParam))
        {
            movingBoolHash = Animator.StringToHash(movingBoolParam);
        }

        if (!string.IsNullOrEmpty(speedFloatParam))
        {
            speedFloatHash = Animator.StringToHash(speedFloatParam);
        }

        // Verificar si los parámetros realmente existen en el Animator Controller
        CacheExistingParameters();

        lastPosition = transform.position;
    }

    /// <summary>
    /// Recorre los parámetros del AnimatorController una sola vez para saber
    /// cuáles existen de verdad, evitando la advertencia "Parameter does not exist".
    /// </summary>
    private void CacheExistingParameters()
    {
        hasMovingBool = false;
        hasSpeedFloat = false;

        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.nameHash == movingBoolHash && param.type == AnimatorControllerParameterType.Bool)
            {
                hasMovingBool = true;
            }
            else if (param.nameHash == speedFloatHash && param.type == AnimatorControllerParameterType.Float)
            {
                hasSpeedFloat = true;
            }
        }
    }

    private void Update()
    {
        if (animator == null) return;

        bool isMoving = false;
        float currentSpeed = 0f;

        // Opción 1: Si tenemos PlayerMovementManager, usar su propiedad IsMoving
        if (movementManager != null)
        {
            isMoving = movementManager.IsMoving;
            currentSpeed = isMoving ? movementManager.MoveSpeed : 0f;
        }
        else
        {
            // Opción 2: Para Dummies o Npcs sin PlayerMovementManager, calcular velocidad a partir del desplazamiento global
            Vector3 horizontalDelta = transform.position - lastPosition;
            horizontalDelta.y = 0f; // ignorar movimiento vertical/gravedad
            currentSpeed = horizontalDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            isMoving = currentSpeed > speedThreshold;
            lastPosition = transform.position;
        }

        // Actualizar parámetros en el Animator solo si realmente existen en el Controller
        if (hasMovingBool)
        {
            animator.SetBool(movingBoolHash, isMoving);
        }

        if (hasSpeedFloat)
        {
            animator.SetFloat(speedFloatHash, currentSpeed);
        }
    }
}
