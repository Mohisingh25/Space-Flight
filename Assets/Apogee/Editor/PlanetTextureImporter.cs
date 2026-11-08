using UnityEditor;

namespace Apogee.EditorTools
{
    /// <summary>Import settings for the bundled planet maps (Resources/Planets): full 2K, mipmapped, readable where tinted.</summary>
    public class PlanetTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Planets/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.isReadable = assetPath.EndsWith("moon.jpg");
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.anisoLevel = 4;
            ti.alphaIsTransparency = assetPath.EndsWith(".png");
        }
    }
}
