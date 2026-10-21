using TMPro;
using UnityEngine;

/// <summary>
/// Temporizador de partida SOLO VISUAL para el tutorial offline.
/// Copia el comportamiento del <see cref="MatchTimerUI"/> de gameplay, pero:
/// - No depende del NetworkManager ni de MatchTimerManager (100% offline).
/// - Solo muestra la cuenta atrás (por defecto 2:00); al llegar a 00:00 no pasa nada.
/// - El texto se toma de la HUD copiada de gameplay (hijo "Timer" del Canvas).
/// Colocar en el GameObject raíz del Canvas del tutorial.
/// </summary>
public class TutorialMatchTimer : MonoBehaviour
{
    public static TutorialMatchTimer Instance { get; private set; }

    [Header("Duración")]
    [Tooltip("Duración de la cuenta atrás visual en segundos (120 = 2 minutos).")]
    [SerializeField] private float matchDurationSeconds = 120f;

    [Header("Referencias")]
    [Tooltip("TMP_Text donde se muestra el tiempo. Si se deja vacío se busca un hijo llamado como timerObjectName.")]
    [SerializeField] private TMP_Text timerText;

    [Tooltip("Nombre del hijo TMP_Text del Timer en la HUD de gameplay.")]
    [SerializeField] private string timerObjectName = "Timer";

    [Header("Formato")]
    [SerializeField] private string format = "{0:00}:{1:00}";
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color lowTimeColor = Color.red;
    [SerializeField] private float lowTimeThreshold = 30f;

    /// <summary>Segundos restantes de la cuenta atrás visual (solo lectura).</summary>
    public float RemainingTime => remainingTime;
    /// <summary>True mientras la cuenta visual siga corriendo (no llega a cero).</summary>
    public bool IsRunning { get; private set; }

    private TMP_Text boundText;
    private float remainingTime;
    private int lastShownSecond = -1;

    private void Awake()
    {
        Instance = this;
        BindText();
    }

    private void OnEnable()
    {
        Instance = this;
        BindText();
        ResetTimer(matchDurationSeconds);
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        // Al salir del tutorial la escena se descarga completa: no hay que limpiar más.
    }

    /// <summary>Reinicia la cuenta atrás visual.</summary>
    public void ResetTimer(float seconds)
    {
        remainingTime = Mathf.Max(0f, seconds);
        IsRunning = remainingTime > 0f;
        lastShownSecond = -1;
        RefreshLabel(true);
    }

    private void Update()
    {
        // Solo corre en el tutorial offline; fuera del tutorial queda inmóvil.
        if (!TutorialManager.IsTutorial) return;
        if (!IsRunning) return;

        remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
        IsRunning = remainingTime > 0f;

        int second = Mathf.CeilToInt(remainingTime);
        if (second != lastShownSecond)
        {
            RefreshLabel(false);
        }
        // Al llegar a 00:00 no se hace nada: es un timer puramente visual.
    }

    private void BindText()
    {
        if (boundText != null && boundText != null) return;
        if (timerText != null)
        {
            boundText = timerText;
            return;
        }
        var texts = GetComponentsInChildren<TMP_Text>(true);
        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(timerObjectName) && text.name == timerObjectName)
            {
                boundText = text;
                break;
            }
        }
        if (boundText == null && texts.Length > 0)
            boundText = texts[0];

        if (boundText == null)
            Debug.LogWarning($"[TutorialMatchTimer] No se encontró TMP_Text '{timerObjectName}' para el timer visual del tutorial.");
    }

    private void RefreshLabel(bool force)
    {
        if (boundText == null)
        {
            BindText();
            if (boundText == null) return;
        }

        int t = Mathf.CeilToInt(remainingTime);
        lastShownSecond = t;
        int m = t / 60;
        int s = t % 60;
        boundText.text = string.Format(format, m, s);
        boundText.color = t <= lowTimeThreshold ? lowTimeColor : normalColor;
        if (force) Debug.Log($"[TutorialMatchTimer] Timer visual del tutorial iniciado: {matchDurationSeconds:0}s.");
    }
}
