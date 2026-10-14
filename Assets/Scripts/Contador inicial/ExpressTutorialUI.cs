using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra un tutorial express dinámico y animado (Objetivo y Tiempo) antes de la cuenta regresiva.
/// Incluye animaciones de entrada fluidas (slide-up, pop y fade) para la imagen y la descripción en cada panel.
/// Se sincroniza automáticamente con MatchCountdownManager y muestra la duración real desde MatchTimerManager.
/// Ubicar este script en el contenedor del Canvas en MainScene.
/// </summary>
public class ExpressTutorialUI : MonoBehaviour
{
    [Header("Contenedor Principal")]
    [Tooltip("Objeto raíz del tutorial express. Se activa al inicio del tutorial y se oculta al finalizar.")]
    [SerializeField] private GameObject tutorialRoot;

    [Tooltip("CanvasGroup opcional para transiciones suaves de entrada y salida (fade) de todo el tutorial.")]
    [SerializeField] private CanvasGroup rootCanvasGroup;

    [Header("Paneles del Tutorial (2 Pasos)")]
    [Tooltip("Panel 1: Explica el objetivo principal del juego (paquetes y entregas).")]
    [SerializeField] private GameObject panelObjective;

    [Tooltip("Panel 2: Explica el tiempo límite y condición de victoria.")]
    [SerializeField] private GameObject panelTime;

    [Header("Elementos Animados - Panel Objetivo (Paso 1)")]
    [Tooltip("Imagen o icono ilustrativo del objetivo.")]
    [SerializeField] private RectTransform objectiveImage;

    [Tooltip("Texto o contenedor de la descripción del objetivo.")]
    [SerializeField] private RectTransform objectiveDescription;

    [Header("Elementos Animados - Panel Tiempo (Paso 2)")]
    [Tooltip("Imagen o icono ilustrativo del tiempo / podio.")]
    [SerializeField] private RectTransform timeImage;

    [Tooltip("Texto TMP dentro del Panel 2 donde se insertará el tiempo de partida dinámicamente.")]
    [SerializeField] private TMP_Text timeDescriptionText;

    [Tooltip("Plantilla para formatear la descripción del tiempo. Usa {0} para el tiempo formateado.")]
    [SerializeField] private string timeFormatTemplate = "¡Tienes {0} para entregar la mayor cantidad de paquetes en los edificios y ganar!";

    [Header("Configuración de Animación de Entrada")]
    [Tooltip("Duración de la animación de entrada para cada elemento (segundos).")]
    [SerializeField] private float elementAnimDuration = 0.45f;

    [Tooltip("Desfase de tiempo (stagger) entre la entrada de la imagen y la descripción.")]
    [SerializeField] private float staggerDelay = 0.12f;

    [Tooltip("Desplazamiento vertical inicial para el efecto slide-up (píxeles).")]
    [SerializeField] private float slideOffset = 40f;

    [Tooltip("Escala inicial al comenzar la animación de entrada.")]
    [SerializeField] private float startScale = 0.7f;

    [Header("Indicadores de Progreso Dinámicos")]
    [Tooltip("Barra de progreso visual (Image tipo Filled) que se llena suavemente durante cada diapositiva.")]
    [SerializeField] private Image stepProgressBar;

    [Tooltip("Indicadores circulares (dots) del paso actual (Paso 1 y Paso 2).")]
    [SerializeField] private RectTransform[] stepDots;

    [SerializeField] private Color activeDotColor = new Color(1f, 0.85f, 0.2f); // Amarillo dorado
    [SerializeField] private Color inactiveDotColor = new Color(1f, 1f, 1f, 0.3f); // Blanco translúcido

    [Header("Animación de Paneles y Fade General")]
    [SerializeField] private float fadeDuration = 0.3f;
    [SerializeField] private float punchScaleFactor = 1.05f;
    [SerializeField] private float punchDuration = 0.3f;

    [Header("Audio (Opcional)")]
    [SerializeField] private AudioClip slideChangeClip;
    [SerializeField] private AudioSource audioSource;

    // Cache de posiciones y escalas originales para animaciones repetibles
    private Vector2 objectiveImageOrigPos;
    private Vector3 objectiveImageOrigScale = Vector3.one;
    private CanvasGroup objectiveImageCanvasGroup;

    private Vector2 objectiveDescOrigPos;
    private Vector3 objectiveDescOrigScale = Vector3.one;
    private CanvasGroup objectiveDescCanvasGroup;

