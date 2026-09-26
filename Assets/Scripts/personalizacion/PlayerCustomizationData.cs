using UnityEngine;

public static class PlayerCustomizationData
{
    private const string KeyPlayerName = "PlayerCustomization_PlayerName";
    private const string KeyHatIndex = "PlayerCustomization_HatIndex";
    private const string KeyBodyIndex = "PlayerCustomization_BodyIndex";
    private const string KeyBagIndex = "PlayerCustomization_BagIndex";
    private const string KeySkinIndex = "PlayerCustomization_SkinIndex";

    public static string PlayerName
    {
        get => PlayerPrefs.GetString(KeyPlayerName, "Repartidor");
        set
        {
            PlayerPrefs.SetString(KeyPlayerName, value);
            PlayerPrefs.Save();
        }
    }

    public static int HatIndex
    {
        get => PlayerPrefs.GetInt(KeyHatIndex, 0);
        set
        {
            PlayerPrefs.SetInt(KeyHatIndex, value);
            PlayerPrefs.Save();
        }
    }

    public static int BodyIndex
    {
        get => PlayerPrefs.GetInt(KeyBodyIndex, 0);
        set
        {
            PlayerPrefs.SetInt(KeyBodyIndex, value);
            PlayerPrefs.Save();
        }
    }

    public static int BagIndex
    {
        get => PlayerPrefs.GetInt(KeyBagIndex, 0);
        set
        {
            PlayerPrefs.SetInt(KeyBagIndex, value);
            PlayerPrefs.Save();
        }
    }

    public static int SkinIndex
    {
        get => PlayerPrefs.GetInt(KeySkinIndex, 0);
        set
        {
            PlayerPrefs.SetInt(KeySkinIndex, value);
            PlayerPrefs.Save();
        }
    }
}
