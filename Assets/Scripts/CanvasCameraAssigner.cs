using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Asigna y actualiza automáticamente la render camera (worldCamera) de un Canvas 
/// configurado en modo Screen Space - Camera.
/// En el Lobby se vincula a la Main Camera de la escena del Lobby.
/// En la escena de juego se vincula a la Main Camera activa (cámara del personaje).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class CanvasCameraAssigner : MonoBehaviour
{
    [Tooltip("Nombre de la escena de Lobby.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    private Canvas targetCanvas;

    private void Awake()
    {
        targetCanvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        AssignCamera();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        AssignCamera();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AssignCamera();
    }

    private void Update()
    {
        // Si el worldCamera es nulo o fue destruido al cambiar de escena, intentar reasignarlo.
        if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && targetCanvas.worldCamera == null)
        {
            AssignCamera();
        }
    }

    /// <summary>
    /// Asigna la cámara principal activa al Canvas.
    /// En la escena de juego busca primero la PlayerCamera del jugador local (IsOwner).
    /// </summary>
    public void AssignCamera()
    {
        if (targetCanvas == null)
            targetCanvas = GetComponent<Canvas>();

        if (targetCanvas == null || targetCanvas.renderMode != RenderMode.ScreenSpaceCamera)
            return;

        // En escenas de juego (no Lobby), intentar encontrar la cámara del jugador local
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool inLobby = (currentScene == lobbySceneName);

        if (!inLobby)
        {
            // Buscar la PlayerCamera del jugador local (IsOwner) que esté habilitada.
            // En tutorial offline IsOwner es false (sin red): aceptar el único jugador en escena.
            bool isOfflineTutorial = TutorialManager.IsTutorial
                && (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening);
            foreach (var pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
            {
                if (pm.IsOwner || isOfflineTutorial)
                {
                    Camera playerCam = pm.GetComponentInChildren<Camera>(true);
                    if (playerCam != null && playerCam.enabled)
                    {
                        targetCanvas.worldCamera = playerCam;
                        return;
                    }
                }
            }
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            targetCanvas.worldCamera = mainCam;
        }
        else
        {
            // Buscar cualquier cámara activa como fallback si Camera.main no está etiquetada adecuadamente
            Camera anyCam = FindFirstObjectByType<Camera>();
            if (anyCam != null)
            {
                targetCanvas.worldCamera = anyCam;
            }
        }
    }
}