    private Vector2 timeImageOrigPos;
    private Vector3 timeImageOrigScale = Vector3.one;
    private CanvasGroup timeImageCanvasGroup;

    private Vector2 timeDescOrigPos;
    private Vector3 timeDescOrigScale = Vector3.one;
    private CanvasGroup timeDescCanvasGroup;

    private Coroutine fadeCoroutine;
    private Coroutine progressCoroutine;
    private Coroutine punchCoroutine;
    private Coroutine entranceCoroutine1;
    private Coroutine entranceCoroutine2;
    private int currentStep = -1;

    private void Awake()
    {
        AutoWireComponents();
        CacheOriginalTransforms();
        SetTutorialVisible(false, immediate: true);
    }

    private void OnEnable()
    {
        MatchCountdownManager.OnTutorialStepChanged += HandleTutorialStepChanged;
        MatchCountdownManager.OnTutorialFinished += HandleTutorialFinished;

        if (MatchCountdownManager.Instance != null && MatchCountdownManager.Instance.IsTutorialActive)
        {
            HandleTutorialStepChanged(MatchCountdownManager.Instance.CurrentTutorialStepValue);
        }
    }

    private Coroutine waitTransitionCoroutine;

    private void OnDisable()
    {
        MatchCountdownManager.OnTutorialStepChanged -= HandleTutorialStepChanged;
        MatchCountdownManager.OnTutorialFinished -= HandleTutorialFinished;
        if (waitTransitionCoroutine != null)
        {
            StopCoroutine(waitTransitionCoroutine);
            waitTransitionCoroutine = null;
        }
    }

    /// <summary>
    /// Guarda las posiciones, escalas y grupos de lienzo originales de los elementos a animar.
    /// </summary>
    private void CacheOriginalTransforms()
    {
        if (objectiveImage != null)
        {
            objectiveImageOrigPos = objectiveImage.anchoredPosition;
            objectiveImageOrigScale = objectiveImage.localScale;
            objectiveImageCanvasGroup = EnsureCanvasGroup(objectiveImage.gameObject);
        }

        if (objectiveDescription != null)
        {
            objectiveDescOrigPos = objectiveDescription.anchoredPosition;
            objectiveDescOrigScale = objectiveDescription.localScale;
            objectiveDescCanvasGroup = EnsureCanvasGroup(objectiveDescription.gameObject);
        }

        if (timeImage != null)
        {
            timeImageOrigPos = timeImage.anchoredPosition;
            timeImageOrigScale = timeImage.localScale;
            timeImageCanvasGroup = EnsureCanvasGroup(timeImage.gameObject);
        }

        if (timeDescriptionText != null)
        {
            var rect = timeDescriptionText.rectTransform;
            timeDescOrigPos = rect.anchoredPosition;
            timeDescOrigScale = rect.localScale;
            timeDescCanvasGroup = EnsureCanvasGroup(timeDescriptionText.gameObject);
        }
    }

