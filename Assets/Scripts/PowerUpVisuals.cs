using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Visuales y sonidos de power-ups en el jugador (componente del prefab Player).
/// Tiene 2 items visuales a asignar en el Inspector:
/// 1. boostModel: modelo alternativo (hijo desactivado) que reemplaza al normal
///    mientras la velocidad está aumentada.
/// 2. slowMaterial: material que reemplaza temporalmente todas las texturas
///    mientras el jugador está ralentizado.
/// Más 2 sonidos de activación (ver "Audio"): hielo global y moto solo-caster.
/// Lo maneja <see cref="PlayerMovementManager"/> según el efecto activo en red,
/// así todos los clientes ven el cambio. Al terminar la duración todo se restaura solo.
/// </summary>
public class PowerUpVisuals : NetworkBehaviour
{
    [Header("Boost: cambio de modelo")]
    [Tooltip("ITEM 1: modelo alternativo (GameObject hijo, debe iniciar desactivado) visible solo durante el aumento de velocidad.")]
    [SerializeField] private GameObject boostModel;

    [Header("Slow: cambio de textura")]
    [Tooltip("ITEM 2: material que reemplaza temporalmente todas las texturas durante la ralentización.")]
    [SerializeField] private Material slowMaterial;

    [Header("Alineación")]
    [Tooltip("Si es true, el modelo boost hereda el transform del jugador (misma posición, rotación y tamaño) al activarse.")]
    [SerializeField] private bool matchPlayerTransform = true;
    [Tooltip("Corrección de giro en grados si el modelo boost mira a otro eje que el jugador (ej: 90 si avanza de lado, 180 si va de espaldas).")]
    [SerializeField] private float boostYawOffset;
    [Tooltip("Desplazamiento vertical (offset en Y) para la posición local del modelo boost.")]
    [SerializeField] private float boostYOffset;

    [Header("Audio")]
    [Tooltip("Sonido al activar congelar (SlowOthers). Lo escuchan todos los clientes.")]
    [SerializeField] private AudioClip slowPowerUpSound;
    [Tooltip("Volumen del sonido de congelar (0 a 1).")]
    [Range(0f, 1f)]
    [SerializeField] private float slowPowerUpSoundVolume = 1f;
    [Tooltip("Sonido al activar moto/velocidad (BoostSelf). Solo lo escucha quien lo usa.")]
    [SerializeField] private AudioClip boostPowerUpSound;
    [Tooltip("Volumen del sonido de moto (0 a 1).")]
    [Range(0f, 1f)]
    [SerializeField] private float boostPowerUpSoundVolume = 1f;

    private Renderer[] normalRenderers = System.Array.Empty<Renderer>();
    private Renderer[] boostRenderers = System.Array.Empty<Renderer>();
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private bool boostActive;
    private bool slowActive;
    private bool warnedMissing;

    private void Awake()
    {
        CacheRenderers();
    }

    private void CacheRenderers()
    {
        if (boostModel != null && !boostModel.scene.IsValid())
        {
            // Asignaron el asset del prefab (Project) en vez de un hijo del Player:
            // activarlo no mostraría nada, así que se instancia bajo el jugador.
            boostModel = Instantiate(boostModel, transform);
            boostModel.name = boostModel.name.Replace("(Clone)", "_BoostModel");
            Debug.Log($"[PowerUpVisuals] boostModel era un asset: instanciado como hijo de {name}.");
        }

        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        HashSet<Renderer> boostSet = null;
        if (boostModel != null)
        {
            boostSet = new HashSet<Renderer>(boostModel.GetComponentsInChildren<Renderer>(true));
            if (boostModel.activeSelf)
                boostModel.SetActive(false);
        }

        List<Renderer> normal = new List<Renderer>(all.Length);
        foreach (Renderer r in all)
        {
            if (r == null) continue;
            if (boostSet != null && boostSet.Contains(r)) continue;
            normal.Add(r);
        }
        normalRenderers = normal.ToArray();
        boostRenderers = boostSet != null ? new List<Renderer>(boostSet).ToArray() : System.Array.Empty<Renderer>();
        AlignBoostToPlayer();
        Debug.Log($"[PowerUpVisuals] {name}: {normalRenderers.Length} renderers normales, {boostRenderers.Length} en boostModel ({(boostModel != null ? boostModel.name : "SIN ASIGNAR")}).");
    }

    /// <summary>Muestra el modelo alternativo (true) o restaura el normal (false).</summary>
    public void SetBoostActive(bool active)
    {
        if (boostModel == null)
        {
            WarnMissing("boostModel: asigna el modelo alternativo en el prefab Player.");
            return;
        }
        if (boostActive == active) return;
        boostActive = active;

        if (active)
            AlignBoostToPlayer();
        boostModel.SetActive(active);
        foreach (Renderer r in normalRenderers)
        {
            if (r != null) r.enabled = !active;
        }
    }

