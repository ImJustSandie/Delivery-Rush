using UnityEngine;

public class CharacterCustomizationPreview : MonoBehaviour
{
    [Header("Skin Database (Opcional para Carga Automática)")]
    [Tooltip("Base de datos de skins para cargar automáticamente las preferencias guardadas en PlayerPrefs.")]
    [SerializeField] private CharacterSkinDatabase skinDatabase;

    [Header("Renderers / Mesh Components")]
    [Tooltip("Renderer responsable del sombrero/cabeza (Hat).")]
    [SerializeField] private Renderer hatRenderer;

    [Tooltip("Renderer responsable del cuerpo (Body).")]
    [SerializeField] private Renderer bodyRenderer;

    [Tooltip("Renderer responsable de la mochila (Bag).")]
    [SerializeField] private Renderer bagRenderer;

    [Tooltip("Renderer responsable de la camisa (Shirt).")]
    [SerializeField] private Renderer shirtRenderer;

    [Tooltip("Renderer responsable de la piel/cuerpo base (Skin).")]
    [SerializeField] private Renderer skinRenderer;

    [Header("UI / Name Display (Opcional)")]
    [Tooltip("Texto TMP sobre el dummy de preview para mostrar el nombre en tiempo real.")]
    [SerializeField] private TMPro.TMP_Text nameTextLabel;

    [Header("Sub-material Indices (Opcional)")]
    [Tooltip("Índice de material dentro del Renderer si el objeto usa múltiples sub-materiales.")]
    [SerializeField] private int hatMaterialIndex = 0;
    [SerializeField] private int bodyMaterialIndex = 0;
    [SerializeField] private int bagMaterialIndex = 0;
    [SerializeField] private int shirtMaterialIndex = 0;
    [SerializeField] private int skinMaterialIndex = 0;

    [Tooltip("Si es verdadero, cargará los skins guardados en PlayerPrefs en Start y OnEnable (solo para previews fuera de red).")]
    [SerializeField] private bool autoLoadFromPlayerPrefs = true;

    private void Start()
    {
        TryAutoLoadPlayerPrefs();
    }

    private void OnEnable()
    {
        TryAutoLoadPlayerPrefs();
    }

    private void TryAutoLoadPlayerPrefs()
    {
        // No cargar automátiamente PlayerPrefs en objetos que tienen NetworkPlayerSkinSynchronizer,
        // ya que la red (NGO) se encarga de determinar si es el owner o remoto.
        // Excepción: tutorial offline (sin red), donde el synchronizer nunca recibe OnNetworkSpawn
        // y por tanto hay que cargar local igual.
        bool isOfflineTutorial = TutorialManager.IsTutorial
            && (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening);
        if (autoLoadFromPlayerPrefs && (GetComponent<NetworkPlayerSkinSynchronizer>() == null || isOfflineTutorial))
        {
            LoadSavedSkinsFromPlayerPrefs();
        }
    }

    /// <summary>
    /// Lee los índices guardados en PlayerPrefs y aplica los materiales correspondientes de la base de datos.
    /// </summary>
    public void LoadSavedSkinsFromPlayerPrefs()
    {
        if (skinDatabase != null)
        {
            ApplyHatMaterial(skinDatabase.GetHatMaterial(PlayerCustomizationData.HatIndex));
            ApplyBodyMaterial(skinDatabase.GetBodyMaterial(PlayerCustomizationData.BodyIndex));
            ApplyBagMaterial(skinDatabase.GetBagMaterial(PlayerCustomizationData.BagIndex));
            ApplyShirtMaterial(skinDatabase.GetShirtMaterial(PlayerCustomizationData.ShirtIndex));
            ApplySkinMaterial(skinDatabase.GetSkinMaterial(PlayerCustomizationData.SkinIndex));
        }
        ApplyPlayerName(PlayerCustomizationData.PlayerName);
    }

    /// <summary>
    /// Actualiza la etiqueta con el nombre sobre el dummy en tiempo real.
    /// </summary>
    public void ApplyPlayerName(string newName)
    {
        if (nameTextLabel != null)
        {
            nameTextLabel.text = string.IsNullOrWhiteSpace(newName) ? "Repartidor" : newName;
        }

        LobbyPlayerDisplay display = GetComponent<LobbyPlayerDisplay>();
        if (display != null)
        {
            display.UpdatePlayerLabel();
        }
    }

    public void ApplyHatMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(hatRenderer, hatMaterialIndex, newMat);
    }

    public void ApplyBodyMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(bodyRenderer, bodyMaterialIndex, newMat);
    }

    public void ApplyBagMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(bagRenderer, bagMaterialIndex, newMat);
    }

    public void ApplyShirtMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(shirtRenderer, shirtMaterialIndex, newMat);
    }

    public void ApplySkinMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(skinRenderer, skinMaterialIndex, newMat);
    }

    /// <summary>
    /// Copia los materiales visibles desde otro preview (ej. el dummy del tutorial
    /// hereda solo lo visual del jugador local). No copia índices ni PlayerPrefs.
    /// </summary>
    public void CopyVisualsFrom(CharacterCustomizationPreview source)
    {
        if (source == null) return;
        CopyRendererMaterials(source.hatRenderer, hatRenderer);
        CopyRendererMaterials(source.bodyRenderer, bodyRenderer);
        CopyRendererMaterials(source.bagRenderer, bagRenderer);
        CopyRendererMaterials(source.shirtRenderer, shirtRenderer);
        CopyRendererMaterials(source.skinRenderer, skinRenderer);
    }

    private static void CopyRendererMaterials(Renderer from, Renderer to)
    {
        if (from == null || to == null) return;
        try { to.sharedMaterials = from.sharedMaterials; }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CharacterCustomizationPreview] No se pudieron copiar materiales: {e.Message}");
        }
    }

    private void ApplyMaterialToRenderer(Renderer rend, int index, Material newMat)
    {
        if (rend == null || newMat == null) return;

        Material[] materials = rend.materials;
        if (index >= 0 && index < materials.Length)
        {
            materials[index] = newMat;
            rend.materials = materials;
        }
    }
}
