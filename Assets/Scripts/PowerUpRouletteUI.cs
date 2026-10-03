using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD de inventario de Power-Ups con animaciones de ruleta al recoger
/// y transición de la ranura secundaria a la primaria al usar un poder (estilo Mario Kart).
/// Se auto-vincula al jugador local (IsOwner o tutorial offline).
/// </summary>
public class PowerUpRouletteUI : MonoBehaviour
{
    [Header("Ranura 1: Círculo Principal (Poder Activo)")]
    [Tooltip("RectTransform del círculo grande principal.")]
    [SerializeField] private RectTransform primaryCircleRect;
    [Tooltip("Image que muestra el ícono del poder primario dentro del círculo grande.")]
    [SerializeField] private Image primaryIconImage;
    [Tooltip("Botón opcional para activar el poder primario al hacer clic en el círculo.")]
    [SerializeField] private Button primaryUseButton;
    [Tooltip("CanvasGroup opcional del círculo principal.")]
    [SerializeField] private CanvasGroup primaryCanvasGroup;

    [Header("Ranura 2: Círculo Secundario (Poder Reserva)")]
    [Tooltip("RectTransform del círculo pequeño secundario (ubicado arriba a la izquierda).")]
    [SerializeField] private RectTransform secondaryCircleRect;
    [Tooltip("Image que muestra el ícono del poder secundario dentro del círculo pequeño.")]
    [SerializeField] private Image secondaryIconImage;
    [Tooltip("CanvasGroup opcional del círculo secundario.")]
    [SerializeField] private CanvasGroup secondaryCanvasGroup;

    [Header("Sprites de Power-Ups")]
    [Tooltip("Ícono para el poder de velocidad (BoostSelf).")]
    [SerializeField] private Sprite boostIcon;
    [Tooltip("Ícono para el poder de ralentizar (SlowOthers).")]
    [SerializeField] private Sprite slowIcon;
    [Tooltip("Sprite opcional cuando la ranura está vacía (si es null se oculta la imagen).")]
    [SerializeField] private Sprite emptySlotSprite;

    [Header("Configuración de Ruleta")]
    [Tooltip("Duración en segundos de la animación de ruleta al recoger un poder.")]
    [SerializeField] private float rouletteDuration = 1.2f;
    [Tooltip("Intervalo entre cambios de ícono durante la ruleta.")]
    [SerializeField] private float rouletteInterval = 0.06f;
    [Tooltip("Multiplicador de escala al finalizar la ruleta (efecto pop/bounce).")]
    [SerializeField] private float popScaleAmount = 1.25f;
    [Tooltip("Duración del efecto pop/bounce al detener la ruleta.")]
    [SerializeField] private float popScaleDuration = 0.18f;

    [Header("Configuración de Transición / Promoción")]
    [Tooltip("Duración de la animación cuando el poder secundario se mueve a la ranura primaria.")]
    [SerializeField] private float shiftDuration = 0.35f;

    [Header("Jugador Local")]
    [Tooltip("Referencia opcional al Player local. Si se deja vacía se detecta automáticamente.")]
    [SerializeField] private PlayerMovementManager targetPlayer;

    private bool isSubscribed;
    private bool isSlot0Spinning;
    private bool isSlot1Spinning;
    private bool isPromoting;

    private Coroutine slot0Routine;
    private Coroutine slot1Routine;
    private Coroutine promoteRoutine;

    private Vector3 primaryOriginalScale = Vector3.one;
    private Vector3 secondaryOriginalScale = Vector3.one;

    private void Awake()
    {
        AutoFindReferences();

        if (primaryCircleRect != null)
            primaryOriginalScale = primaryCircleRect.localScale;
        if (secondaryCircleRect != null)
            secondaryOriginalScale = secondaryCircleRect.localScale;
    }

