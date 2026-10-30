using UnityEditor;
using UnityEngine;

#region Pixel Art Sprite Importer
// Domyślne ustawienia importu dla nowych sprite'ów w Assets/Sprites (pixel art, 32 px = 1 jednostka).
// Działa tylko przy pierwszym imporcie pliku - ręczne zmiany w Inspectorze nie są potem nadpisywane.
public class PixelArtSpriteImporter : AssetPostprocessor
{
    const int PixelsPerUnit = 32;
    const string SpritesFolder = "Assets/Sprites/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(SpritesFolder) || !assetImporter.importSettingsMissing)
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
    }
}
#endregion
