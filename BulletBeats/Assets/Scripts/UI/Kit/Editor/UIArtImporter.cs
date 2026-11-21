using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rules for the menu art in Resources/UI/Art, so a replacement PNG dropped in with the
/// same name is set up the same way: a single sprite, no mipmaps, full quality. Button frames
/// (btn_*) get 9-slice borders so they stretch to any width without blurring their corners.
/// </summary>
class UIArtImporter : AssetPostprocessor
{
    const string ArtFolder = "Assets/Resources/UI/Art/";

    /// <summary>Border (left, bottom, right, top) in source pixels for the 724 x 256 button frames.</summary>
    static readonly Vector4 ButtonBorder = new Vector4(110f, 80f, 110f, 80f);

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ArtFolder)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;

        string file = System.IO.Path.GetFileName(assetPath);
        if (file.StartsWith("btn_"))
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder = ButtonBorder;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }
}
