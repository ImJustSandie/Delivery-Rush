using UnityEngine;

public static class PlayerCustomizationData
{
    public const int MaxPlayerNameLength = 8;
    public const string DefaultPlayerName = "Jugador";

    private const string KeyPlayerName = "PlayerCustomization_PlayerName";
    private const string KeyHatIndex = "PlayerCustomization_HatIndex";
    private const string KeyBodyIndex = "PlayerCustomization_BodyIndex";
    private const string KeyBagIndex = "PlayerCustomization_BagIndex";
    private const string KeyShirtIndex = "PlayerCustomization_ShirtIndex";
    private const string KeySkinIndex = "PlayerCustomization_SkinIndex";

    public static string PlayerName
    {
        get
        {
            string storedName = PlayerPrefs.GetString(KeyPlayerName, DefaultPlayerName);
            if (string.IsNullOrWhiteSpace(storedName))
            {
                return DefaultPlayerName;
            }
            if (storedName.Length > MaxPlayerNameLength)
            {
                storedName = storedName.Substring(0, MaxPlayerNameLength);
                PlayerPrefs.SetString(KeyPlayerName, storedName);
                PlayerPrefs.Save();
            }
            return storedName;
        }
        set
        {
            string cleanName = value?.Trim();
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                cleanName = DefaultPlayerName;
            }
            else if (cleanName.Length > MaxPlayerNameLength)
            {
                cleanName = cleanName.Substring(0, MaxPlayerNameLength);
            }

            PlayerPrefs.SetString(KeyPlayerName, cleanName);
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

    public static int ShirtIndex
    {
        get => PlayerPrefs.GetInt(KeyShirtIndex, 0);
        set
        {
            PlayerPrefs.SetInt(KeyShirtIndex, value);
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
