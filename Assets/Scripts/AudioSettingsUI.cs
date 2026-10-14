using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Enlaza los 2 sliders del panel de audio del menu principal con
/// <see cref="GameAudioManager"/>: uno para la musica de fondo y otro
/// para los efectos/VFX. Solo codigo: no toca escenas ni prefabs.
/// Al arrancar, los sliders toman el valor guardado en PlayerPrefs y
/// al moverlos se guarda automaticamente. Los cambios aplican en vivo
/// (la musica cambia al instante y los efectos en el proximo sonido).
/// Incluye cambio de color en el fill al llegar al maximo y etiquetas de porcentaje.
/// </summary>
public class AudioSettingsUI : MonoBehaviour
{
    [Header("Sliders (opcional: se autodetectan si se dejan vacios)")]
    [Tooltip("Slider del volumen de musica de fondo (0 a 1).")]
    [SerializeField] private Slider musicSlider;
    [Tooltip("Slider del volumen de efectos/VFX del juego (0 a 1).")]
    [SerializeField] private Slider sfxSlider;

    [Header("Etiquetas de Porcentaje (TMP_Text)")]
    [Tooltip("Texto para mostrar el porcentaje de volumen de musica (ej. '100%'). Opcional.")]
    [SerializeField] private TMP_Text musicPercentText;
    [Tooltip("Texto para mostrar el porcentaje de volumen de efectos/SFX (ej. '100%'). Opcional.")]
    [SerializeField] private TMP_Text sfxPercentText;
    [Tooltip("Formato del texto de porcentaje. {0} se reemplaza por el valor entero (0 a 100).")]
    [SerializeField] private string percentFormat = "{0}%";

    [Header("Imagenes de Relleno (Fill)")]
    [Tooltip("Imagen del relleno del slider de musica. Si esta vacia, se detecta desde musicSlider.fillRect.")]
    [SerializeField] private Image musicFillImage;
    [Tooltip("Imagen del relleno del slider de SFX. Si esta vacia, se detecta desde sfxSlider.fillRect.")]
    [SerializeField] private Image sfxFillImage;

    [Header("Colores de Relleno")]
    [Tooltip("Color de la barra de relleno cuando el volumen disminuye / no esta al maximo.")]
    [SerializeField] private Color normalFillColor = new Color(0.2f, 0.6f, 1f, 1f);
    [Tooltip("Color de la barra de relleno cuando el volumen esta al maximo (100%).")]
    [SerializeField] private Color maxFillColor = new Color(0.2f, 0.9f, 0.4f, 1f);

    [Header("Autodeteccion")]
    [Tooltip("Si es true y los sliders no estan asignados, se buscan por nombre entre los hijos (contiene 'music' / 'sfx', 'vfx', 'fx', 'efecto').")]
    [SerializeField] private bool autoFindSliders = true;
    [Tooltip("Si es true, busca automaticamente textos de porcentaje o fills no asignados en los hijos de cada slider.")]
    [SerializeField] private bool autoFindFillAndLabels = true;

    [Header("Panel (para abrir/cerrar desde un boton)")]
    [Tooltip("Panel que se muestra/oculta con Show()/Hide()/Toggle(). Por defecto es el propio GameObject. Asignalo si el script esta en otro objeto.")]
    [SerializeField] private GameObject panelRoot;

    private bool listenersAttached;

    private void Awake()
    {
        if (autoFindSliders)
        {
            AutoFindSliders();
        }
        ResolveVisualReferences();
    }

