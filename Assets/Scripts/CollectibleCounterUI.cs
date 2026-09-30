using UnityEngine;
using TMPro;

/// <summary>
/// HUD que muestra cuántos objetos lleva el jugador local y su puntuación entregada.
/// Ubicar en un GameObject con TMP_Text en el Canvas de MainScene.
/// Se auto-vincula al Player local (IsOwner) y escucha CollectedCountChanged + ScoreChanged.
/// Si scoreText está asignado, muestra puntuación separada; si no, usa combinedFormat en counterText.
/// </summary>
public class CollectibleCounterUI : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Texto donde se muestra el contador. Si se deja vacío lo busca en este GameObject o hijos.")]
    [SerializeField] private TMP_Text counterText;
    [Tooltip("Texto opcional separado para puntuación. Si es null, la puntuación se muestra combinada en counterText.")]
    [SerializeField] private TMP_Text scoreText;

    [Tooltip("Referencia opcional al Player local. Si se deja vacío se detecta automáticamente (IsOwner).")]
    [SerializeField] private PlayerMovementManager targetPlayer;

    [Header("Formato")]
    [Tooltip("Formato del contador. {0}=actual, {1}=máximo.")]
    [SerializeField] private string format = "{0} / {1}";
    [Tooltip("Prefijo opcional (ej: \"Objetos: \"). Se antepone al formato si no está vacío.")]
    [SerializeField] private string prefix = "";
    [Tooltip("Formato de puntuación cuando hay scoreText separado. {0}=puntos.")]
    [SerializeField] private string scoreFormat = "Puntos: {0}";
    [Tooltip("Formato combinado cuando no hay scoreText. {0}=puntos, {1}=actual, {2}=máximo.")]
    [SerializeField] private string combinedFormat = "Puntos: {0} | {1}/{2}";

    [Header("Colores")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color fullColor = new Color(1f, 0.85f, 0.2f);
    [Tooltip("Si es true, también muestra '¡Inventario lleno!' o cambia estilo cuando está lleno.")]
    [SerializeField] private bool showFullLabel = false;
    [SerializeField] private string fullSuffix = " ¡Lleno!";

    private bool isSubscribed;
    private int lastShownCount = int.MinValue;
    private int lastShownScore = int.MinValue;

    private void Awake()
    {
        if (counterText == null)
            counterText = GetComponentInChildren<TMP_Text>(true);

        if (counterText == null)
            Debug.LogWarning($"[CollectibleCounterUI] No se encontró TMP_Text en {name}. Asigna counterText en el Inspector.");

        // Si en el Inspector se arrastró el prefab Player (referencia a asset), descartarla
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Debug.LogWarning($"[CollectibleCounterUI] targetPlayer en {name} apunta a un prefab/asset, se descartará y se buscará el Player local (IsOwner). Deja targetPlayer en None en el Inspector.");
            targetPlayer = null;
        }
    }

    private void OnEnable()
    {
        // Validar de nuevo por si la escena se cargó con referencia inválida
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Unsubscribe();
            targetPlayer = null;
        }

        TryBindToLocalPlayer();
        // Reintentar de forma persistente: el jugador offline se instancia un frame
        // después de cargar la escena, y puede ser reemplazado al salir/entrar.
        // No se cancela tras el primer bind: cada tick valida que el vínculo siga vivo.
        CancelInvoke(nameof(TryBindToLocalPlayer));
        InvokeRepeating(nameof(TryBindToLocalPlayer), 0.5f, 0.5f);
    }

    private void Update()
    {
        // Respaldo por polling: aunque se pierda un evento, la UI siempre refleja
        // el estado actual del jugador (llevo / entrego). Barato y a prueba de orden de ejecución.
        if (targetPlayer == null || !IsValidSceneReference(targetPlayer)) return;
        if (targetPlayer.CollectedCount != lastShownCount || targetPlayer.Score != lastShownScore)
            RefreshUI();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(TryBindToLocalPlayer));
        Unsubscribe();
    }

    private void TryBindToLocalPlayer()
    {
        // Si hay referencia inválida (prefab) o el jugador vinculado fue destruido /
        // quedó en otra escena (salir/entrar al tutorial), descartarla y re-buscar.
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Unsubscribe();
            targetPlayer = null;
        }

        if (targetPlayer != null && isSubscribed)
        {
            // Vínculo vivo: refrescar por si llegó un jugador nuevo idéntico.
            RefreshUI();
            return;
        }

        if (targetPlayer == null)
            targetPlayer = FindLocalPlayer();

        if (targetPlayer == null) return;

        // Encontrado
        Subscribe();
        RefreshUI();
    }

    private static bool IsValidSceneReference(PlayerMovementManager pm)
    {
        if (pm == null) return false;
        // El dummy del tutorial nunca es el jugador local del HUD.
        if (TutorialManager.IsTutorialDummyPlayer(pm)) return false;
        // Un asset de prefab no está en ninguna escena cargada -> scene.IsValid() == false o rootCount == 0
        GameObject go = pm.gameObject;
        if (go == null) return false;
        var scene = go.scene;
        // En runtime un objeto instanciado siempre está en una scene válida y cargada
        if (!scene.IsValid() || !scene.isLoaded) return false;
        // Solo vale el jugador de la escena activa (evita restos de otra escena).
        if (!string.Equals(scene.name, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return false;
        return true;
    }

    private static PlayerMovementManager FindLocalPlayer()
    {
        // Buscar el Player con IsOwner (solo existe tras NetworkSpawn)
        PlayerMovementManager first = null;
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (pm == null) continue;
            // El dummy del tutorial no es el jugador local.
            if (TutorialManager.IsTutorialDummyPlayer(pm)) continue;
            // Solo jugadores de la escena activa (evita atarse a restos de otra escena).
            if (!pm.gameObject.scene.IsValid() || !pm.gameObject.scene.isLoaded) continue;
            if (!string.Equals(pm.gameObject.scene.name, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) continue;
            if (first == null) first = pm;
            if (pm.IsOwner)
                return pm;
        }
        // Sin red (tutorial offline o escena abierta directo en Editor): IsOwner
        // siempre es false, usar el jugador de la escena activa.
        if (first != null && (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening))
            return first;
        // Compatibilidad: si es tutorial, aceptar el primero aunque haya red residual.
        if (TutorialManager.IsTutorial && first != null)
            return first;
        return null;
    }

    private void Subscribe()
    {
        if (targetPlayer == null || isSubscribed) return;
        targetPlayer.CollectedCountChanged += OnCollectedChanged;
        targetPlayer.ScoreChanged += OnScoreChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (targetPlayer != null && isSubscribed)
        {
            targetPlayer.CollectedCountChanged -= OnCollectedChanged;
            targetPlayer.ScoreChanged -= OnScoreChanged;
        }
        isSubscribed = false;
    }

    private void OnCollectedChanged(int newCount)
    {
        RefreshUI();
    }

    private void OnScoreChanged(int newScore)
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (targetPlayer == null) return;

        int current = targetPlayer.CollectedCount;
        int max = targetPlayer.MaxCollected;
        int score = targetPlayer.Score;
        bool isFull = targetPlayer.IsInventoryFull;
        lastShownCount = current;
        lastShownScore = score;

        // Texto de contador
        if (counterText != null)
        {
            string counterString = string.IsNullOrEmpty(prefix)
                ? string.Format(format, current, max)
                : prefix + string.Format(format, current, max);

            if (isFull && showFullLabel)
                counterString += fullSuffix;

            // Si no hay scoreText separado, mostrar combinado
            if (scoreText == null)
            {
                string combined = string.Format(combinedFormat, score, current, max);
                // Respetar prefix si se quiere: prefix ya incluido en format, para combined usamos directamente
                // Si showFullLabel, añadir sufijo
                if (isFull && showFullLabel)
                    combined += fullSuffix;
                counterText.text = combined;
            }
            else
            {
                counterText.text = counterString;
            }
            counterText.color = isFull ? fullColor : normalColor;
        }

        // Texto de puntuación separado
        if (scoreText != null)
        {
            scoreText.text = string.Format(scoreFormat, score);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(format)) format = "{0} / {1}";
        if (string.IsNullOrEmpty(scoreFormat)) scoreFormat = "Puntos: {0}";
        if (string.IsNullOrEmpty(combinedFormat)) combinedFormat = "Puntos: {0} | {1}/{2}";
        if (counterText == null)
            counterText = GetComponentInChildren<TMP_Text>(true);
        // Actualizar preview en editor si hay player en escena
        if (Application.isPlaying) RefreshUI();
    }
#endif
}
