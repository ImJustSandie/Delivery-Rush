using UnityEngine;
using UnityEngine.EventSystems;

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
    private int currentShirtIndex;
    private int currentSkinIndex;

    private void Start()
    {
        // Cargar datos previos
        currentHatIndex = PlayerCustomizationData.HatIndex;
        currentBodyIndex = PlayerCustomizationData.BodyIndex;
        currentBagIndex = PlayerCustomizationData.BagIndex;
        currentShirtIndex = PlayerCustomizationData.ShirtIndex;
        currentSkinIndex = PlayerCustomizationData.SkinIndex;

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

    // --- Shirt Customization ---
    public void SelectNextShirtSkin()
    {
        if (skinDatabase == null || skinDatabase.ShirtSkins.Count == 0) return;
        currentShirtIndex = (currentShirtIndex + 1) % skinDatabase.ShirtSkins.Count;
        SaveAndApplyShirt();
    }

    public void SelectPreviousShirtSkin()
    {
        if (skinDatabase == null || skinDatabase.ShirtSkins.Count == 0) return;
        currentShirtIndex = (currentShirtIndex - 1 + skinDatabase.ShirtSkins.Count) % skinDatabase.ShirtSkins.Count;
        SaveAndApplyShirt();
    }

    /// <summary>
    /// Selecciona la skin de camisa directamente por índice de lista (ej: 0 para camisa1, 1 para camisa2, 2 para camisa3).
    /// Asignar a OnClick del botón en Unity Inspector.
    /// </summary>
    public void SetShirtSkin(int index)
    {
        if (skinDatabase == null || skinDatabase.ShirtSkins.Count == 0) return;
        currentShirtIndex = Mathf.Clamp(index, 0, skinDatabase.ShirtSkins.Count - 1);
        SaveAndApplyShirt();
    }

    public void SelectShirtSkin(int index) => SetShirtSkin(index);

    private void SaveAndApplyShirt()
    {
        PlayerCustomizationData.ShirtIndex = currentShirtIndex;
        if (preview != null && skinDatabase != null)
        {
            preview.ApplyShirtMaterial(skinDatabase.GetShirtMaterial(currentShirtIndex));
        }
        SyncWithNetwork();
    }

    // --- Base Skin (Piel) Customization ---
    public void SelectNextSkin()
    {
        if (skinDatabase == null || skinDatabase.SkinSkins.Count == 0) return;
        currentSkinIndex = (currentSkinIndex + 1) % skinDatabase.SkinSkins.Count;
        SaveAndApplySkin();
    }

    public void SelectPreviousSkin()
    {
        if (skinDatabase == null || skinDatabase.SkinSkins.Count == 0) return;
        currentSkinIndex = (currentSkinIndex - 1 + skinDatabase.SkinSkins.Count) % skinDatabase.SkinSkins.Count;
        SaveAndApplySkin();
    }

    /// <summary>
    /// Selecciona el tono/material de la piel directamente por índice de lista (ej: 0 para piel1, 1 para piel2, 2 para piel3).
    /// Asignar a OnClick del botón en Unity Inspector.
    /// </summary>
    public void SetSkin(int index)
    {
        if (skinDatabase == null || skinDatabase.SkinSkins.Count == 0) return;
        currentSkinIndex = Mathf.Clamp(index, 0, skinDatabase.SkinSkins.Count - 1);
        SaveAndApplySkin();
    }

    public void SelectSkin(int index) => SetSkin(index);

    private void SaveAndApplySkin()
    {
        PlayerCustomizationData.SkinIndex = currentSkinIndex;
        if (preview != null && skinDatabase != null)
        {
            preview.ApplySkinMaterial(skinDatabase.GetSkinMaterial(currentSkinIndex));
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
                sync.UpdateSkinSelection(currentHatIndex, currentBodyIndex, currentBagIndex, currentShirtIndex, currentSkinIndex);
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
            preview.ApplyShirtMaterial(skinDatabase.GetShirtMaterial(currentShirtIndex));
            preview.ApplySkinMaterial(skinDatabase.GetSkinMaterial(currentSkinIndex));
        }
        preview.ApplyPlayerName(PlayerCustomizationData.PlayerName);
    }

    // --- Panel Management ---
    /// <summary>
    /// Activa un panel de UI. Asignar a OnClick del botón en Unity Inspector
    /// arrastrando el GameObject del panel como argumento.
    /// </summary>
    public void OpenPanel(GameObject panel)
    {
        if (panel != null)
        {
            panel.SetActive(true);
        }
    }

    /// <summary>
    /// Cierra el panel padre del botón que lo invoca. Asignar a OnClick del botón
    /// en Unity Inspector sin arrastrar ninguna referencia (sin parámetros).
    /// Busca hacia arriba el padre más cercano cuyo nombre contenga "Panel";
    /// si no lo encuentra, cierra el padre directo.
    /// </summary>
    public void ClosePanel()
    {
        GameObject clickedButton = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;

        if (clickedButton == null) return;

        Transform parentPanel = FindParentPanel(clickedButton.transform);
        if (parentPanel != null)
        {
            parentPanel.gameObject.SetActive(false);
        }
    }

    private static Transform FindParentPanel(Transform start)
    {
        Transform current = start.parent;
        Transform fallback = current;

        while (current != null)
        {
            if (current.name.ToLower().Contains("panel"))
            {
                return current;
            }
            current = current.parent;
        }

        return fallback;
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

