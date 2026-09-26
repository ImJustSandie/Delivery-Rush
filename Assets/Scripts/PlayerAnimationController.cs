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

    [Header("Parámetros de Resultado / Podio")]
    [Tooltip("Nombre del parámetro Trigger o Bool para la animación de victoria / celebración (ej: Victory, Celebrate, IsVictory).")]
    [SerializeField] private string victoryParam = "Victory";
    [Tooltip("Nombre del parámetro Trigger o Bool para la animación de derrota (ej: Defeat, Sad, IsDefeat).")]
    [SerializeField] private string defeatParam = "Defeat";

    [Header("Configuración")]
    [Tooltip("Velocidad mínima para considerar que el personaje se está moviendo (si no se usa PlayerMovementManager).")]
    [SerializeField] private float speedThreshold = 0.1f;

    private Vector3 lastPosition;
    private int movingBoolHash;
    private int speedFloatHash;
    private bool hasMovingBool;
    private bool hasSpeedFloat;

    private int victoryHash;
    private int defeatHash;
    private AnimatorControllerParameterType victoryParamType;
    private AnimatorControllerParameterType defeatParamType;
    private bool hasVictoryParam;
    private bool hasDefeatParam;

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

        if (!string.IsNullOrEmpty(victoryParam))
        {
            victoryHash = Animator.StringToHash(victoryParam);
        }

        if (!string.IsNullOrEmpty(defeatParam))
        {
            defeatHash = Animator.StringToHash(defeatParam);
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
        hasVictoryParam = false;
        hasDefeatParam = false;

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
            else if (param.nameHash == victoryHash)
            {
                hasVictoryParam = true;
                victoryParamType = param.type;
            }
            else if (param.nameHash == defeatHash)
            {
                hasDefeatParam = true;
                defeatParamType = param.type;
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

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        // Si al activarse no estamos en Podio (ej: volviendo al Lobby), resetear animación de resultado
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!currentScene.Equals("Podium", System.StringComparison.OrdinalIgnoreCase))
        {
            ResetResultAnimations();
        }
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Al cargar cualquier escena que no sea Podio (ej: Lobby), volver a Idle
        if (!scene.name.Equals("Podium", System.StringComparison.OrdinalIgnoreCase))
        {
            ResetResultAnimations();
        }
    }

    /// <summary>
    /// Reproduce la animación de victoria / celebración (1er lugar).
    /// </summary>
    public void PlayVictoryAnimation()
    {
        if (animator == null) return;
        if (!hasVictoryParam) CacheExistingParameters();

        if (hasVictoryParam)
        {
            if (victoryParamType == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(victoryHash);
            }
            else if (victoryParamType == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(victoryHash, true);
                if (hasDefeatParam && defeatParamType == AnimatorControllerParameterType.Bool)
                    animator.SetBool(defeatHash, false);
            }
        }
    }

    /// <summary>
    /// Reproduce la animación de derrota (2º, 3º, 4º lugar).
    /// </summary>
    public void PlayDefeatAnimation()
    {
        if (animator == null) return;
        if (!hasDefeatParam) CacheExistingParameters();

        if (hasDefeatParam)
        {
            if (defeatParamType == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(defeatHash);
            }
            else if (defeatParamType == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(defeatHash, true);
                if (hasVictoryParam && victoryParamType == AnimatorControllerParameterType.Bool)
                    animator.SetBool(victoryHash, false);
            }
        }
    }

    /// <summary>
    /// Activa la animación adecuada dependiendo de si es primer lugar o no.
    /// </summary>
    public void PlayResultAnimation(bool isFirstPlace)
    {
        if (isFirstPlace)
        {
            PlayVictoryAnimation();
        }
        else
        {
            PlayDefeatAnimation();
        }
    }

    /// <summary>
    /// Resetea los parámetros de victoria/derrota y permite que el personaje vuelva a Idle.
    /// </summary>
    public void ResetResultAnimations()
    {
        if (animator == null) return;
        if (!hasVictoryParam && !hasDefeatParam) CacheExistingParameters();

        if (hasVictoryParam)
        {
            if (victoryParamType == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(victoryHash);
            }
            else if (victoryParamType == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(victoryHash, false);
            }
        }

        if (hasDefeatParam)
        {
            if (defeatParamType == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(defeatHash);
            }
            else if (defeatParamType == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(defeatHash, false);
            }
        }
    }
}
