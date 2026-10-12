using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Musica de fondo asociada a un nombre de escena.
/// </summary>
[System.Serializable]
public class SceneMusicEntry
{
    [Tooltip("Nombre exacto de la escena (ej. MainScene).")]
    public string sceneName = "MainScene";
    [Tooltip("Tema musical que suena al entrar en esa escena. En loop.")]
    public AudioClip musicClip;
}

/// <summary>
/// Gestor local de musica de fondo persistente (una instancia por cliente, sin red).
/// La musica suena de forma continua entre escenas gracias a DontDestroyOnLoad:
/// solo cambia al llegar a una escena con tema distinto asignado en
/// sceneMusicTracks (ej. MainScene). Las escenas sin entrada mantienen el
/// tema anterior sin cortes.
/// El AudioSource se crea por codigo en modo 2D (spatialBlend 0), asi la
/// musica se escucha igual sin importar donde este el AudioListener del
/// jugador local (ver PlayerLocalAudioListener).
/// Solo codigo: colocar el componente en un GameObject de la primera escena.
/// </summary>
public class GameAudioManager : MonoBehaviour
{
    public static GameAudioManager Instance { get; private set; }

    [Header("Music Tracks")]
    [Tooltip("Un tema por escena. Para la escena de juego añade: Scene Name = MainScene + tu clip de musica.")]
    [SerializeField] private List<SceneMusicEntry> sceneMusicTracks = new List<SceneMusicEntry>();

    [Header("Playback")]
    [Tooltip("Volumen de la musica (0 a 1). Se guarda entre sesiones.")]
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.7f;
    [Tooltip("Volumen de los efectos / VFX (0 a 1). Se guarda entre sesiones. Multiplica a todos los sonidos puntuales.")]
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;
    [Tooltip("Tema por defecto si al arrancar no hay musica sonando y la escena activa no tiene tema asignado. Si es null, se queda en silencio hasta llegar a una escena con tema.")]
    [SerializeField] private AudioClip defaultMusicClip;
    [Tooltip("Si es true, al entrar en una escena sin tema asignado la musica se detiene. Si es false (recomendado), sigue sonando el tema anterior sin cortes.")]
    [SerializeField] private bool stopMusicWhenNoEntry = false;
    [Tooltip("Fundido de salida/entrada al cambiar de tema (segundos). 0 = corte directo.")]
    [SerializeField] private float fadeDuration = 1f;

    private AudioSource musicSource;
    private Coroutine fadeRoutine;
    private Coroutine ensurePlayingRoutine;
    private AudioClip desiredTrack;
    private AudioClip fadingToClip;
    private const string VolumePrefsKey = "GameAudioManager.MusicVolume";
    private const string SfxVolumePrefsKey = "GameAudioManager.SfxVolume";

    /// <summary>Se invoca cada vez que cambia el volumen de musica o de efectos.</summary>
    public static event System.Action VolumesChanged;

