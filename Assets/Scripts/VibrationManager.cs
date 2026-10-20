using UnityEngine;

/// <summary>
/// Utilidad generica de vibracion. Cualquier script puede llamarla sin
/// instancia ni GameObject en escena:
///   - Aviso corto:   VibrationManager.Vibrate(0.1f);
///   - Aviso normal:  VibrationManager.Vibrate(0.25f);
///   - Aviso largo:   VibrationManager.Vibrate(0.7f);
///
/// No incluye niveles de fuerza a proposito: en varios equipos (ej. Honor)
/// el motor no soporta control de amplitud y cualquier variacion de
/// intensidad se ignora o la descarta el sistema. vibrar siempre con la
/// llamada simple por duracion es compatible con todos los dispositivos.
///
/// Comportamiento por plataforma:
///   - Android: usa el servicio Vibrator nativo con duracion en ms.
///   - iOS: usa Handheld.Vibrate (duracion fija del sistema).
///   - Editor / escritorio: no hace nada (solo log de diagnostico temporal).
/// </summary>
public static class VibrationManager
{
    [Header("Vibration Settings")]
    [Tooltip("Activa o desactiva globalmente la vibracion del juego.")]
    public static bool Enabled = true;

    [Tooltip("Duracion por defecto en segundos si la llamada no especifica una.")]
    public static float DefaultDurationSeconds = 0.2f;

    private const float MaxDurationSeconds = 5f;

    /// <summary>
    /// Llamada generica de vibracion. Es la funcion que deben usar los demas
    /// sistemas del juego en cualquier apartado que necesite haptica.
    /// </summary>
    /// <param name="durationSeconds">Duracion en segundos (se clampa entre 0.01 y 5).</param>
    public static void Vibrate(float durationSeconds = -1f)
    {
        if (!Enabled)
        {
            Debug.Log("[VibrationManager] Skip: Enabled=false");
            return;
        }

        float duration = durationSeconds > 0f ? durationSeconds : DefaultDurationSeconds;
        duration = Mathf.Clamp(duration, 0.01f, MaxDurationSeconds);
        Debug.Log($"[VibrationManager] Vibrate: duracion={duration:0.00}s");

#if UNITY_ANDROID && !UNITY_EDITOR
        VibrateAndroid(duration);
#elif UNITY_IOS && !UNITY_EDITOR
        // iOS no expone duracion publica; vibra con el patron fijo del sistema.
        Handheld.Vibrate();
#else
        Debug.Log("[VibrationManager] Plataforma sin vibracion (Editor/PC): solo simulado.");
#endif
    }

    /// <summary>Cancela cualquier vibracion en curso (solo tiene efecto en Android).</summary>
    public static void Cancel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var vibrator = GetAndroidVibrator())
        {
            if (vibrator == null) return;
            try
            {
                vibrator.Call("cancel");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VibrationManager] Fallo al cancelar vibracion: {e}");
            }
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void VibrateAndroid(float durationSeconds)
    {
        int milliseconds = Mathf.CeilToInt(durationSeconds * 1000f);

        using (var vibrator = GetAndroidVibrator())
        {
            if (vibrator == null)
            {
                Debug.LogWarning("[VibrationManager] VibrateAndroid aborta: vibrator null");
                return;
            }

            try
            {
                // Llamada simple por duracion: todos los motores, incluso los sin
                // control de amplitud (ej. Honor), la aceptan.
                vibrator.Call("vibrate", milliseconds);
                Debug.Log($"[VibrationManager] Vibrando: ms={milliseconds}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VibrationManager] Fallo al vibrar en Android: {e}");
            }
        }
    }

    private static AndroidJavaObject GetAndroidVibrator()
    {
        AndroidJavaObject vibrator = null;
        try
        {
            // El servicio "vibrator" sigue funcionando en API 31+ (solo deprecated)
            // y existe en todas las versiones anteriores.
            using (var playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VibrationManager] No se pudo obtener el Vibrator de Android: {e}");
        }

        return vibrator;
    }
#endif
}
