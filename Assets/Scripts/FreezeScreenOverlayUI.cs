using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Imagen a pantalla completa que se muestra mientras el jugador local está
/// congelado/ralentizado por el power-up SlowOthers (se ve en la pantalla del
/// jugador afectado, la de los demás no). Ponelo en un GameObject con Image
/// dentro del Canvas de la escena (Anchors full stretch, Raycast Target off).
/// Si <see cref="overlayImage"/> está vacío toma el Image del mismo GameObject.
/// Se enlaza solo al jugador local con el mismo patrón que <see cref="PowerUpUI"/>
/// y hace un fade de entrada/salida configurable.
/// </summary>
public class FreezeScreenOverlayUI : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Image a pantalla completa. Si se deja vacío se toma el del mismo GameObject.")]
    [SerializeField] private Image overlayImage;
    [Tooltip("Referencia opcional al Player local. Si se deja vacía se detecta automáticamente (owner o tutorial offline).")]
    [SerializeField] private PlayerMovementManager targetPlayer;

    [Header("Fade")]
    [Tooltip("Duración del fade de entrada/salida en segundos (0 = corte seco).")]
    [SerializeField] private float fadeDuration = 0.25f;

    private float maxAlpha;
    private float currentAlpha;
    private bool targetVisible;
    private bool isSubscribed;

    private void Awake()
    {
        if (overlayImage == null)
            overlayImage = GetComponent<Image>();

        if (overlayImage == null)
        {
            Debug.LogWarning($"[FreezeScreenOverlayUI] No se encontró Image en {name}. Asigna overlayImage en el Inspector.");
            return;
        }

        maxAlpha = overlayImage.color.a;
        if (maxAlpha <= 0f)
            Debug.LogWarning($"[FreezeScreenOverlayUI] El Image de {name} tiene alfa 0: la imagen nunca será visible. Sube la alfa del color en el Inspector.");
        overlayImage.raycastTarget = false;
        currentAlpha = 0f;
        overlayImage.enabled = false;
    }

    private void OnEnable()
    {
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Unsubscribe();
            targetPlayer = null;
        }
        TryBindToLocalPlayer();
        CancelInvoke(nameof(TryBindToLocalPlayer));
        InvokeRepeating(nameof(TryBindToLocalPlayer), 0.5f, 0.5f);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(TryBindToLocalPlayer));
        Unsubscribe();
    }

    private void Update()
    {
        if (overlayImage == null || targetPlayer == null) return;
        // Respaldo por polling: refleja el estado aunque se pierda un evento.
        if (targetPlayer.FreezeOverlayActive != targetVisible)
            SetVisible(targetPlayer.FreezeOverlayActive);

        float targetAlpha = targetVisible ? maxAlpha : 0f;
        if (Mathf.Approximately(currentAlpha, targetAlpha)) return;

        if (fadeDuration <= 0f || maxAlpha <= 0f)
        {
            SetAlpha(targetAlpha);
            return;
        }
        float speed = maxAlpha / fadeDuration;
        SetAlpha(Mathf.MoveTowards(currentAlpha, targetAlpha, speed * Time.deltaTime));
    }

    private void TryBindToLocalPlayer()
    {
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Unsubscribe();
            targetPlayer = null;
        }
        if (targetPlayer != null && isSubscribed)
            return;

        if (targetPlayer == null)
            targetPlayer = FindLocalPlayer();
        if (targetPlayer == null) return;

        Subscribe();
        // Estado inicial: si el jugador ya está congelado al enlazar, se muestra sin fade.
        SetVisible(targetPlayer.FreezeOverlayActive);
        SetAlpha(targetVisible ? maxAlpha : 0f);
    }

    private void Subscribe()
    {
        if (targetPlayer == null || isSubscribed) return;
        targetPlayer.FreezeOverlayChanged += OnFreezeOverlayChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (targetPlayer != null && isSubscribed)
            targetPlayer.FreezeOverlayChanged -= OnFreezeOverlayChanged;
        isSubscribed = false;
    }

    private void OnFreezeOverlayChanged(bool active)
    {
        SetVisible(active);
    }

    private void SetVisible(bool visible)
    {
        targetVisible = visible;
        if (visible && overlayImage != null)
            overlayImage.enabled = true;
    }

    private void SetAlpha(float alpha)
    {
        currentAlpha = alpha;
        if (overlayImage == null) return;
        Color color = overlayImage.color;
        color.a = alpha;
        overlayImage.color = color;
        if (alpha <= 0.001f)
            overlayImage.enabled = false;
    }

    private static bool IsValidSceneReference(PlayerMovementManager pm)
    {
        if (pm == null) return false;
        // El dummy del tutorial nunca es el jugador local del HUD.
        if (TutorialManager.IsTutorialDummyPlayer(pm)) return false;
        GameObject go = pm.gameObject;
        if (go == null) return false;
        var scene = go.scene;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        // Solo vale el jugador de la escena activa (evita restos de otra escena).
        if (!string.Equals(scene.name, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return false;
        return true;
    }

    private static PlayerMovementManager FindLocalPlayer()
    {
        PlayerMovementManager first = null;
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (pm == null) continue;
            // El dummy del tutorial no es el jugador local.
            if (TutorialManager.IsTutorialDummyPlayer(pm)) continue;
            if (!pm.gameObject.scene.IsValid() || !pm.gameObject.scene.isLoaded) continue;
            if (!string.Equals(pm.gameObject.scene.name, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) continue;
            if (first == null) first = pm;
            if (pm.IsOwner)
                return pm;
        }
        // Sin red (tutorial offline o escena abierta directo): aceptar el jugador de la escena.
        if (first != null && (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening))
            return first;
        if (TutorialManager.IsTutorial && first != null)
            return first;
        return null;
    }
}