    private void Start()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (autoFindSliders && (musicSlider == null || sfxSlider == null))
        {
            AutoFindSliders();
        }
        ResolveVisualReferences();
        SyncSlidersFromManager();
        AttachListeners();
    }

    private void OnEnable()
    {
        ResolveVisualReferences();
        SyncSlidersFromManager();
    }

    private void OnDestroy()
    {
        DetachListeners();
    }

    /// <summary>
    /// Resuelve las referencias a los Image del Fill y a los TMP_Text de porcentaje
    /// si no se asignaron manualmente en el Inspector.
    /// </summary>
    private void ResolveVisualReferences()
    {
        // Resolver Fill Image para musica
        if (musicFillImage == null && musicSlider != null && musicSlider.fillRect != null)
        {
            musicFillImage = musicSlider.fillRect.GetComponent<Image>();
        }

        // Resolver Fill Image para SFX
        if (sfxFillImage == null && sfxSlider != null && sfxSlider.fillRect != null)
        {
            sfxFillImage = sfxSlider.fillRect.GetComponent<Image>();
        }

        // Autodeteccion opcional de TMP_Text si no estan asignados
        if (autoFindFillAndLabels)
        {
            if (musicPercentText == null && musicSlider != null)
            {
                musicPercentText = FindPercentTextInHierarchy(musicSlider.transform, "music", "musica");
            }
            if (sfxPercentText == null && sfxSlider != null)
            {
                sfxPercentText = FindPercentTextInHierarchy(sfxSlider.transform, "sfx", "vfx", "fx", "efecto");
            }
        }
    }

    /// <summary>
    /// Intenta buscar un TMP_Text hijo o hermano que concuerde con porcentaje.
    /// </summary>
    private TMP_Text FindPercentTextInHierarchy(Transform sliderTransform, params string[] keywords)
    {
        if (sliderTransform == null) return null;

        // 1. Buscar entre hijos del slider
        TMP_Text[] inChildren = sliderTransform.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text t in inChildren)
        {
            if (t == null) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("%") || n.Contains("percent") || n.Contains("porcentaje") || n.Contains("val") || n.Contains("txt"))
            {
                return t;
            }
        }
        if (inChildren.Length > 0)
        {
            return inChildren[0];
        }

        // 2. Buscar entre hermanos del slider
        if (sliderTransform.parent != null)
        {
            TMP_Text[] inSiblings = sliderTransform.parent.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text t in inSiblings)
            {
                if (t == null) continue;
                string n = t.name.ToLowerInvariant();
                foreach (string kw in keywords)
                {
                    if (n.Contains(kw) && (n.Contains("%") || n.Contains("percent") || n.Contains("val") || n.Contains("text") || n.Contains("txt")))
                    {
                        return t;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Busca los sliders entre los hijos por nombre. El primero que contenga
    /// "music"/"musica" se usa para musica; el que contenga "sfx"/"vfx"/"fx"/
    /// "efecto"/"effect" para efectos. Si no hay coincidencia por nombre y hay
    /// exactamente 2 sliders, se asignan por orden jerarquico.
    /// </summary>
    private void AutoFindSliders()
    {
        Slider[] sliders = GetComponentsInChildren<Slider>(true);
        if (sliders == null || sliders.Length == 0)
        {
            return;
        }
        foreach (Slider s in sliders)
        {
            if (s == null) continue;
            string n = s.name.ToLowerInvariant();
            bool looksMusic = n.Contains("music") || n.Contains("musica");
            bool looksSfx = n.Contains("sfx") || n.Contains("vfx") || n.Contains("efecto") || n.Contains("effect") || n == "fx" || n.Contains("fx");
            if (looksMusic && musicSlider == null)
            {
                musicSlider = s;
            }
            else if (looksSfx && sfxSlider == null)
            {
                sfxSlider = s;
            }
        }
        if ((musicSlider == null || sfxSlider == null) && sliders.Length >= 2)
        {
            if (musicSlider == null) musicSlider = sliders[0];
            if (sfxSlider == null) sfxSlider = sliders[1];
            Debug.Log("[AudioSettingsUI] Sliders asignados por orden jerarquico (revisa que el primero sea musica y el segundo efectos).");
        }
    }

    /// <summary>Pone cada slider en el valor guardado, sin disparar eventos, y refresca visuales.</summary>
    private void SyncSlidersFromManager()
    {
        float music = GameAudioManager.Instance != null
            ? GameAudioManager.Instance.MusicVolume
            : PlayerPrefs.GetFloat("GameAudioManager.MusicVolume", 0.7f);
        float sfx = GameAudioManager.GetSfxVolume();

        float clampedMusic = Mathf.Clamp01(music);
        float clampedSfx = Mathf.Clamp01(sfx);

        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(clampedMusic);
            UpdateSliderVisuals(musicSlider, musicFillImage, musicPercentText, clampedMusic);
        }

        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(clampedSfx);
            UpdateSliderVisuals(sfxSlider, sfxFillImage, sfxPercentText, clampedSfx);
        }
    }

    private void AttachListeners()
    {
        if (listenersAttached) return;
        listenersAttached = true;
        if (musicSlider != null) musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
    }

    private void DetachListeners()
    {
        if (musicSlider != null) musicSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(OnSfxSliderChanged);
        listenersAttached = false;
    }

    /// <summary>
    /// Actualiza el color de relleno (fill) y la etiqueta de texto con el porcentaje actual.
    /// </summary>
    private void UpdateSliderVisuals(Slider slider, Image fillImage, TMP_Text percentLabel, float value)
    {
        float normalized = 0f;
        if (slider != null && slider.maxValue > slider.minValue)
        {
            normalized = Mathf.InverseLerp(slider.minValue, slider.maxValue, value);
        }
        else
        {
            normalized = Mathf.Clamp01(value);
        }

        int percent = Mathf.RoundToInt(normalized * 100f);

        // Actualizar texto
        if (percentLabel != null)
        {
            percentLabel.text = string.Format(percentFormat, percent);
        }

        // Actualizar color del relleno
        if (fillImage != null)
        {
            bool isMax = normalized >= 0.999f;
            fillImage.color = isMax ? maxFillColor : normalFillColor;
        }
    }

    /// <summary>
    /// Callback para el OnValueChanged del slider de musica (tambien vale
    /// asignarlo manualmente en el Inspector si no quieres listeners por codigo).
    /// </summary>
    public void OnMusicSliderChanged(float value)
    {
        UpdateSliderVisuals(musicSlider, musicFillImage, musicPercentText, value);

        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.SetMusicVolume(value);
        }
        else
        {
            PlayerPrefs.SetFloat("GameAudioManager.MusicVolume", Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Callback para el OnValueChanged del slider de efectos/VFX.
    /// </summary>
    public void OnSfxSliderChanged(float value)
    {
        UpdateSliderVisuals(sfxSlider, sfxFillImage, sfxPercentText, value);

        if (GameAudioManager.Instance != null)
        {
            GameAudioManager.Instance.SetSfxVolume(value);
        }
        else
        {
            PlayerPrefs.SetFloat("GameAudioManager.SfxVolume", Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }

    // --- Mostrar/ocultar el panel desde un boton (OnClick) ---

    /// <summary>Muestra el panel de audio. Conectalo al OnClick del boton de ajustes.</summary>
    public void Show()
    {
        GameObject target = panelRoot != null ? panelRoot : gameObject;
        target.SetActive(true);
    }

    /// <summary>Oculta el panel de audio. Conectalo al OnClick del boton de cerrar.</summary>
    public void Hide()
    {
        GameObject target = panelRoot != null ? panelRoot : gameObject;
        target.SetActive(false);
    }

    /// <summary>Alterna el panel de audio (un solo boton abre/cierra).</summary>
    public void Toggle()
    {
        GameObject target = panelRoot != null ? panelRoot : gameObject;
        target.SetActive(!target.activeSelf);
    }
}