    /// <summary>Aplica la textura de ralentizado (true) o restaura las originales (false).</summary>
    public void SetSlowActive(bool active)
    {
        if (active == slowActive) return;
        if (active && slowMaterial == null)
        {
            WarnMissing("slowMaterial: asigna el material de ralentizado en el prefab Player.");
            return;
        }
        slowActive = active;
        if (active)
            ApplySlowOverlay();
        else
            ClearSlowOverlay();
    }

    /// <summary>
    /// Restablece inmediatamente todos los visuales al estado normal (oculta el modelo de boost y restaura texturas).
    /// </summary>
    public void ResetVisuals()
    {
        boostActive = false;
        if (boostModel != null && boostModel.activeSelf)
        {
            boostModel.SetActive(false);
        }
        foreach (Renderer r in normalRenderers)
        {
            if (r != null) r.enabled = true;
        }

        slowActive = false;
        ClearSlowOverlay();
    }

    // ── Audio de activación (eventos, no estado visual) ──────────────────────

    /// <summary>Emite el sonido de congelar a todos. Solo la invoca el servidor al activar SlowOthers.</summary>
    public void PlaySlowSoundServerSide()
    {
        if (!IsServer) return;
        PlaySlowSoundClientRpc();
    }

    /// <summary>Emite el sonido de moto solo al cliente indicado. Solo la invoca el servidor al activar BoostSelf.</summary>
    public void PlayBoostSoundServerSide(ulong casterClientId)
    {
        if (!IsServer) return;
        PlayBoostSoundClientRpc(transform.position, RpcTarget.Single(casterClientId, RpcTargetUse.Temp));
    }

    /// <summary>Sonido de congelar en local (tutorial offline).</summary>
    public void PlaySlowSoundLocal()
    {
        GameAudioManager.PlayOneShot2D(slowPowerUpSound, slowPowerUpSoundVolume);
    }

    /// <summary>Sonido de moto en local (tutorial offline).</summary>
    public void PlayBoostSoundLocal(Vector3 boostPosition)
    {
        InteractableCube.PlayCollectSoundAt(boostPowerUpSound, boostPosition, boostPowerUpSoundVolume);
    }

    /// <summary>Sonido de congelar: lo escuchan todos los clientes (2D global).</summary>
    [Rpc(SendTo.Everyone)]
    private void PlaySlowSoundClientRpc()
    {
        GameAudioManager.PlayOneShot2D(slowPowerUpSound, slowPowerUpSoundVolume);
    }

    /// <summary>Sonido de moto: solo lo escucha el cliente indicado.</summary>
    [Rpc(SendTo.SpecifiedInParams)]
    private void PlayBoostSoundClientRpc(Vector3 boostPosition, RpcParams rpcParams = default)
    {
        InteractableCube.PlayCollectSoundAt(boostPowerUpSound, boostPosition, boostPowerUpSoundVolume);
    }

    private void ApplySlowOverlay()
    {
        originalMaterials.Clear();
        foreach (Renderer r in AllEffectRenderers())
        {
            if (r == null) continue;
            Material[] current = r.sharedMaterials;
            originalMaterials[r] = (Material[])current.Clone();
            Material[] overlay = new Material[current.Length];
            for (int i = 0; i < overlay.Length; i++)
                overlay[i] = slowMaterial;
            r.sharedMaterials = overlay;
        }
    }

    private void ClearSlowOverlay()
    {
        foreach (KeyValuePair<Renderer, Material[]> kvp in originalMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.sharedMaterials = kvp.Value;
        }
        originalMaterials.Clear();
    }

    private IEnumerable<Renderer> AllEffectRenderers()
    {
        foreach (Renderer r in normalRenderers) yield return r;
        foreach (Renderer r in boostRenderers) yield return r;
    }

    /// <summary>
    /// Pone el modelo boost con la misma posición, rotación y tamaño del jugador
    /// (local cero/identidad/uno al ser hijo), más una corrección de giro por si el
    /// modelo está construido mirando a otro eje. Así no conserva su orientación propia.
    /// </summary>
    private void AlignBoostToPlayer()
    {
        if (!matchPlayerTransform || boostModel == null) return;
        boostModel.transform.localPosition = new Vector3(0f, boostYOffset, 0f);
        boostModel.transform.localRotation = Quaternion.Euler(0f, boostYawOffset, 0f);
        boostModel.transform.localScale = Vector3.one;
    }

    private void WarnMissing(string message)
    {
        if (warnedMissing) return;
        warnedMissing = true;
        Debug.LogWarning($"[PowerUpVisuals] {message}");
    }
}