    private void AutoFindReferences()
    {
        if (primaryUseButton == null)
            primaryUseButton = GetComponentInChildren<Button>(true);

        if (primaryIconImage == null && primaryCircleRect != null)
            primaryIconImage = primaryCircleRect.GetComponentInChildren<Image>(true);

        if (secondaryIconImage == null && secondaryCircleRect != null)
            secondaryIconImage = secondaryCircleRect.GetComponentInChildren<Image>(true);
    }

    private void OnEnable()
    {
        if (primaryUseButton != null)
            primaryUseButton.onClick.AddListener(OnPrimaryButtonPressed);

        TryBindToLocalPlayer();
        CancelInvoke(nameof(TryBindToLocalPlayer));
        InvokeRepeating(nameof(TryBindToLocalPlayer), 0.5f, 0.5f);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(TryBindToLocalPlayer));
        if (primaryUseButton != null)
            primaryUseButton.onClick.RemoveListener(OnPrimaryButtonPressed);

        Unsubscribe();
    }

    private void Update()
    {
        if (targetPlayer == null || !IsValidSceneReference(targetPlayer)) return;

        // Refresco de respaldo si no hay animaciones activas
        if (!isSlot0Spinning && !isSlot1Spinning && !isPromoting)
        {
            RefreshUIInstant();
        }
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
            if (!isSlot0Spinning && !isSlot1Spinning && !isPromoting)
                RefreshUIInstant();
            return;
        }

        if (targetPlayer == null)
            targetPlayer = FindLocalPlayer();

        if (targetPlayer == null) return;

        Subscribe();
        RefreshUIInstant();
    }

    private static bool IsValidSceneReference(PlayerMovementManager pm)
    {
        if (pm == null) return false;
        if (TutorialManager.IsTutorialDummyPlayer(pm)) return false;
        GameObject go = pm.gameObject;
        if (go == null) return false;
        var scene = go.scene;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        if (!string.Equals(scene.name, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return false;
        return true;
    }

    private static PlayerMovementManager FindLocalPlayer()
    {
        PlayerMovementManager first = null;
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (pm == null) continue;
            if (TutorialManager.IsTutorialDummyPlayer(pm)) continue;
            if (!pm.gameObject.scene.IsValid() || !pm.gameObject.scene.isLoaded) continue;
            if (!string.Equals(pm.gameObject.scene.name, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) continue;

            if (first == null) first = pm;
            if (pm.IsOwner) return pm;
        }

        if (first != null && (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening))
            return first;
        if (TutorialManager.IsTutorial && first != null)
            return first;

        return null;
    }

    private void Subscribe()
    {
        if (targetPlayer == null || isSubscribed) return;

        targetPlayer.PowerUpAdded += OnPowerUpAdded;
        targetPlayer.PowerUpUsed += OnPowerUpUsed;
        targetPlayer.PowerUpPromoted += OnPowerUpPromoted;
        targetPlayer.PowerUpCleared += OnPowerUpCleared;
        targetPlayer.PowerUpCountChanged += OnPowerUpCountChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (targetPlayer != null && isSubscribed)
        {
            targetPlayer.PowerUpAdded -= OnPowerUpAdded;
            targetPlayer.PowerUpUsed -= OnPowerUpUsed;
            targetPlayer.PowerUpPromoted -= OnPowerUpPromoted;
            targetPlayer.PowerUpCleared -= OnPowerUpCleared;
            targetPlayer.PowerUpCountChanged -= OnPowerUpCountChanged;
        }
        isSubscribed = false;
    }

    private void OnPowerUpAdded(int slotIndex, PowerUpEffect effect)
    {
        if (slotIndex == 0)
        {
            if (slot0Routine != null) StopCoroutine(slot0Routine);
            slot0Routine = StartCoroutine(PlayRouletteRoutine(0, effect));
        }
        else if (slotIndex == 1)
        {
            if (slot1Routine != null) StopCoroutine(slot1Routine);
            slot1Routine = StartCoroutine(PlayRouletteRoutine(1, effect));
        }
    }

    private void OnPowerUpUsed(int slotIndex)
    {
        // Feedback inmediato al usar
        if (slotIndex == 0 && primaryCircleRect != null)
        {
            StartCoroutine(PlayShrinkEffect(primaryCircleRect, primaryOriginalScale));
        }
    }

    private void OnPowerUpPromoted()
    {
        if (promoteRoutine != null) StopCoroutine(promoteRoutine);
        promoteRoutine = StartCoroutine(PlayPromotionRoutine());
    }

    private void OnPowerUpCleared()
    {
        StopAllCoroutines();
        isSlot0Spinning = false;
        isSlot1Spinning = false;
        isPromoting = false;
        if (targetPlayer != null) targetPlayer.IsPowerUpLocked = false;
        RefreshUIInstant();
    }

    private void OnPowerUpCountChanged(int newCount)
    {
        if (!isSlot0Spinning && !isSlot1Spinning && !isPromoting)
        {
            RefreshUIInstant();
        }
    }

    private void OnPrimaryButtonPressed()
    {
        if (targetPlayer == null) return;
        if (isSlot0Spinning || isPromoting || targetPlayer.IsPowerUpLocked) return;
        if (targetPlayer.HasPowerUp)
        {
            targetPlayer.TryUsePowerUp(PowerUpEffect.Random);
        }
    }

    private IEnumerator PlayRouletteRoutine(int slotIndex, PowerUpEffect finalEffect)
    {
        Image targetImage = slotIndex == 0 ? primaryIconImage : secondaryIconImage;
        RectTransform targetRect = slotIndex == 0 ? primaryCircleRect : secondaryCircleRect;
        Vector3 baseScale = slotIndex == 0 ? primaryOriginalScale : secondaryOriginalScale;

        if (slotIndex == 0)
        {
            isSlot0Spinning = true;
            if (targetPlayer != null) targetPlayer.IsPowerUpLocked = true;
        }
        else
        {
            isSlot1Spinning = true;
        }
        RefreshUIInstant();

        if (targetImage != null)
        {
            targetImage.enabled = true;
            targetImage.color = Color.white;
        }

        List<Sprite> availableSprites = GetAvailableSprites();
        float elapsed = 0f;
        int spriteIndex = 0;

        // Giro de ruleta
        while (elapsed < rouletteDuration)
        {
            if (targetImage != null && availableSprites.Count > 0)
            {
                targetImage.sprite = availableSprites[spriteIndex % availableSprites.Count];
                spriteIndex++;
            }
            yield return new WaitForSeconds(rouletteInterval);
            elapsed += rouletteInterval;
        }

        // Fijar resultado final
        if (targetImage != null)
        {
            targetImage.sprite = GetSpriteForEffect(finalEffect);
        }

        // Animación Pop/Bounce de cierre
        if (targetRect != null)
        {
            float popTime = 0f;
            while (popTime < popScaleDuration)
            {
                popTime += Time.deltaTime;
                float progress = popTime / popScaleDuration;
                float scaleFactor = Mathf.Sin(progress * Mathf.PI);
                targetRect.localScale = Vector3.Lerp(baseScale, baseScale * popScaleAmount, scaleFactor);
                yield return null;
            }
            targetRect.localScale = baseScale;
        }

        if (slotIndex == 0)
        {
            isSlot0Spinning = false;
            if (targetPlayer != null) targetPlayer.IsPowerUpLocked = false;
        }
        else
        {
            isSlot1Spinning = false;
        }
        RefreshUIInstant();
    }

    private IEnumerator PlayPromotionRoutine()
    {
        isPromoting = true;
        if (targetPlayer != null) targetPlayer.IsPowerUpLocked = true;
        RefreshUIInstant();

        // Si hay un ícono en la ranura secundaria, animar su transición al ícono principal
        PowerUpEffect? newPrimaryEffect = targetPlayer != null ? targetPlayer.GetPowerUpAt(0) : null;
        PowerUpEffect? newSecondaryEffect = targetPlayer != null ? targetPlayer.GetPowerUpAt(1) : null;

        if (secondaryIconImage != null && primaryIconImage != null && secondaryCircleRect != null && primaryCircleRect != null)
        {
            float elapsed = 0f;
            Vector3 startPos = secondaryCircleRect.position;
            Vector3 endPos = primaryCircleRect.position;
            Vector3 startScale = secondaryCircleRect.localScale;
            Vector3 endScale = primaryOriginalScale;

            // Creamos un duplicado temporal visual si es necesario, o desplazamos secondaryIconImage suavemente
            Sprite secondarySprite = secondaryIconImage.sprite;
            secondaryIconImage.enabled = false;

            // Mostrar temporalmente en el ícono principal el sprite que viene del secundario
            primaryIconImage.sprite = secondarySprite;
            primaryIconImage.enabled = secondarySprite != null;

            if (primaryCircleRect != null)
            {
                while (elapsed < shiftDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, elapsed / shiftDuration);
                    primaryCircleRect.localScale = Vector3.Lerp(primaryOriginalScale * 0.7f, primaryOriginalScale, t);
                    yield return null;
                }
                primaryCircleRect.localScale = primaryOriginalScale;
            }
        }

        isPromoting = false;
        if (targetPlayer != null) targetPlayer.IsPowerUpLocked = false;
        RefreshUIInstant();
    }

    private IEnumerator PlayShrinkEffect(RectTransform rect, Vector3 originalScale)
    {
        float duration = 0.15f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            rect.localScale = Vector3.Lerp(originalScale, originalScale * 0.7f, t);
            yield return null;
        }
        rect.localScale = originalScale;
    }

    private void RefreshUIInstant()
    {
        if (targetPlayer == null)
        {
            SetSlotState(primaryIconImage, primaryCanvasGroup, null);
            SetSlotState(secondaryIconImage, secondaryCanvasGroup, null);
            if (primaryUseButton != null) primaryUseButton.interactable = false;
            return;
        }

        PowerUpEffect? slot0 = targetPlayer.GetPowerUpAt(0);
        PowerUpEffect? slot1 = targetPlayer.GetPowerUpAt(1);

        if (!isSlot0Spinning && !isPromoting)
        {
            SetSlotState(primaryIconImage, primaryCanvasGroup, slot0);
        }

        if (!isSlot1Spinning && !isPromoting)
        {
            SetSlotState(secondaryIconImage, secondaryCanvasGroup, slot1);
        }

        if (primaryUseButton != null)
        {
            bool isLocked = isSlot0Spinning || isPromoting || (targetPlayer != null && targetPlayer.IsPowerUpLocked);
            primaryUseButton.interactable = targetPlayer.HasPowerUp && !isLocked;
        }
    }

    private void SetSlotState(Image iconImage, CanvasGroup canvasGroup, PowerUpEffect? effect)
    {
        if (iconImage == null) return;

        if (effect.HasValue)
        {
            iconImage.enabled = true;
            iconImage.sprite = GetSpriteForEffect(effect.Value);
            iconImage.color = Color.white;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
        else
        {
            if (emptySlotSprite != null)
            {
                iconImage.enabled = true;
                iconImage.sprite = emptySlotSprite;
                iconImage.color = new Color(1f, 1f, 1f, 0.4f);
            }
            else
            {
                iconImage.enabled = false;
            }
            if (canvasGroup != null) canvasGroup.alpha = 0.5f;
        }
    }

    private List<Sprite> GetAvailableSprites()
    {
        List<Sprite> list = new List<Sprite>();
        if (boostIcon != null) list.Add(boostIcon);
        if (slowIcon != null) list.Add(slowIcon);
        return list;
    }

    private Sprite GetSpriteForEffect(PowerUpEffect effect)
    {
        return effect switch
        {
            PowerUpEffect.BoostSelf => boostIcon != null ? boostIcon : emptySlotSprite,
            PowerUpEffect.SlowOthers => slowIcon != null ? slowIcon : emptySlotSprite,
            _ => emptySlotSprite
        };
    }
}
