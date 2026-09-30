using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Objeto de configuración exclusivo de TutorialScene (lógica del tutorial sin red).
/// Centraliza en el Inspector lo que antes se detectaba por código:
/// dummy(s) de prueba, su nombre visible y opciones del tutorial.
///
/// PASOS MANUALES EN EL EDITOR (el código no puede hacerlos):
/// 1. En TutorialScene, crear un GameObject vacío llamado "TutorialSetup".
/// 2. Agregarle este componente (Add Component → TutorialSceneSetup).
/// 3. (Opcional) Arrastrar el/los jugadores de prueba a la lista "Dummy Players".
///    Si la lista queda vacía, igual se detecta por código cualquier jugador extra
///    en la escena (o cuyo nombre contenga "Dummy") como dummy.
/// 4. NO vincular aquí al jugador local: se instancia solo por código al entrar.
///
/// En partida online (MainScene/Lobby) este componente no hace nada aunque exista.
/// </summary>
public class TutorialSceneSetup : MonoBehaviour
{
    public static TutorialSceneSetup Instance { get; private set; }

    [Header("Dummy de prueba (rival del tutorial)")]
    [Tooltip("Jugadores extra del tutorial. Quedan quietos, heredan lo visual del usuario, muestran el nombre configurado y son el blanco del congelar. Si se deja vacío se detectan por código.")]
    [SerializeField] private List<PlayerMovementManager> dummyPlayers = new List<PlayerMovementManager>();

    [Tooltip("Nombre visible sobre el dummy (por defecto \"Dummy\").")]
    [SerializeField] private string dummyDisplayName = "Dummy";

    [Tooltip("Si es true, el dummy hereda solo lo visual (materiales de skin) del jugador local.")]
    [SerializeField] private bool copyVisualsFromLocalPlayer = true;

    [Header("Power-Ups en tutorial")]
    [Tooltip("Si es true, el congelar/ralentizar del usuario afecta a estos dummies (nunca al que lo usa).")]
    [SerializeField] private bool freezeAffectsDummies = true;

    [Header("Ruta del dummy (patrulla)")]
    [Tooltip("Puntos que el dummy recorre en orden cíclico 1 → 2 → 3 → 4 → 1. Pueden ser hijos del dummy: al iniciar se captura su posición mundial y el dummy los recorre en ese orden.")]
    [SerializeField] private List<Transform> dummyPatrolPoints = new List<Transform>();

    [Tooltip("Factor de velocidad del dummy respecto al personaje normal. 1 = va exactamente igual que un personaje (usa su misma velocidad en vivo, incluyendo efectos).")]
    [SerializeField] private float dummyPatrolSpeedFactor = 1f;

    [Tooltip("Distancia horizontal para considerar que el dummy llegó a un punto y pasa al siguiente.")]
    [SerializeField] private float dummyPatrolArriveDistance = 0.6f;

    public IReadOnlyList<PlayerMovementManager> DummyPlayers => dummyPlayers;

    public string DummyDisplayName =>
        string.IsNullOrWhiteSpace(dummyDisplayName) ? TutorialManager.DummyDisplayName : dummyDisplayName.Trim();

    public bool CopyVisualsFromLocalPlayer => copyVisualsFromLocalPlayer;

    public bool FreezeAffectsDummies => freezeAffectsDummies;

    public IReadOnlyList<Transform> DummyPatrolPoints => dummyPatrolPoints;

    public float DummyPatrolSpeedFactor => dummyPatrolSpeedFactor;

    public float DummyPatrolArriveDistance => dummyPatrolArriveDistance;

    /// <summary>
    /// Foto de las posiciones mundiales actuales de los puntos, en el orden del Inspector.
    /// Se captura al iniciar para que sirva aunque los puntos sean hijos del dummy.
    /// </summary>
    public List<Vector3> GetPatrolRouteWorldPositions()
    {
        var route = new List<Vector3>();
        foreach (Transform t in dummyPatrolPoints)
        {
            if (t != null) route.Add(t.position);
        }
        return route;
    }

    /// <summary>True si este jugador fue vinculado como dummy en el Inspector.</summary>
    public bool IsExplicitDummy(PlayerMovementManager pm)
    {
        return pm != null && dummyPlayers.Contains(pm);
    }

    /// <summary>True si este objeto fue vinculado como dummy en el Inspector.</summary>
    public bool IsExplicitDummyObject(GameObject go)
    {
        if (go == null) return false;
        foreach (PlayerMovementManager pm in dummyPlayers)
        {
            if (pm != null && pm.gameObject == go) return true;
        }
        return false;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TutorialSceneSetup] Ya existe un setup en la escena, se destruye el duplicado.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
