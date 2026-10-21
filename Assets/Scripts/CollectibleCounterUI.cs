using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD que muestra cuántos objetos lleva el jugador local y su puntuación entregada.
/// Ubicar en un GameObject con TMP_Text en el Canvas de MainScene.
/// Se auto-vincula al Player local (IsOwner) y escucha CollectedCountChanged + ScoreChanged.
/// Si scoreText está asignado, muestra puntuación separada; si no, usa combinedFormat en counterText.
/// Permite cambiar el color del panel de fondo (ej: PanelCantidad) cuando el jugador alcanza el tope máximo.
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

    [Header("Panel")]
    [Tooltip("Imagen de fondo del panel de cantidad. Si se deja vacía se detecta automáticamente (padre de counterText o GameObject 'PanelCantidad').")]
    [SerializeField] private Image panelImage;
    [Tooltip("Si es true, captura el color actual del Image en Awake como color normal.")]
    [SerializeField] private bool autoCapturePanelNormalColor = true;
    [Tooltip("Color de fondo del panel en estado normal (utilizado si autoCapturePanelNormalColor es false).")]
    [SerializeField] private Color panelNormalColor = new Color(1f, 1f, 1f, 0.64f);
    [Tooltip("Color de fondo del panel cuando el jugador lleva la cantidad máxima de objetos encima.")]
    [SerializeField] private Color panelFullColor = new Color(0.9f, 0.25f, 0.25f, 0.85f);
    [Tooltip("Si es true, usa panelFullIntenseColor (un rojo más intenso, con mejor contraste) en lugar de panelFullColor cuando el inventario está lleno.")]
    [SerializeField] private bool useIntenseFullColor = true;
    [Tooltip("Rojo intenso usado cuando el inventario está lleno y useIntenseFullColor es true.")]
    [SerializeField] private Color panelFullIntenseColor = new Color(1f, 0.06f, 0.06f, 0.95f);

    [Header("Formato")]
    [Tooltip("Formato del contador. {0}=actual, {1}=máximo.")]
    [SerializeField] private string format = "{0} / {1}";
    [Tooltip("Prefijo opcional (ej: \"Objetos: \"). Se antepone al formato si no está vacío.")]
    [SerializeField] private string prefix = "";
    [Tooltip("Formato de puntuación cuando hay scoreText separado. {0}=puntos.")]
    [SerializeField] private string scoreFormat = "Puntos: {0}";
    [Tooltip("Formato combinado cuando no hay scoreText. {0}=puntos, {1}=actual, {2}=máximo.")]
    [SerializeField] private string combinedFormat = "Puntos: {0} | {1}/{2}";

    [Header("Colores Texto")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color fullColor = new Color(1f, 0.85f, 0.2f);
    [Tooltip("Si es true, también muestra '¡Inventario lleno!' o cambia estilo cuando está lleno.")]
    [SerializeField] private bool showFullLabel = false;
    [SerializeField] private string fullSuffix = " ¡Lleno!";

    [Header("Animación al estar lleno (pulso)")]
    [Tooltip("Si es true, el panel da un pulso de crecimiento + sacudida al llenarse y cada vez que se intenta recoger un item con el inventario lleno.")]
    [SerializeField] private bool fullPanelAnimation = true;
    [Tooltip("Cuánto crece el panel en el pulso (1 = sin cambio). Ej: 1.12 = 12% más grande.")]
    [SerializeField] private float fullPulseScale = 1.12f;
    [Tooltip("Duración de cada pulso (segundos).")]
    [SerializeField] private float fullPulseDuration = 0.35f;
    [Tooltip("Intensidad de la sacudida en píxeles (desplazamiento máximo alrededor de la posición normal).")]
    [SerializeField] private float fullShakeStrength = 6f;
    [Tooltip("RectTransform que se anima. Si se deja vacío se usa el del panelImage (el panel completo con su texto).")]
    [SerializeField] private RectTransform fullPulseTarget;
    [Tooltip("Si es true, la imagen de fondo del panel crece mientras el inventario está lleno. El texto del contador es hijo del panel, así que crece junto con ella y queda proporcional al fondo (sin descuadrarse).")]
    [SerializeField] private bool growPanelWhenFull = true;
    [Tooltip("Escala de la imagen de fondo del panel — y del texto que contiene — mientras el inventario está lleno.")]
    [SerializeField] private float fullPanelScaleMultiplier = 1.4f;

    [Header("Mensaje al rechazar (maleta llena)")]
    [Tooltip("Si es true, en cada pulso (al llenarse o al intentar recoger estando lleno) aparece un mensaje sobre/bajo el panel.")]
    [SerializeField] private bool showFullMessage = true;
    [Tooltip("Texto a mostrar en cada temblor.")]
    [SerializeField] private string fullMessage = "Maleta llena";
    [Tooltip("TMP_Text del mensaje. Si se deja vacío se crea automáticamente en runtime como hijo del panel.")]
    [SerializeField] private TMP_Text fullMessageText;
    [Tooltip("Desplazamiento del mensaje respecto al centro del panel (en coordenadas locales del panel).")]
    [SerializeField] private Vector2 fullMessageOffset = new Vector2(0f, -95f);
    [Tooltip("Tamaño de fuente del mensaje.")]
    [SerializeField] private float fullMessageFontSize = 42f;
    [Tooltip("Color del mensaje (el alfa se anima con el pulso).")]
    [SerializeField] private Color fullMessageColor = new Color(1f, 0.85f, 0.2f);

    [Header("Escala del texto al estar lleno")]
    [Tooltip("Escala final del mensaje 'Maleta llena' (1 = tamaño original de la fuente). Se aplica sobre el texto y además hereda el crecimiento del panel.")]
    [SerializeField] private float fullMessageScale = 1.35f;

    [Header("Variación de color del panel al estar lleno (seno)")]
    [Tooltip("Si es true, mientras el inventario está lleno el color de fondo del panel varía constantemente siguiendo una función seno.")]
    [SerializeField] private bool fullColorWave = true;
    [Tooltip("Velocidad de la oscilación del seno (en radianes por segundo).")]
    [SerializeField] private float fullColorWaveSpeed = 5f;
    [Tooltip("Cuánto se acerca el color hacia fullColorWaveColor en el pico del seno (0 = sin variación, 1 = hasta el color pico).")]
    [SerializeField, Range(0f, 1f)] private float fullColorWaveAmplitude = 0.35f;
    [Tooltip("Color pico hacia el que oscila el fondo del panel (un rojo más claro) mientras el inventario está lleno.")]
    [SerializeField] private Color fullColorWaveColor = new Color(1f, 0.45f, 0.35f, 0.95f);

    private bool isSubscribed;
    private int lastShownCount = int.MinValue;
    private int lastShownScore = int.MinValue;
    private int lastShownMax = int.MinValue;
    private Color initialPanelColor;
    private bool hasCapturedPanelColor;

    private Coroutine fullPulseRoutine;
    private bool wasFull;
    private Vector3 basePulseScale = Vector3.one;
    private Vector2 basePulsePosition;
    private bool hasBasePulseTransform;

    private Coroutine fullColorWaveRoutine;

    private void Awake()
    {
        if (counterText == null)
            counterText = GetComponentInChildren<TMP_Text>(true);

        if (panelImage == null)
            FindPanelImage();

        CaptureInitialPanelColor();
        CaptureFullPulseBase(FullPulseRect);
        EnsureFullMessageText();

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
        if (panelImage == null)
        {
            FindPanelImage();
            CaptureInitialPanelColor();
        }

        // Re-capturar el estado base del panel (por si el objeto cambió de escena/instancia).
        hasBasePulseTransform = false;
        wasFull = false;
        CaptureFullPulseBase(FullPulseRect);
        EnsureFullMessageText();

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
        if (targetPlayer.CollectedCount != lastShownCount || targetPlayer.Score != lastShownScore || targetPlayer.MaxCollected != lastShownMax)
            RefreshUI();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(TryBindToLocalPlayer));
        Unsubscribe();
        wasFull = false;
        StopFullPulse();
        StopFullColorWave();
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
        targetPlayer.CollectRejected += OnCollectRejected;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (targetPlayer != null && isSubscribed)
        {
            targetPlayer.CollectedCountChanged -= OnCollectedChanged;
            targetPlayer.ScoreChanged -= OnScoreChanged;
            targetPlayer.CollectRejected -= OnCollectRejected;
        }
        isSubscribed = false;
        // Sin jugador vinculado no hay estado "lleno": detener y restaurar el panel.
        wasFull = false;
        StopFullPulse();
        StopFullColorWave();
    }

    private void OnCollectedChanged(int newCount)
    {
        RefreshUI();
    }

    private void OnScoreChanged(int newScore)
    {
        RefreshUI();
    }

    /// <summary>El jugador intentó recoger un item con el inventario lleno: reproducir un pulso.</summary>
    private void OnCollectRejected()
    {
        PlayFullPulse();
    }

    private void CaptureInitialPanelColor()
    {
        if (panelImage != null && !hasCapturedPanelColor)
        {
            initialPanelColor = panelImage.color;
            hasCapturedPanelColor = true;
        }
    }

    private void FindPanelImage()
    {
        if (panelImage != null) return;

        // 1. Padre directo de counterText (caso MainScene: Cantidad dentro de PanelCantidad)
        if (counterText != null && counterText.transform.parent != null)
        {
            panelImage = counterText.transform.parent.GetComponent<Image>();
            if (panelImage != null) return;
        }

        // 2. Componente en este mismo GameObject o hijos
        panelImage = GetComponent<Image>();
        if (panelImage != null) return;

        panelImage = GetComponentInChildren<Image>(true);
        if (panelImage != null) return;

        // 3. Buscar GameObject 'PanelCantidad' en la escena activa
        GameObject found = GameObject.Find("PanelCantidad");
        if (found != null)
        {
            panelImage = found.GetComponent<Image>();
            if (panelImage != null) return;
        }

        // 4. Componente en ancestros de counterText
        if (counterText != null)
        {
            panelImage = counterText.GetComponentInParent<Image>();
        }
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
        lastShownMax = max;

        // Actualizar color del panel de fondo si existe
        if (panelImage == null)
        {
            FindPanelImage();
            CaptureInitialPanelColor();
        }

        if (panelImage != null)
        {
            Color normal = (autoCapturePanelNormalColor && hasCapturedPanelColor) ? initialPanelColor : panelNormalColor;
            if (isFull)
            {
                Color full = useIntenseFullColor ? panelFullIntenseColor : panelFullColor;
                if (fullColorWave && isActiveAndEnabled)
                {
                    // El fondo oscila constantemente (función seno) mientras esté lleno.
                    StartFullColorWave(full);
                }
                else
                {
                    StopFullColorWave();
                    panelImage.color = full;
                }
            }
            else
            {
                StopFullColorWave();
                panelImage.color = normal;
            }
        }

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

        SetPanelFullScale(isFull);
        RefreshFullPulse(isFull);
    }

    // ── Animación del panel cuando está lleno ────────────────────────────

    /// <summary>RectTransform a animar: el asignado o, por defecto, el del panel (fondo + texto).</summary>
    private RectTransform FullPulseRect
    {
        get
        {
            if (fullPulseTarget != null) return fullPulseTarget;
            return panelImage != null ? panelImage.rectTransform : null;
        }
    }

    private void CaptureFullPulseBase(RectTransform rect)
    {
        if (rect == null || hasBasePulseTransform) return;
        basePulseScale = rect.localScale;
        basePulsePosition = rect.anchoredPosition;
        hasBasePulseTransform = true;
    }

    /// <summary>
    /// Asegura que exista el TMP_Text del mensaje. Si no se asignó en el Inspector,
    /// se crea en runtime como hijo del panel (así tiembla junto a él).
    /// </summary>
    private void EnsureFullMessageText()
    {
        // Si ya existe (asignado desde el Inspector o creado antes), solo aplicar la escala configurada.
        if (fullMessageText != null)
        {
            ApplyFullMessageScale();
            return;
        }

        if (!showFullMessage) return;

        RectTransform parent = FullPulseRect;
        if (parent == null) return;

        if (counterText == null)
        {
            Debug.LogWarning($"[CollectibleCounterUI] No se pudo crear el mensaje '{fullMessage}' en {name}: falta un TMP_Text de referencia para la fuente.");
            return;
        }

        GameObject go = new GameObject("FullMessage", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(600f, 80f);
        rect.anchoredPosition = fullMessageOffset;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.font = counterText.font;
        text.text = fullMessage;
        text.fontSize = fullMessageFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        Color c = fullMessageColor;
        c.a = 0f;
        text.color = c;

        fullMessageText = text;
        ApplyFullMessageScale();
    }

    /// <summary>Aplica la escala configurada al texto del mensaje (1 = tamaño de fuente original).</summary>
    private void ApplyFullMessageScale()
    {
        if (fullMessageText == null) return;
        fullMessageText.rectTransform.localScale = Vector3.one * Mathf.Max(0.1f, fullMessageScale);
    }

    /// <summary>
    /// Escala la imagen de fondo del panel (rect animado) mientras el inventario está lleno,
    /// para que el texto agrandado quepa dentro. Se restaura al volver a estado normal.
    /// </summary>
    private void SetPanelFullScale(bool isFull)
    {
        if (!growPanelWhenFull) return;
        RectTransform rect = FullPulseRect;
        if (rect == null) return;
        if (!hasBasePulseTransform) CaptureFullPulseBase(rect);
        // Si hay un pulso en curso, la corrutina administra la escala y restaurará el base al terminar.
        if (fullPulseRoutine != null) return;
        float multiplier = isFull ? Mathf.Max(1f, fullPanelScaleMultiplier) : 1f;
        rect.localScale = basePulseScale * multiplier;
    }

    /// <summary>Aplica el alfa del mensaje (0 = invisible), conservando el color configurado.</summary>
    private void SetFullMessageAlpha(float alpha)
    {
        if (!showFullMessage || fullMessageText == null) return;
        Color c = fullMessageColor;
        c.a = Mathf.Clamp01(alpha);
        fullMessageText.color = c;
    }

    /// <summary>Reproduce un pulso al pasar a "lleno"; al dejar de estarlo detiene y restaura el panel.</summary>
    private void RefreshFullPulse(bool isFull)
    {
        if (wasFull == isFull) return;
        wasFull = isFull;
        if (isFull)
            PlayFullPulse();
        else
            StopFullPulse();
    }

    /// <summary>Dispara un pulso de crecimiento + sacudida. Ignora el disparo si ya hay uno en curso.</summary>
    private void PlayFullPulse()
    {
        if (!fullPanelAnimation || !isActiveAndEnabled) return;
        if (fullPulseRoutine != null) return;
        RectTransform rect = FullPulseRect;
        if (rect == null) return;
        CaptureFullPulseBase(rect);
        EnsureFullMessageText();
        fullPulseRoutine = StartCoroutine(FullPanelPulseRoutine(rect));
    }

    private void StopFullPulse()
    {
        if (fullPulseRoutine != null)
        {
            StopCoroutine(fullPulseRoutine);
            fullPulseRoutine = null;
        }
        RectTransform rect = FullPulseRect;
        if (hasBasePulseTransform && rect != null)
        {
            rect.localScale = basePulseScale;
            rect.anchoredPosition = basePulsePosition;
        }
        SetFullMessageAlpha(0f);
    }

    // ── Variación senoidal del color al estar lleno ──────────────────────

    /// <summary>Inicia (una sola vez) la oscilación senoidal del color de fondo del panel.</summary>
    private void StartFullColorWave(Color baseColor)
    {
        if (fullColorWaveRoutine != null || panelImage == null) return;
        fullColorWaveRoutine = StartCoroutine(FullColorWaveRoutine(baseColor));
    }

    private void StopFullColorWave()
    {
        if (fullColorWaveRoutine == null) return;
        StopCoroutine(fullColorWaveRoutine);
        fullColorWaveRoutine = null;
    }

    /// <summary>
    /// Mientras el inventario esté lleno, el color de fondo del panel varía constantemente
    /// con una función seno: oscila entre el color base "lleno" y fullColorWaveColor.
    /// </summary>
    private IEnumerator FullColorWaveRoutine(Color baseColor)
    {
        float time = 0f;
        float speed = Mathf.Max(0.01f, fullColorWaveSpeed);
        while (panelImage != null)
        {
            time += Time.unscaledDeltaTime * speed;
            // Función seno: 0 → 1 → 0 suavemente, escalado por la amplitud configurada.
            float wave = (0.5f + 0.5f * Mathf.Sin(time)) * fullColorWaveAmplitude;
            panelImage.color = Color.Lerp(baseColor, fullColorWaveColor, wave);
            yield return null;
        }
        fullColorWaveRoutine = null;
    }

    /// <summary>
    /// Un pulso: el panel crece hasta fullPulseScale con una pequeña sacudida y vuelve
    /// exactamente al tamaño/posición base. Se dispara al llenarse o al intentar recoger
    /// otro item estando lleno (una vez por contacto).
    /// </summary>
    private IEnumerator FullPanelPulseRoutine(RectTransform rect)
    {
        float duration = Mathf.Max(0.01f, fullPulseDuration);
        float scaleFactor = Mathf.Max(1f, fullPulseScale);
        // Partir de la escala actual (puede estar agrandada por el estado lleno).
        Vector3 startScale = rect.localScale;
        Vector3 targetScale = startScale * scaleFactor;

        SetFullMessageAlpha(0f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            float clamped = Mathf.Clamp01(t);
            // Curva 0 → 1 → 0 para que el pulso suba y baje suavemente.
            float pulse = Mathf.Sin(clamped * Mathf.PI);
            rect.localScale = Vector3.LerpUnclamped(startScale, targetScale, pulse);
            float shake = fullShakeStrength * pulse;
            float phase = clamped * Mathf.PI * 6f;
            rect.anchoredPosition = basePulsePosition
                + new Vector2(Mathf.Sin(phase) * shake, Mathf.Cos(phase * 0.7f) * shake * 0.5f);
            SetFullMessageAlpha(pulse);
            yield return null;
        }

        rect.localScale = startScale;
        rect.anchoredPosition = basePulsePosition;
        SetFullMessageAlpha(0f);
        fullPulseRoutine = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(format)) format = "{0} / {1}";
        if (string.IsNullOrEmpty(scoreFormat)) scoreFormat = "Puntos: {0}";
        if (string.IsNullOrEmpty(combinedFormat)) combinedFormat = "Puntos: {0} | {1}/{2}";
        if (counterText == null)
            counterText = GetComponentInChildren<TMP_Text>(true);
        if (panelImage == null)
            FindPanelImage();
        if (fullPulseScale < 1f) fullPulseScale = 1f;
        if (fullPulseDuration <= 0f) fullPulseDuration = 0.35f;
        if (fullShakeStrength < 0f) fullShakeStrength = 0f;
        if (fullPanelScaleMultiplier < 1f) fullPanelScaleMultiplier = 1f;
        if (fullMessageFontSize <= 0f) fullMessageFontSize = 42f;
        if (fullMessageScale < 0.1f) fullMessageScale = 1f;
        if (fullColorWaveSpeed < 0.01f) fullColorWaveSpeed = 5f;
        if (string.IsNullOrEmpty(fullMessage)) fullMessage = "Maleta llena";
        // Actualizar preview en editor si hay player en escena
        if (Application.isPlaying) RefreshUI();
    }
#endif
}
