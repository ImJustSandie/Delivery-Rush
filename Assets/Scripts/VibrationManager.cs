using UnityEngine;

/// <summary>
/// Tipos de fuerza de vibracion. Cada nivel mapea a una amplitud del motor
/// (0 a 255 en Android); a mayor valor, mas fuerte vibra el celular.
/// </summary>
public enum VibrationStrength
{
    Light = 0,
    Medium = 1,
    Strong = 2
}

/// <summary>
/// Utilidad generica de vibracion. Cualquier script puede llamarla sin
/// instancia ni GameObject en escena, por ejemplo para:
///   - Recoger un objeto:    VibrationManager.Vibrate(VibrationStrength.Light, 0.05f);
///   - Impacto / power-up:   VibrationManager.Vibrate(VibrationStrength.Medium, 0.25f);
///   - Golpe fuerte / derrota: VibrationManager.Vibrate(VibrationStrength.Strong, 0.7f);
///
/// Comportamiento por plataforma:
///   - Android: usa el servicio Vibrator nativo (JNI). Con control de amplitud
///     respeta la fuerza elegida; si el motor no lo soporta, vibra a maxima
///     intensidad el tiempo indicado.
///   - iOS: usa Handheld.Vibrate (duracion fija del sistema, se ignora el tiempo).
///   - Editor / escritorio: no hace nada (solo un log de debug opcional).
/// </summary>
public static class VibrationManager
{
    [Header("Vibration Settings")]
    [Tooltip("Activa o desactiva globalmente la vibracion del juego.")]
    public static bool Enabled = true;

    [Tooltip("Amplitud (1-255) del nivel Light.")]
    public static int LightAmplitude = 60;

    [Tooltip("Amplitud (1-255) del nivel Medium.")]
    public static int MediumAmplitude = 150;

    [Tooltip("Amplitud (1-255) del nivel Strong.")]
    public static int StrongAmplitude = 255;

    [Tooltip("Duracion por defecto en segundos si la llamada no especifica una.")]
    public static float DefaultDurationSeconds = 0.2f;

    private const float MaxDurationSeconds = 5f;

    /// <summary>
    /// Llamada generica de vibracion. Es la funcion que deben usar los demas
    /// sistemas del juego en cualquier apartado que necesite haptica.
    /// </summary>
    /// <param name="strength">Tipo de fuerza: Light, Medium o Strong.</param>
    /// <param name="durationSeconds">Duracion en segundos (se clampa entre 0.01 y 5).</param>
    public static void Vibrate(VibrationStrength strength, float durationSeconds = -1f)
    {
        if (!Enabled) return;

        float duration = durationSeconds > 0f ? durationSeconds : DefaultDurationSeconds;
        duration = Mathf.Clamp(duration, 0.01f, MaxDurationSeconds);

        int amplitude = GetAmplitude(strength);

#if UNITY_ANDROID && !UNITY_EDITOR
        VibrateAndroid(duration, amplitude);
#elif UNITY_IOS && !UNITY_EDITOR
        // iOS no expone control de amplitud ni duracion publicos; vibra con el
        // patron corto fijo del sistema.
        Handheld.Vibrate();
#else
        if (Debug.isDebugBuild)
        {
            Debug.Log($"[VibrationManager] Vibracion simulada: strength={strength}, amplitud={amplitude}, duracion={duration:0.00}s");
        }
#endif
    }

    /// <summary>Cancela cualquier vibracion en curso (solo tiene efecto en Android).</summary>
    public static void Cancel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var vibrator = GetAndroidVibrator())
        {
            if (vibrator == null) return;
            vibrator.Call("cancel");
        }
#endif
    }

    private static int GetAmplitude(VibrationStrength strength)
    {
        switch (strength)
        {
            case VibrationStrength.Light: return Mathf.Clamp(LightAmplitude, 1, 255);
            case VibrationStrength.Medium: return Mathf.Clamp(MediumAmplitude, 1, 255);
            case VibrationStrength.Strong: return Mathf.Clamp(StrongAmplitude, 1, 255);
            default: return MediumAmplitude;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void VibrateAndroid(float durationSeconds, int amplitude)
    {
        int milliseconds = Mathf.CeilToInt(durationSeconds * 1000f);

        using (var vibrator = GetAndroidVibrator())
        {
            if (vibrator == null) return;

            try
            {
                if (vibrator.Call<bool>("hasVibrator") && vibrator.Call<bool>("hasAmplitudeControl"))
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude))
                    {
                        vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    // Motor sin control de amplitud: vibra a maxima intensidad.
                    vibrator.Call("vibrate", milliseconds);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VibrationManager] Fallo al vibrar en Android: {e.Message}");
            }
        }
    }

    private static AndroidJavaObject GetAndroidVibrator()
    {
        AndroidJavaObject vibrator = null;
        try
        {
            using (var playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                // Android 12+ (API 31): el servicio "vibrator" esta deprecated y
                // se pide por VibratorManager. Si no existe, se usa el camino antiguo.
                try
                {
                    using (var vibratorManager = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                    {
                        if (vibratorManager != null)
                        {
                            vibrator = vibratorManager.Call<AndroidJavaObject>("getDefaultVibrator");
                        }
                    }
                }
                catch
                {
                    // API < 31: no hay VibratorManager.
                }

                if (vibrator == null)
                {
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VibrationManager] No se pudo obtener el Vibrator de Android: {e.Message}");
        }

        return vibrator;
    }
#endif
}
