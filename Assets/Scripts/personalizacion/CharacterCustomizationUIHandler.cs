using UnityEngine;

public class CharacterCustomizationUIHandler : MonoBehaviour
{
    [Header("Database & Preview References")]
    [SerializeField] private CharacterSkinDatabase skinDatabase;
    [SerializeField] private CharacterCustomizationPreview preview;

    [Header("UI Controls")]
    [SerializeField] private TMPro.TMP_InputField nameInputField;

    [Header("Scene Navigation")]
    [SerializeField] private string connectionSceneName = "ConnectionScene";

    private int currentHatIndex;
    private int currentBodyIndex;
    private int currentBagIndex;

    private void Start()
    {
        // Cargar datos previos
        currentHatIndex = PlayerCustomizationData.HatIndex;
        currentBodyIndex = PlayerCustomizationData.BodyIndex;
        currentBagIndex = PlayerCustomizationData.BagIndex;

        if (nameInputField != null)
        {
            nameInputField.text = PlayerCustomizationData.PlayerName;
            nameInputField.onValueChanged.AddListener(OnPlayerNameChanged);
        }

        UpdateAllPreviews();
    }

    private void OnDestroy()
    {
        if (nameInputField != null)
        {
            nameInputField.onValueChanged.RemoveListener(OnPlayerNameChanged);
        }
    }

    public void OnPlayerNameChanged(string newName)
    {
        PlayerCustomizationData.PlayerName = newName;
        if (preview != null)
        {
            preview.ApplyPlayerName(newName);
        }
        SyncWithNetwork();
    }

    // --- Hat Customization ---
    public void SelectNextHatSkin()
    {
        if (skinDatabase == null || skinDatabase.HatSkins.Count == 0) return;
        currentHatIndex = (currentHatIndex + 1) % skinDatabase.HatSkins.Count;
        SaveAndApplyHat();
    }

    public void SelectPreviousHatSkin()
    {
        if (skinDatabase == null || skinDatabase.HatSkins.Count == 0) return;
        currentHatIndex = (currentHatIndex - 1 + skinDatabase.HatSkins.Count) % skinDatabase.HatSkins.Count;
        SaveAndApplyHat();
    }

    /// <summary>
    /// Selecciona la skin de gorra directamente por índice de lista (ej: 0 para gorra1, 1 para gorra2, 2 para gorra3).
    /// Asignar a OnClick del botón en Unity Inspector.
    /// </summary>
    public void SetHatSkin(int index)
    {
        if (skinDatabase == null || skinDatabase.HatSkins.Count == 0) return;
        currentHatIndex = Mathf.Clamp(index, 0, skinDatabase.HatSkins.Count - 1);
        SaveAndApplyHat();
    }

    public void SelectHatSkin(int index) => SetHatSkin(index);

    private void SaveAndApplyHat()
    {
        PlayerCustomizationData.HatIndex = currentHatIndex;
        if (preview != null && skinDatabase != null)
        {
            preview.ApplyHatMaterial(skinDatabase.GetHatMaterial(currentHatIndex));
        }
        SyncWithNetwork();
    }

    // --- Body Customization ---
    public void SelectNextBodySkin()
    {
        if (skinDatabase == null || skinDatabase.BodySkins.Count == 0) return;
        currentBodyIndex = (currentBodyIndex + 1) % skinDatabase.BodySkins.Count;
        SaveAndApplyBody();
    }

    public void SelectPreviousBodySkin()
    {
        if (skinDatabase == null || skinDatabase.BodySkins.Count == 0) return;
        currentBodyIndex = (currentBodyIndex - 1 + skinDatabase.BodySkins.Count) % skinDatabase.BodySkins.Count;
        SaveAndApplyBody();
    }

    /// <summary>
    /// Selecciona la skin de chaleco directamente por índice de lista (ej: 0 para chaleco1, 1 para chaleco2, 2 para chaleco3).
    /// Asignar a OnClick del botón en Unity Inspector.
    /// </summary>
    public void SetBodySkin(int index)
    {
        if (skinDatabase == null || skinDatabase.BodySkins.Count == 0) return;
        currentBodyIndex = Mathf.Clamp(index, 0, skinDatabase.BodySkins.Count - 1);
        SaveAndApplyBody();
    }

    public void SelectBodySkin(int index) => SetBodySkin(index);

    private void SaveAndApplyBody()
    {
        PlayerCustomizationData.BodyIndex = currentBodyIndex;
        if (preview != null && skinDatabase != null)
        {
            preview.ApplyBodyMaterial(skinDatabase.GetBodyMaterial(currentBodyIndex));
        }
        SyncWithNetwork();
    }

    // --- Bag Customization ---
    public void SelectNextBagSkin()
    {
        if (skinDatabase == null || skinDatabase.BagSkins.Count == 0) return;
        currentBagIndex = (currentBagIndex + 1) % skinDatabase.BagSkins.Count;
        SaveAndApplyBag();
    }

    public void SelectPreviousBagSkin()
    {
        if (skinDatabase == null || skinDatabase.BagSkins.Count == 0) return;
        currentBagIndex = (currentBagIndex - 1 + skinDatabase.BagSkins.Count) % skinDatabase.BagSkins.Count;
        SaveAndApplyBag();
    }

    /// <summary>
    /// Selecciona la skin de maleta directamente por índice de lista (ej: 0 para maleta1, 1 para maleta2, 2 para maleta3).
    /// Asignar a OnClick del botón en Unity Inspector.
    /// </summary>
    public void SetBagSkin(int index)
    {
        if (skinDatabase == null || skinDatabase.BagSkins.Count == 0) return;
        currentBagIndex = Mathf.Clamp(index, 0, skinDatabase.BagSkins.Count - 1);
        SaveAndApplyBag();
    }

    public void SelectBagSkin(int index) => SetBagSkin(index);

    private void SaveAndApplyBag()
    {
        PlayerCustomizationData.BagIndex = currentBagIndex;
        if (preview != null && skinDatabase != null)
        {
            preview.ApplyBagMaterial(skinDatabase.GetBagMaterial(currentBagIndex));
        }
        SyncWithNetwork();
    }

    private void SyncWithNetwork()
    {
        NetworkPlayerSkinSynchronizer[] synchronizers = FindObjectsByType<NetworkPlayerSkinSynchronizer>(FindObjectsSortMode.None);
        foreach (var sync in synchronizers)
        {
            if (sync.IsOwner)
            {
                sync.UpdateSkinSelection(currentHatIndex, currentBodyIndex, currentBagIndex);
                sync.UpdatePlayerName(PlayerCustomizationData.PlayerName);
            }
        }
    }

    private void UpdateAllPreviews()
    {
        if (preview == null) return;
        if (skinDatabase != null)
        {
            preview.ApplyHatMaterial(skinDatabase.GetHatMaterial(currentHatIndex));
            preview.ApplyBodyMaterial(skinDatabase.GetBodyMaterial(currentBodyIndex));
            preview.ApplyBagMaterial(skinDatabase.GetBagMaterial(currentBagIndex));
        }
        preview.ApplyPlayerName(PlayerCustomizationData.PlayerName);
    }

    public void ReturnToConnectionScene()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(connectionSceneName);
    }

    /// <summary>
    /// Método público para asignar al evento OnClick del botón de Volver en la interfaz de usuario.
    /// </summary>
    public void OnBackButtonClicked()
    {
        ReturnToConnectionScene();
    }
}