    private CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = go.AddComponent<CanvasGroup>();
        }
        return cg;
    }

    /// <summary>
    /// Responde al cambio de paso del tutorial sincronizado por el servidor.
    /// </summary>
    /// <param name="step">0 = Objetivo, 1 = Tiempo, -1 = Inactivo</param>
    private void HandleTutorialStepChanged(int step)
    {
        currentStep = step;

        if (waitTransitionCoroutine != null)
        {
            StopCoroutine(waitTransitionCoroutine);
            waitTransitionCoroutine = null;
        }

        if (step < 0)
        {
            SetTutorialVisible(false, immediate: false);
            return;
        }

        // Si la cortina de transición aún no se ha abierto por completo en este cliente, esperar
        if (DiagonalSceneTransition.Instance != null && !DiagonalSceneTransition.Instance.IsOpen)
        {
            waitTransitionCoroutine = StartCoroutine(WaitTransitionThenShow(step));
        }
        else
        {
            ExecuteStepDisplay(step);
        }
    }

    private IEnumerator WaitTransitionThenShow(int step)
    {
        float waited = 0f;
        const float maxWait = 8.0f;
        while (DiagonalSceneTransition.Instance != null && !DiagonalSceneTransition.Instance.IsOpen && waited < maxWait)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }

        waitTransitionCoroutine = null;
        if (currentStep == step)
        {
            ExecuteStepDisplay(step);
        }
    }

    private void ExecuteStepDisplay(int step)
    {
        SetTutorialVisible(true, immediate: false);

        float stepDuration = MatchCountdownManager.Instance != null
            ? MatchCountdownManager.Instance.TutorialStepDuration
            : 3.5f;

        if (step == 0)
        {
            ShowStep(panelObjective, panelTime);
            TriggerStepEntranceAnimations(0);
            UpdateStepDots(0);
        }
        else if (step == 1)
        {
            UpdateTimeText();
            ShowStep(panelTime, panelObjective);
            TriggerStepEntranceAnimations(1);
            UpdateStepDots(1);
        }

        // Animar barra de tiempo
        if (stepProgressBar != null)
        {
            if (progressCoroutine != null) StopCoroutine(progressCoroutine);
            progressCoroutine = StartCoroutine(AnimateProgressBar(stepDuration));
        }

        // Sonido de cambio de panel
        PlaySlideSound();
    }

    private void HandleTutorialFinished()
    {
        if (waitTransitionCoroutine != null)
        {
            StopCoroutine(waitTransitionCoroutine);
            waitTransitionCoroutine = null;
        }
        SetTutorialVisible(false, immediate: false);
    }

    /// <summary>
    /// Dispara la animación escalonada de entrada para la imagen y la descripción del paso activo.
    /// </summary>
    private void TriggerStepEntranceAnimations(int step)
    {
        StopEntranceCoroutines();

        if (step == 0)
        {
            // Panel 1: Objetivo
            if (objectiveImage != null)
            {
                entranceCoroutine1 = StartCoroutine(AnimateEntranceElement(
                    objectiveImage,
                    objectiveImageCanvasGroup,
                    objectiveImageOrigPos,
                    objectiveImageOrigScale,
                    delay: 0f
                ));
            }

            if (objectiveDescription != null)
            {
                entranceCoroutine2 = StartCoroutine(AnimateEntranceElement(
                    objectiveDescription,
                    objectiveDescCanvasGroup,
                    objectiveDescOrigPos,
                    objectiveDescOrigScale,
                    delay: staggerDelay
                ));
            }
        }
        else if (step == 1)
        {
            // Panel 2: Tiempo
            if (timeImage != null)
            {
                entranceCoroutine1 = StartCoroutine(AnimateEntranceElement(
                    timeImage,
                    timeImageCanvasGroup,
                    timeImageOrigPos,
                    timeImageOrigScale,
                    delay: 0f
                ));
            }

            if (timeDescriptionText != null)
            {
                entranceCoroutine2 = StartCoroutine(AnimateEntranceElement(
                    timeDescriptionText.rectTransform,
                    timeDescCanvasGroup,
                    timeDescOrigPos,
                    timeDescOrigScale,
                    delay: staggerDelay
                ));
            }
        }
    }

    private void StopEntranceCoroutines()
    {
        if (entranceCoroutine1 != null) StopCoroutine(entranceCoroutine1);
        if (entranceCoroutine2 != null) StopCoroutine(entranceCoroutine2);
    }

    /// <summary>
    /// Corrutina de entrada fluida: slide-up vertical + escala con rebote elástico suave + fundido de alpha.
    /// </summary>
    private IEnumerator AnimateEntranceElement(RectTransform rect, CanvasGroup cg, Vector2 origPos, Vector3 origScale, float delay)
    {
        if (rect == null) yield break;

        // Estado inicial invisible y desplazado
        Vector2 startPos = origPos + new Vector2(0f, -slideOffset);
        Vector3 initialScale = origScale * startScale;

        rect.anchoredPosition = startPos;
        rect.localScale = initialScale;
        if (cg != null) cg.alpha = 0f;

        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        float elapsed = 0f;
        while (elapsed < elementAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / elementAnimDuration);

            // Curva EaseOutBack para un rebote elástico profesional
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float easeOutBack = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);

            // Curva suave cuadrática para posición y opacidad
            float easeOutQuad = 1f - (1f - t) * (1f - t);

            rect.anchoredPosition = Vector2.Lerp(startPos, origPos, easeOutQuad);
            rect.localScale = Vector3.LerpUnclamped(initialScale, origScale, easeOutBack);

            if (cg != null)
            {
                cg.alpha = Mathf.Lerp(0f, 1f, easeOutQuad);
            }

            yield return null;
        }

        rect.anchoredPosition = origPos;
        rect.localScale = origScale;
        if (cg != null) cg.alpha = 1f;
    }

    /// <summary>
    /// Activa el panel activo y desactiva el otro con animación de punch.
    /// </summary>
    private void ShowStep(GameObject activePanel, GameObject inactivePanel)
    {
        if (inactivePanel != null) inactivePanel.SetActive(false);

        if (activePanel != null)
        {
            activePanel.SetActive(true);
            if (punchCoroutine != null) StopCoroutine(punchCoroutine);
            punchCoroutine = StartCoroutine(AnimatePunch(activePanel.transform));
        }
    }

    /// <summary>
    /// Actualiza el texto de tiempo con la duración real configurada en MatchTimerManager.
    /// </summary>
    private void UpdateTimeText()
    {
        if (timeDescriptionText == null) return;

        string formattedTime = GetFormattedMatchDuration();
        timeDescriptionText.text = string.Format(timeFormatTemplate, formattedTime);
    }

    private string GetFormattedMatchDuration()
    {
        float duration = 180f;
        if (MatchTimerManager.Instance != null)
        {
            duration = MatchTimerManager.Instance.Duration;
        }

        int totalSeconds = Mathf.CeilToInt(duration);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        if (seconds == 0)
        {
            return minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        }

        return $"{minutes}:{seconds:00} minutos";
    }

    private void UpdateStepDots(int activeIndex)
    {
        if (stepDots == null || stepDots.Length == 0) return;

        for (int i = 0; i < stepDots.Length; i++)
        {
            if (stepDots[i] == null) continue;

            bool isActive = i == activeIndex;
            stepDots[i].localScale = isActive ? Vector3.one * 1.25f : Vector3.one;

            var img = stepDots[i].GetComponent<Image>();
            if (img != null)
            {
                img.color = isActive ? activeDotColor : inactiveDotColor;
            }
        }
    }

    private void SetTutorialVisible(bool visible, bool immediate)
    {
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        if (progressCoroutine != null && !visible) StopCoroutine(progressCoroutine);
        if (!visible) StopEntranceCoroutines();

        if (visible)
        {
            if (tutorialRoot != null) tutorialRoot.SetActive(true);
            if (rootCanvasGroup != null)
            {
                if (immediate)
                {
                    rootCanvasGroup.alpha = 1f;
                }
                else
                {
                    fadeCoroutine = StartCoroutine(FadeCanvasGroup(rootCanvasGroup, rootCanvasGroup.alpha, 1f));
                }
            }
        }
        else
        {
            if (immediate || rootCanvasGroup == null)
            {
                if (rootCanvasGroup != null) rootCanvasGroup.alpha = 0f;
                if (tutorialRoot != null) tutorialRoot.SetActive(false);
            }
            else
            {
                fadeCoroutine = StartCoroutine(FadeOutAndHide());
            }
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            if (cg != null) cg.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        if (cg != null) cg.alpha = to;
    }

    private IEnumerator FadeOutAndHide()
    {
        yield return FadeCanvasGroup(rootCanvasGroup, rootCanvasGroup != null ? rootCanvasGroup.alpha : 1f, 0f);
        if (tutorialRoot != null) tutorialRoot.SetActive(false);
    }

    private IEnumerator AnimatePunch(Transform targetTransform)
    {
        if (targetTransform == null) yield break;

        Vector3 startScale = Vector3.one * punchScaleFactor;
        Vector3 endScale = Vector3.one;
        float elapsed = 0f;

        while (elapsed < punchDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / punchDuration;
            float smoothT = Mathf.Sin(t * Mathf.PI * 0.5f);
            targetTransform.localScale = Vector3.Lerp(startScale, endScale, smoothT);
            yield return null;
        }

        targetTransform.localScale = endScale;
    }

    private IEnumerator AnimateProgressBar(float duration)
    {
        if (stepProgressBar == null) yield break;

        float elapsed = 0f;
        stepProgressBar.fillAmount = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            stepProgressBar.fillAmount = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        stepProgressBar.fillAmount = 1f;
    }

    private void PlaySlideSound()
    {
        if (slideChangeClip == null) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(slideChangeClip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(slideChangeClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }
    }

    /// <summary>
    /// Auto-vincula componentes por jerarquía y nombres para minimizar la configuración manual en el Inspector.
    /// </summary>
    private void AutoWireComponents()
    {
        if (tutorialRoot == null)
        {
            tutorialRoot = gameObject;
        }

        if (rootCanvasGroup == null)
        {
            rootCanvasGroup = tutorialRoot.GetComponent<CanvasGroup>();
            if (rootCanvasGroup == null)
            {
                rootCanvasGroup = tutorialRoot.AddComponent<CanvasGroup>();
            }
        }

        // Buscar paneles si no están asignados
        if (panelObjective == null || panelTime == null)
        {
            foreach (Transform child in tutorialRoot.transform)
            {
                string lower = child.name.ToLower();
                if (panelObjective == null && (lower.Contains("obj") || lower.Contains("step1") || lower.Contains("panel1")))
                {
                    panelObjective = child.gameObject;
                }
                else if (panelTime == null && (lower.Contains("tiemp") || lower.Contains("time") || lower.Contains("step2") || lower.Contains("panel2")))
                {
                    panelTime = child.gameObject;
                }
            }

            // Fallback: usar los primeros dos hijos si aún no se encontraron
            if (panelObjective == null && tutorialRoot.transform.childCount > 0)
                panelObjective = tutorialRoot.transform.GetChild(0).gameObject;

            if (panelTime == null && tutorialRoot.transform.childCount > 1)
                panelTime = tutorialRoot.transform.GetChild(1).gameObject;
        }

        // Auto-detectar elementos en Panel 1 (Objetivo)
        if (panelObjective != null)
        {
            if (objectiveImage == null)
            {
                var images = panelObjective.GetComponentsInChildren<Image>(true);
                foreach (var img in images)
                {
                    // Evitar el fondo del panel si tiene el mismo transform
                    if (img.gameObject == panelObjective) continue;

                    string lower = img.name.ToLower();
                    if (lower.Contains("icon") || lower.Contains("img") || lower.Contains("image") || lower.Contains("ilustr") || lower.Contains("foto"))
                    {
                        objectiveImage = img.rectTransform;
                        break;
                    }
                }
                // Si aún es null pero hay imágenes hijas
                if (objectiveImage == null)
                {
                    foreach (var img in images)
                    {
                        if (img.gameObject != panelObjective)
                        {
                            objectiveImage = img.rectTransform;
                            break;
                        }
                    }
                }
            }

            if (objectiveDescription == null)
            {
                var texts = panelObjective.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts)
                {
                    string lower = t.name.ToLower();
                    if (lower.Contains("desc") || lower.Contains("texto") || lower.Contains("body"))
                    {
                        objectiveDescription = t.rectTransform;
                        break;
                    }
                }
                if (objectiveDescription == null && texts.Length > 1)
                {
                    objectiveDescription = texts[1].rectTransform; // Segundo texto suele ser descripción
                }
            }
        }

        // Auto-detectar elementos en Panel 2 (Tiempo)
        if (panelTime != null)
        {
            if (timeImage == null)
            {
                var images = panelTime.GetComponentsInChildren<Image>(true);
                foreach (var img in images)
                {
                    if (img.gameObject == panelTime) continue;

                    string lower = img.name.ToLower();
                    if (lower.Contains("icon") || lower.Contains("img") || lower.Contains("image") || lower.Contains("ilustr") || lower.Contains("foto") || lower.Contains("reloj") || lower.Contains("clock"))
                    {
                        timeImage = img.rectTransform;
                        break;
                    }
                }
                if (timeImage == null)
                {
                    foreach (var img in images)
                    {
                        if (img.gameObject != panelTime)
                        {
                            timeImage = img.rectTransform;
                            break;
                        }
                    }
                }
            }

            if (timeDescriptionText == null)
            {
                var texts = panelTime.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts)
                {
                    string lower = t.name.ToLower();
                    if (lower.Contains("desc") || lower.Contains("tiemp") || lower.Contains("time") || lower.Contains("body"))
                    {
                        timeDescriptionText = t;
                        break;
                    }
                }
                if (timeDescriptionText == null && texts.Length > 0)
                {
                    timeDescriptionText = texts.Length > 1 ? texts[1] : texts[0];
                }
            }
        }

        // Buscar barra de progreso
        if (stepProgressBar == null)
        {
            var images = tutorialRoot.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.type == Image.Type.Filled || img.name.ToLower().Contains("bar") || img.name.ToLower().Contains("progress"))
                {
                    stepProgressBar = img;
                    break;
                }
            }
        }

        // Buscar AudioSource
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }
}
