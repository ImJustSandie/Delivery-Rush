using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Muestra en pantalla el texto animado (3, 2, 1, ¡GO!) y el panel de fondo sincronizado de la cuenta regresiva.
/// Ubicar este script en la carpeta Assets/Scripts/Contador inicial.
/// </summary>
public class MatchCountdownUI : MonoBehaviour
{
    [Header("Referencias UI")]
    [Tooltip("Panel de fondo / contenedor de la cuenta regresiva. Se activa durante el conteo y se oculta al finalizar.")]
    [SerializeField] private GameObject countdownPanel;

    [Tooltip("Texto MeshPro para mostrar el conteo. Si se deja vacío se busca en hijos.")]
    [SerializeField] private TMP_Text countdownText;

    [Tooltip("CanvasGroup opcional del panel para animación suave de desvanecido (fade out).")]
    [SerializeField] private CanvasGroup panelCanvasGroup;

    [Header("Configuración Visual")]
    [SerializeField] private string goText = "¡A JUGAR!";
    [SerializeField] private Color numberColor = new Color(1f, 0.85f, 0.2f); // Amarillo dorado
    [SerializeField] private Color goColor = new Color(0.2f, 1f, 0.4f);       // Verde brillante
    [SerializeField] private float punchScaleFactor = 1.5f;
    [SerializeField] private float animDuration = 0.4f;
    [SerializeField] private float fadeDuration = 0.35f;

    private Coroutine punchCoroutine;
    private Coroutine fadeCoroutine;

    private void Awake()
    {
        if (countdownText == null)
            countdownText = GetComponentInChildren<TMP_Text>(true);

        if (countdownPanel == null && panelCanvasGroup != null)
            countdownPanel = panelCanvasGroup.gameObject;

        SetPanelActive(false, true);
    }

    private void OnEnable()
    {
        MatchCountdownManager.OnCountdownTick += HandleCountdownTick;
        if (MatchCountdownManager.Instance != null && MatchCountdownManager.Instance.IsCountdownActive)
        {
            HandleCountdownTick(MatchCountdownManager.Instance.CurrentCountdownValue);
        }
    }

    private void OnDisable()
    {
        MatchCountdownManager.OnCountdownTick -= HandleCountdownTick;
    }

    private void HandleCountdownTick(int value)
    {
        if (value < 0)
        {
            SetPanelActive(false, false);
            return;
        }

        SetPanelActive(true, true);

        if (countdownText != null)
        {
            if (value > 0)
            {
                countdownText.text = value.ToString();
                countdownText.color = numberColor;
            }
            else
            {
                countdownText.text = goText;
                countdownText.color = goColor;
            }

            if (punchCoroutine != null) StopCoroutine(punchCoroutine);
            punchCoroutine = StartCoroutine(AnimatePunch());
        }
    }

    private void SetPanelActive(bool active, bool immediate)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);

        if (active)
        {
            if (countdownPanel != null) countdownPanel.SetActive(true);
            if (countdownText != null) countdownText.gameObject.SetActive(true);
            if (panelCanvasGroup != null) panelCanvasGroup.alpha = 1f;
        }
        else
        {
            if (immediate || panelCanvasGroup == null)
            {
                if (countdownPanel != null) countdownPanel.SetActive(false);
                if (countdownText != null) countdownText.gameObject.SetActive(false);
                if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
            }
            else
            {
                fadeCoroutine = StartCoroutine(FadeOutPanel());
            }
        }
    }

    private IEnumerator FadeOutPanel()
    {
        float elapsed = 0f;
        float startAlpha = panelCanvasGroup != null ? panelCanvasGroup.alpha : 1f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            if (panelCanvasGroup != null) panelCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }

        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
        if (countdownPanel != null) countdownPanel.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }

    private IEnumerator AnimatePunch()
    {
        Vector3 startScale = Vector3.one * punchScaleFactor;
        Vector3 endScale = Vector3.one;
        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / animDuration;
            float smoothT = Mathf.Sin(t * Mathf.PI * 0.5f);
            countdownText.transform.localScale = Vector3.Lerp(startScale, endScale, smoothT);
            yield return null;
        }

        countdownText.transform.localScale = endScale;
    }
}
