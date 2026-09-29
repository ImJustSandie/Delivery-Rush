using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterSkinDatabase", menuName = "Customization/Character Skin Database")]
public class CharacterSkinDatabase : ScriptableObject
{
    [Serializable]
    public struct SkinOption
    {
        public string id;
        public string displayName;
        public Material material;
    }

    [Header("Skin Categories")]
    [SerializeField] private List<SkinOption> hatSkins = new List<SkinOption>();
    [SerializeField] private List<SkinOption> bodySkins = new List<SkinOption>();
    [SerializeField] private List<SkinOption> bagSkins = new List<SkinOption>();
    [SerializeField] private List<SkinOption> shirtSkins = new List<SkinOption>();
    [SerializeField] private List<SkinOption> skinSkins = new List<SkinOption>();

    public IReadOnlyList<SkinOption> HatSkins => hatSkins;
    public IReadOnlyList<SkinOption> BodySkins => bodySkins;
    public IReadOnlyList<SkinOption> BagSkins => bagSkins;
    public IReadOnlyList<SkinOption> ShirtSkins => shirtSkins;
    public IReadOnlyList<SkinOption> SkinSkins => skinSkins;

    public Material GetHatMaterial(int index)
    {
        if (hatSkins == null || hatSkins.Count == 0) return null;
        int safeIndex = Mathf.Clamp(index, 0, hatSkins.Count - 1);
        return hatSkins[safeIndex].material;
    }

    public Material GetBodyMaterial(int index)
    {
        if (bodySkins == null || bodySkins.Count == 0) return null;
        int safeIndex = Mathf.Clamp(index, 0, bodySkins.Count - 1);
        return bodySkins[safeIndex].material;
    }

    public Material GetBagMaterial(int index)
    {
        if (bagSkins == null || bagSkins.Count == 0) return null;
        int safeIndex = Mathf.Clamp(index, 0, bagSkins.Count - 1);
        return bagSkins[safeIndex].material;
    }

    public Material GetShirtMaterial(int index)
    {
        if (shirtSkins == null || shirtSkins.Count == 0) return null;
        int safeIndex = Mathf.Clamp(index, 0, shirtSkins.Count - 1);
        return shirtSkins[safeIndex].material;
    }

    public Material GetSkinMaterial(int index)
    {
        if (skinSkins == null || skinSkins.Count == 0) return null;
        int safeIndex = Mathf.Clamp(index, 0, skinSkins.Count - 1);
        return skinSkins[safeIndex].material;
    }
}
