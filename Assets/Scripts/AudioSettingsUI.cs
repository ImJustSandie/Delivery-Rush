using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Enlaza los 2 sliders del panel de audio del menu principal con
/// <see cref="GameAudioManager"/>: uno para la musica de fondo y otro
/// para los efectos/VFX. Solo codigo: no toca escenas ni prefabs.
/// Al arrancar, los sliders toman el valor guardado en PlayerPrefs y
/// al moverlos se guarda automaticamente. Los cambios aplican en vivo
/// (la musica cambia al instante y los efectos en el proximo sonido).
/// </summary>
public class AudioSettingsUI : MonoBehaviour
{
    [Header("Sliders (opcional: se autodetectan si se dejan vacios)")]
    [Tooltip("Slider del volumen de musica de fondo (0 a 1).")]
    [SerializeField] private Slider musicSlider;
    [Tooltip("Slider del volumen de efectos/VFX del juego (0 a 1).")]
    [SerializeField] private Slider sfxSlider;

    [Header("Autodeteccion")]
    [Tooltip("Si es true y los sliders no estan asignados, se buscan por nombre entre los hijos (contiene 'music' / 'sfx', 'vfx', 'fx', 'efecto').")]
    [SerializeField] private bool autoFindSliders = true;

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
    }

    private void Start()
    {
        if (panelRoot == null) panelRoot = gameObject;        if (autoFindSliders && (musicSlider == null || sfxSlider == null))
        {
            AutoFindSliders();
        }
        SyncSlidersFromManager();
        AttachListeners();
    }

    private void OnEnable()
    {
        SyncSlidersFromManager();
    }

    private void OnDestroy()
    {
        DetachListeners();
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

    /// <summary>Pone cada slider en el valor guardado, sin disparar eventos.</summary>
    private void SyncSlidersFromManager()
    {
        float music = GameAudioManager.Instance != null
            ? GameAudioManager.Instance.MusicVolume
            : PlayerPrefs.GetFloat("GameAudioManager.MusicVolume", 0.7f);
        float sfx = GameAudioManager.GetSfxVolume();
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(Mathf.Clamp01(music));
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(Mathf.Clamp01(sfx));
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
    /// Callback para el OnValueChanged del slider de musica (tambien vale
    /// asignarlo manualmente en el Inspector si no quieres listeners por codigo).
    /// </summary>
    public void OnMusicSliderChanged(float value)
    {
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
