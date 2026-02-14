using System.IO;
using UnityEngine;

namespace IronRose.AssetPipeline
{
    public class TextureImporter
    {
        public Texture2D Import(string texturePath)
        {
            if (!File.Exists(texturePath))
            {
                Debug.LogError($"Texture not found: {texturePath}");
                return null!;
            }

            return Texture2D.LoadFromFile(texturePath);
        }
    }
}
