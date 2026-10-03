using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD del power-up: muestra qué power-ups lleva el jugador local (ralentizar y
/// velocidad, sorteados al recoger) y gasta el inventario al pulsar el botón.
/// Pon este script en un GameObject del Canvas de MainScene, asigna un
/// Button (el que usa el jugador para castear) y opcionalmente textos.
/// Sin textos por tipo, el texto principal muestra "Ralentizar: X | Velocidad: Y".
/// Para botones dedicados, crea 2 con este script y elige un
/// <see cref="PowerUpEffect"/> distinto en cada uno.
/// Se auto-vincula al Player local (IsOwner).
/// </summary>
public class PowerUpUI : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Botón de la UI para usar el power-up. Si se deja vacío lo busca en este GameObject.")]
    [SerializeField] private Button useButton;
    [Tooltip("Texto opcional con el contador. Si se deja vacío lo busca en hijos.")]
    [SerializeField] private TMP_Text countText;
    [Tooltip("Referencia opcional al Player local. Si se deja vacío se detecta automáticamente (IsOwner).")]
    [SerializeField] private PlayerMovementManager targetPlayer;

    [Header("Efecto")]
    [Tooltip("Qué gasta este botón. La cola es FIFO: con tipo fijo solo sale si el frente es de ese tipo; con Random sale el frente sea cual sea.")]
    [SerializeField] private PowerUpEffect effect = PowerUpEffect.Random;

    [Header("Formato")]
    [SerializeField] private string format = "Power: {0}/{1}";
    [SerializeField] private string emptySuffix = " (vacío)";
    [Tooltip("Texto opcional con los power-ups de ralentizar. Si se deja vacío y hay countText, el desglose se muestra en countText.")]
    [SerializeField] private TMP_Text slowText;
    [Tooltip("Texto opcional con los power-ups de velocidad. Si se deja vacío y hay countText, el desglose se muestra en countText.")]
    [SerializeField] private TMP_Text boostText;
    [Tooltip("Formato del texto de ralentizar. {0}=cantidad.")]
    [SerializeField] private string slowFormat = "Ralentizar: {0}";
    [Tooltip("Formato del texto de velocidad. {0}=cantidad.")]
    [SerializeField] private string boostFormat = "Velocidad: {0}";
    [Tooltip("Formato del desglose cuando no hay textos por tipo. {0}=ralentizar, {1}=velocidad.")]
    [SerializeField] private string breakdownFormat = "Ralentizar: {0} | Velocidad: {1}";

    private bool isSubscribed;
    private int lastShownPowerUps = int.MinValue;

    private void Awake()
    {
        if (useButton == null)
            useButton = GetComponentInChildren<Button>(true);
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);

        if (useButton == null)
            Debug.LogWarning($"[PowerUpUI] No se encontró Button en {name}. Asigna useButton en el Inspector (botón de la UI para usar el power-up).");

        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Debug.LogWarning($"[PowerUpUI] targetPlayer en {name} apunta a un prefab/asset, se descartará y se buscará el Player local.");
            targetPlayer = null;
        }
    }

    private void OnEnable()
    {
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Unsubscribe();
            targetPlayer = null;
        }
        if (useButton != null)
            useButton.onClick.AddListener(OnUseButtonPressed);

        TryBindToLocalPlayer();
        // Reintento persistente (igual que CollectibleCounterUI): valida el vínculo cada tick.
        CancelInvoke(nameof(TryBindToLocalPlayer));
        InvokeRepeating(nameof(TryBindToLocalPlayer), 0.5f, 0.5f);
    }

    private void Update()
    {
        // Respaldo por polling: refleja el inventario aunque se pierda un evento.
        if (targetPlayer == null || !IsValidSceneReference(targetPlayer)) return;
        if (targetPlayer.PowerUpCount != lastShownPowerUps)
            RefreshUI();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(TryBindToLocalPlayer));
        if (useButton != null)
            useButton.onClick.RemoveListener(OnUseButtonPressed);
        Unsubscribe();
    }

    private void TryBindToLocalPlayer()
    {
        if (targetPlayer != null && !IsValidSceneReference(targetPlayer))
        {
            Unsubscribe();
            targetPlayer = null;
        }
        if (targetPlayer != null && isSubscribed)
        {
            RefreshUI();
            return;
        }

        if (targetPlayer == null)
            targetPlayer = FindLocalPlayer();
        if (targetPlayer == null) return;

        Subscribe();
        RefreshUI();
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

    private void Subscribe()
    {
        if (targetPlayer == null || isSubscribed) return;
        targetPlayer.PowerUpCountChanged += OnPowerUpChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (targetPlayer != null && isSubscribed)
            targetPlayer.PowerUpCountChanged -= OnPowerUpChanged;
        isSubscribed = false;
    }

    private void OnPowerUpChanged(int newCount)
    {
        RefreshUI();
    }

    private void OnUseButtonPressed()
    {
        Debug.Log($"[PowerUpUI] Botón pulsado. Efecto={effect}, jugador={(targetPlayer != null ? targetPlayer.name : "NULL")}, ralentizar={(targetPlayer != null ? targetPlayer.SlowPowerUpCount : -1)}, velocidad={(targetPlayer != null ? targetPlayer.BoostPowerUpCount : -1)}, isOwner={(targetPlayer != null ? targetPlayer.IsOwner : false)}.");
        if (targetPlayer == null)
        {
            Debug.LogWarning("[PowerUpUI] Sin jugador vinculado, no se puede usar el power-up. Revisa que haya un Player spawneado con IsOwner y que PowerUpUI esté en la escena activa.");
            return;
        }
        targetPlayer.TryUsePowerUp(effect);
    }

    private void RefreshUI()
    {
        if (targetPlayer == null)
        {
            Debug.Log("[PowerUpUI] RefreshUI sin jugador vinculado (aún no hay Player local spawneado).");
            return;
        }
        int slow = targetPlayer.SlowPowerUpCount;
        int boost = targetPlayer.BoostPowerUpCount;
        int total = slow + boost;
        int max = targetPlayer.MaxPowerUps;
        lastShownPowerUps = total;

        // Textos por tipo (dicen qué power-up tienes)
        if (slowText != null)
            slowText.text = string.Format(slowFormat, slow);
        if (boostText != null)
            boostText.text = string.Format(boostFormat, boost);

        if (countText != null)
        {
            // Sin textos por tipo: el texto principal muestra el desglose
            if (slowText == null && boostText == null)
            {
                countText.text = string.Format(breakdownFormat, slow, boost);
                if (total <= 0 && !string.IsNullOrEmpty(emptySuffix))
                    countText.text += emptySuffix;
            }
            else
            {
                countText.text = string.Format(format, total, max);
                if (total <= 0 && !string.IsNullOrEmpty(emptySuffix))
                    countText.text += emptySuffix;
            }
        }
        if (useButton != null)
        {
            // FIFO: el botón de tipo fijo solo se habilita si el frente es de ese tipo.
            PowerUpEffect? front = targetPlayer.FrontPowerUp;
            useButton.interactable = effect switch
            {
                PowerUpEffect.SlowOthers => front == PowerUpEffect.SlowOthers,
                PowerUpEffect.BoostSelf => front == PowerUpEffect.BoostSelf,
                _ => total > 0,
            };
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(format)) format = "Power: {0}/{1}";
        if (string.IsNullOrEmpty(slowFormat)) slowFormat = "Ralentizar: {0}";
        if (string.IsNullOrEmpty(boostFormat)) boostFormat = "Velocidad: {0}";
        if (string.IsNullOrEmpty(breakdownFormat)) breakdownFormat = "Ralentizar: {0} | Velocidad: {1}";
        if (useButton == null)
            useButton = GetComponentInChildren<Button>(true);
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);
        if (Application.isPlaying) RefreshUI();
    }
#endif
}
