using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sincroniza la skin y el nombre seleccionados del jugador local a través de la red (NGO)
/// para que todos los demás clientes la vean en tiempo real en el Lobby y la Partida.
/// </summary>
public class NetworkPlayerSkinSynchronizer : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Base de datos de skins con los materiales.")]
    [SerializeField] private CharacterSkinDatabase skinDatabase;

    [Tooltip("Componente de preview/renderers adjunto al jugador.")]
    [SerializeField] private CharacterCustomizationPreview customizationPreview;

    // Variables de red sincronizadas (el servidor escribe, todos leen)
    private readonly NetworkVariable<int> netHatIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> netBodyIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> netBagIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> netSkinIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<FixedString64Bytes> netPlayerName = new NetworkVariable<FixedString64Bytes>("Repartidor", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string PlayerName => netPlayerName.Value.ToString();
    public NetworkVariable<FixedString64Bytes> NetPlayerName => netPlayerName;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (customizationPreview == null)
        {
            customizationPreview = GetComponent<CharacterCustomizationPreview>();
        }

        // Suscribirse a cambios en las variables de red para actualizar materiales y nombre en pantalla
        netHatIndex.OnValueChanged += OnHatChanged;
        netBodyIndex.OnValueChanged += OnBodyChanged;
        netBagIndex.OnValueChanged += OnBagChanged;
        netSkinIndex.OnValueChanged += OnSkinChanged;
        netPlayerName.OnValueChanged += OnPlayerNameChanged;

        // Si soy el cliente dueño de este personaje, enviar mis elecciones guardadas al servidor
        if (IsOwner)
        {
            int myHat = PlayerCustomizationData.HatIndex;
            int myBody = PlayerCustomizationData.BodyIndex;
            int myBag = PlayerCustomizationData.BagIndex;
            int mySkin = PlayerCustomizationData.SkinIndex;
            string myName = PlayerCustomizationData.PlayerName;

            SubmitSkinSelectionServerRpc(myHat, myBody, myBag, mySkin);
            SubmitPlayerNameServerRpc(myName);
            ApplySkin(myHat, myBody, myBag, mySkin);
        }
        else
        {
            // Aplicar el estado actual que el servidor ya conoce para los demás jugadores
            ApplySkin(netHatIndex.Value, netBodyIndex.Value, netBagIndex.Value, netSkinIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        netHatIndex.OnValueChanged -= OnHatChanged;
        netBodyIndex.OnValueChanged -= OnBodyChanged;
        netBagIndex.OnValueChanged -= OnBagChanged;
        netSkinIndex.OnValueChanged -= OnSkinChanged;
        netPlayerName.OnValueChanged -= OnPlayerNameChanged;
    }

    /// <summary>
    /// Actualiza el nombre del jugador local dinámicamente y lo transmite a la red.
    /// </summary>
    public void UpdatePlayerName(string newName)
    {
        if (!IsOwner) return;

        if (string.IsNullOrWhiteSpace(newName))
        {
            newName = "Repartidor";
        }

        if (IsSpawned)
        {
            SubmitPlayerNameServerRpc(newName);
        }
    }

    public void UpdateSkinSelection(int hat, int body, int bag)
    {
        UpdateSkinSelection(hat, body, bag, PlayerCustomizationData.SkinIndex);
    }

    /// <summary>
    /// Actualiza la skin del jugador local dinámicamente y la transmite a través del servidor a todos los clientes.
    /// </summary>
    public void UpdateSkinSelection(int hat, int body, int bag, int skin)
    {
        if (!IsOwner) return;

        ApplySkin(hat, body, bag, skin);

        if (IsSpawned)
        {
            SubmitSkinSelectionServerRpc(hat, body, bag, skin);
        }
    }

    [ServerRpc]
    private void SubmitSkinSelectionServerRpc(int hat, int body, int bag, int skin)
    {
        netHatIndex.Value = hat;
        netBodyIndex.Value = body;
        netBagIndex.Value = bag;
        netSkinIndex.Value = skin;
    }

    [ServerRpc]
    private void SubmitPlayerNameServerRpc(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Repartidor";
        netPlayerName.Value = new FixedString64Bytes(name);
    }

    private void OnHatChanged(int oldVal, int newVal) => ApplySkin(newVal, netBodyIndex.Value, netBagIndex.Value, netSkinIndex.Value);
    private void OnBodyChanged(int oldVal, int newVal) => ApplySkin(netHatIndex.Value, newVal, netBagIndex.Value, netSkinIndex.Value);
    private void OnBagChanged(int oldVal, int newVal) => ApplySkin(netHatIndex.Value, netBodyIndex.Value, newVal, netSkinIndex.Value);
    private void OnSkinChanged(int oldVal, int newVal) => ApplySkin(netHatIndex.Value, netBodyIndex.Value, netBagIndex.Value, newVal);

    private void OnPlayerNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
    {
        LobbyPlayerDisplay display = GetComponent<LobbyPlayerDisplay>();
        if (display != null)
        {
            display.UpdatePlayerLabel();
        }
    }

    private void ApplySkin(int hat, int body, int bag, int skin)
    {
        if (customizationPreview == null || skinDatabase == null) return;

        customizationPreview.ApplyHatMaterial(skinDatabase.GetHatMaterial(hat));
        customizationPreview.ApplyBodyMaterial(skinDatabase.GetBodyMaterial(body));
        customizationPreview.ApplyBagMaterial(skinDatabase.GetBagMaterial(bag));
        customizationPreview.ApplySkinMaterial(skinDatabase.GetSkinMaterial(skin));
    }
}