    public float MusicVolume => musicSource != null ? musicSource.volume : musicVolume;
    public float SfxVolume => sfxVolume;
    public bool IsPlaying => musicSource != null && musicSource.isPlaying;
    public AudioClip CurrentTrack => musicSource != null ? musicSource.clip : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        musicVolume = PlayerPrefs.GetFloat(VolumePrefsKey, musicVolume);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumePrefsKey, sfxVolume);
        EnsureMusicSource();
    }

    private void OnEnable()
    {
        // El duplicado destruido en Awake no debe suscribirse.
        if (Instance != null && Instance != this)
        {
            return;
        }
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        // Intento inicial: si la escena actual tiene tema, ese es el deseado.
        // Si no tiene y no hay nada sonando, se usa el tema por defecto (si hay).
        PlayMusicForScene(SceneManager.GetActiveScene().name);
        if (ensurePlayingRoutine == null)
        {
            ensurePlayingRoutine = StartCoroutine(EnsurePlayingRoutine());
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != null && Instance != this)
        {
            return;
        }
        // Las cargas aditivas (UI,Managers) no deben cortar la musica.
        if (mode != LoadSceneMode.Single)
        {
            return;
        }
        PlayMusicForScene(scene.name);
    }

    /// <summary>
    /// Crea el AudioSource de musica por codigo si no existe (2D, en loop).
    /// </summary>
    private void EnsureMusicSource()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;
    }

    /// <summary>
    /// Reproduce el tema asignado a la escena indicada, si lo hay.
    /// Si la escena no tiene tema asignado, la musica actual sigue sonando
    /// sin cortes (persistente entre escenas), salvo que
    /// stopMusicWhenNoEntry sea true.
    /// </summary>
    public void PlayMusicForScene(string sceneName)
    {
        AudioClip clip = FindTrackForScene(sceneName);
        if (clip == null)
        {
            if (stopMusicWhenNoEntry)
            {
                StopMusic();
                return;
            }
            // Sin entrada: mantener lo que suena (comportamiento pedido).
            // Solo si no hay nada sonando, intentar el tema por defecto
            // para no arrancar en silencio (caso escena de conexion/inicial).
            if ((musicSource == null || !musicSource.isPlaying) && desiredTrack == null && defaultMusicClip != null)
            {
                PlayMusic(defaultMusicClip);
            }
            // Si ya habia un tema deseado (ej. ConnectionScene) pero aun no
            // suena por bloqueo de autoplay, no lo borres: el watchdog lo reintentara.
            return;
        }
        PlayMusic(clip);
    }

    /// <summary>
    /// Reproduce un tema en loop. Si ya suena, no hace nada.
    /// </summary>
    public void PlayMusic(AudioClip clip)
    {
        if (clip == null || musicSource == null)
        {
            return;
        }
        desiredTrack = clip;
        if (musicSource.clip == clip && musicSource.isPlaying)
        {
            return;
        }
        // Si ya hay un fundido en curso hacia este mismo clip, no reiniciarlo.
        if (fadeRoutine != null && fadingToClip == clip)
        {
            return;
        }
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
            fadingToClip = null;
        }
        if (fadeDuration > 0f && musicSource.isPlaying)
        {
            fadingToClip = clip;
            fadeRoutine = StartCoroutine(FadeToTrackRoutine(clip));
        }
        else
        {
            musicSource.clip = clip;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }
    }

    /// <summary>
    /// Detiene la musica actual.
    /// </summary>
    public void StopMusic()
    {
        desiredTrack = null;
        fadingToClip = null;
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
    }

    /// <summary>
    /// Reproduce un efecto puntual global (2D: lo oyen todos los listeners por igual,
    /// sin importar la distancia). Crea un emisor temporal que se autodestruye.
    /// El volumen final es volumen base * volumen de efectos (slider VFX/SFX).
    /// </summary>
    public static void PlayOneShot2D(AudioClip clip, float volume)
    {
        if (clip == null) return;
        float finalVolume = Mathf.Clamp01(volume) * GetSfxVolume();
        if (finalVolume <= 0.001f) return;
        GameObject go = new GameObject("OneShot2D_SFX");
        AudioSource source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.volume = finalVolume;
        source.PlayOneShot(clip);
        Destroy(go, clip.length + 0.1f);
    }

    /// <summary>
    /// Reproduce un efecto puntual 3D en una posicion (sobrevive al despawn/destroy).
    /// El volumen final es volumen base * volumen de efectos (slider VFX/SFX).
    /// Usalo para recolecciones, power-ups, fanfarrias, etc.
    /// </summary>
    public static void PlaySfxAtPoint(AudioClip clip, Vector3 position, float baseVolume = 1f)
    {
        if (clip == null) return;
        float finalVolume = Mathf.Clamp01(baseVolume) * GetSfxVolume();
        if (finalVolume <= 0.001f) return;
        AudioSource.PlayClipAtPoint(clip, position, finalVolume);
    }

    /// <summary>Volumen global de efectos actual (0 a 1). Si no hay instancia, retorna 1.</summary>
    public static float GetSfxVolume()
    {
        return Instance != null ? Instance.sfxVolume : 1f;
    }

    /// <summary>Aplica el volumen de efectos a un volumen base (0 a 1).</summary>
    public static float ApplySfxVolume(float baseVolume)
    {
        return Mathf.Clamp01(baseVolume) * GetSfxVolume();
    }

    /// <summary>
    /// Cambia el volumen de musica (0 a 1) y lo guarda entre sesiones.
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
        PlayerPrefs.SetFloat(VolumePrefsKey, musicVolume);
        PlayerPrefs.Save();
        VolumesChanged?.Invoke();
    }

    /// <summary>
    /// Cambia el volumen de efectos/VFX (0 a 1) y lo guarda entre sesiones.
    /// </summary>
    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumePrefsKey, sfxVolume);
        PlayerPrefs.Save();
        VolumesChanged?.Invoke();
    }

    /// <summary>
    /// Cambia el volumen de musica (0 a 1) y lo guarda entre sesiones.
    /// Alias historico de SetMusicVolume: se mantiene por compatibilidad.
    /// </summary>
    public void SetVolume(float volume)
    {
        SetMusicVolume(volume);
    }

    private AudioClip FindTrackForScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return null;
        }
        foreach (SceneMusicEntry entry in sceneMusicTracks)
        {
            if (entry != null && entry.musicClip != null
                && string.Equals(entry.sceneName.Trim(), sceneName.Trim(), System.StringComparison.Ordinal))
            {
                return entry.musicClip;
            }
        }
        return null;
    }

    /// <summary>
    /// Vigia que garantiza musica continua:
    /// - Si hay un tema deseado y no suena (ej. autoplay bloqueado en WebGL
    ///   en la escena de conexion antes del primer clic, o fundido
    ///   interrumpido con volumen en 0), lo reintenta sin fundido.
    /// Asi la cancion suena desde el arranque y persiste entre escenas;
    /// solo cambia al llegar a una escena con tema distinto asignado.
    /// </summary>
    private IEnumerator EnsurePlayingRoutine()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(0.5f);
        while (true)
        {
            if (desiredTrack != null && musicSource != null && musicVolume > 0f)
            {
                bool shouldBePlaying = fadeRoutine == null
                    && (!musicSource.isPlaying || musicSource.clip != desiredTrack);
                if (shouldBePlaying)
                {
                    musicSource.clip = desiredTrack;
                    musicSource.loop = true;
                    musicSource.spatialBlend = 0f;
                    musicSource.volume = musicVolume;
                    musicSource.Play();
                }
                else if (musicSource.isPlaying && musicSource.volume <= 0.001f && fadeRoutine == null)
                {
                    // Fundido interrumpido dejo el volumen en 0: restaurarlo.
                    musicSource.volume = musicVolume;
                }
            }
            yield return wait;
        }
    }

    private IEnumerator FadeToTrackRoutine(AudioClip nextClip)
    {
        float duration = Mathf.Max(0.01f, fadeDuration);
        float startVolume = musicSource != null ? musicSource.volume : musicVolume;
        float t = 0f;
        while (t < 1f)
        {
            if (musicSource == null)
            {
                fadeRoutine = null;
                fadingToClip = null;
                yield break;
            }
            t += Time.unscaledDeltaTime / duration;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }
        if (musicSource == null)
        {
            fadeRoutine = null;
            fadingToClip = null;
            yield break;
        }
        musicSource.Stop();
        musicSource.clip = nextClip;
        musicSource.Play();
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            musicSource.volume = Mathf.Lerp(0f, musicVolume, t);
            yield return null;
        }
        musicSource.volume = musicVolume;
        fadeRoutine = null;
        fadingToClip = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        musicVolume = Mathf.Clamp01(musicVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);
        if (fadeDuration < 0f) fadeDuration = 0f;
    }
#endif
}
