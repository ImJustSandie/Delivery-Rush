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
/// Gestor local de musica de fondo (una instancia por cliente, sin red).
/// Reproduce un tema en loop segun la escena activa: añade entradas en
/// sceneMusicTracks (ej. Scene Name = MainScene + tu AudioClip) y suena solo.
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
    [Tooltip("Si es true, al entrar en una escena sin tema asignado la musica se detiene. Si es false, sigue sonando el tema anterior.")]
    [SerializeField] private bool stopMusicWhenNoEntry = false;
    [Tooltip("Fundido de salida/entrada al cambiar de tema (segundos). 0 = corte directo.")]
    [SerializeField] private float fadeDuration = 1f;

    private AudioSource musicSource;
    private Coroutine fadeRoutine;
    private const string VolumePrefsKey = "GameAudioManager.MusicVolume";

    public float MusicVolume => musicSource != null ? musicSource.volume : musicVolume;
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
        EnsureMusicSource();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        PlayMusicForScene(SceneManager.GetActiveScene().name);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
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
    /// </summary>
    public void PlayMusicForScene(string sceneName)
    {
        AudioClip clip = FindTrackForScene(sceneName);
        if (clip == null)
        {
            if (stopMusicWhenNoEntry)
            {
                StopMusic();
            }
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
        if (musicSource.clip == clip && musicSource.isPlaying)
        {
            return;
        }
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (fadeDuration > 0f && musicSource.isPlaying)
        {
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
    /// </summary>
    public static void PlayOneShot2D(AudioClip clip, float volume)
    {
        if (clip == null) return;
        GameObject go = new GameObject("OneShot2D_SFX");
        AudioSource source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.volume = Mathf.Clamp01(volume);
        source.PlayOneShot(clip);
        Destroy(go, clip.length + 0.1f);
    }

    /// <summary>
    /// Cambia el volumen (0 a 1) y lo guarda entre sesiones.
    /// </summary>
    public void SetVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
        PlayerPrefs.SetFloat(VolumePrefsKey, musicVolume);
        PlayerPrefs.Save();
    }

    private AudioClip FindTrackForScene(string sceneName)
    {
        foreach (SceneMusicEntry entry in sceneMusicTracks)
        {
            if (entry != null && entry.musicClip != null
                && string.Equals(entry.sceneName, sceneName))
            {
                return entry.musicClip;
            }
        }
        return null;
    }

    private IEnumerator FadeToTrackRoutine(AudioClip nextClip)
    {
        float duration = Mathf.Max(0.01f, fadeDuration);
        float startVolume = musicSource.volume;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
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
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        musicVolume = Mathf.Clamp01(musicVolume);
        if (fadeDuration < 0f) fadeDuration = 0f;
    }
#endif
}
