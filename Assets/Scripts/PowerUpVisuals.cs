using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visuales temporales de power-ups en el jugador (componente del prefab Player).
/// Tiene 2 items a asignar en el Inspector:
/// 1. boostModel: modelo alternativo (hijo desactivado) que reemplaza al normal
///    mientras la velocidad está aumentada.
/// 2. slowMaterial: material que reemplaza temporalmente todas las texturas
///    mientras el jugador está ralentizado.
/// Lo maneja <see cref="PlayerMovementManager"/> según el efecto activo en red,
/// así todos los clientes ven el cambio. Al terminar la duración todo se restaura solo.
/// </summary>
public class PowerUpVisuals : MonoBehaviour
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
        boostModel.transform.localPosition = Vector3.zero;
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
