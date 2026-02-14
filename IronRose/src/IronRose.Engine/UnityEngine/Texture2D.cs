using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace UnityEngine
{
    public class Texture2D : IDisposable
    {
        public int width { get; private set; }
        public int height { get; private set; }

        private byte[]? _pixelData;
        internal Veldrid.Texture? VeldridTexture { get; private set; }
        internal TextureView? TextureView { get; private set; }
        private bool _isDirty = true;
        private bool _hasMipmaps;

        public Texture2D(int width, int height)
        {
            this.width = width;
            this.height = height;
            _pixelData = new byte[width * height * 4];
        }

        private Texture2D(int width, int height, byte[] data)
        {
            this.width = width;
            this.height = height;
            _pixelData = data;
        }

        public static Texture2D LoadFromFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Texture file not found: {path}");

            using var image = Image.Load<Rgba32>(path);
            int w = image.Width;
            int h = image.Height;
            var data = new byte[w * h * 4];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        int offset = (y * w + x) * 4;
                        data[offset + 0] = row[x].R;
                        data[offset + 1] = row[x].G;
                        data[offset + 2] = row[x].B;
                        data[offset + 3] = row[x].A;
                    }
                }
            });

            Console.WriteLine($"[Texture2D] Loaded: {path} ({w}x{h})");
            return new Texture2D(w, h, data);
        }

        public static Texture2D CreateWhitePixel()
        {
            var data = new byte[] { 255, 255, 255, 255 };
            return new Texture2D(1, 1, data);
        }

        public void SetPixels(byte[] rgbaData)
        {
            _pixelData = rgbaData;
            _isDirty = true;
        }

        public void UploadToGPU(GraphicsDevice device, bool generateMipmaps = false)
        {
            bool needsMipmapUpgrade = generateMipmaps && !_hasMipmaps && VeldridTexture != null;
            if ((!_isDirty && !needsMipmapUpgrade) || _pixelData == null)
                return;

            var factory = device.ResourceFactory;

            // Dispose old resources
            TextureView?.Dispose();
            VeldridTexture?.Dispose();

            uint mipLevels = 1;
            var usage = TextureUsage.Sampled;
            if (generateMipmaps)
            {
                mipLevels = (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
                usage = TextureUsage.Sampled | TextureUsage.GenerateMipmaps;
            }

            VeldridTexture = factory.CreateTexture(new TextureDescription(
                (uint)width, (uint)height, 1, mipLevels, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                usage,
                TextureType.Texture2D));

            device.UpdateTexture(VeldridTexture, _pixelData,
                0, 0, 0,
                (uint)width, (uint)height, 1,
                0, 0);

            if (generateMipmaps && mipLevels > 1)
            {
                using var cl = factory.CreateCommandList();
                cl.Begin();
                cl.GenerateMipmaps(VeldridTexture);
                cl.End();
                device.SubmitCommands(cl);
            }

            TextureView = factory.CreateTextureView(VeldridTexture);
            _isDirty = false;
            _hasMipmaps = generateMipmaps && mipLevels > 1;
        }

        /// <summary>
        /// Computes the average color of the texture (downsampled).
        /// Useful for IBL ambient approximation from environment maps.
        /// </summary>
        public Color GetAverageColor()
        {
            if (_pixelData == null || _pixelData.Length < 4)
                return Color.gray;

            int pixelCount = _pixelData.Length / 4;
            // Sample every Nth pixel for performance on large textures
            int step = Math.Max(1, pixelCount / 1024);
            double r = 0, g = 0, b = 0;
            int samples = 0;

            for (int i = 0; i < pixelCount; i += step)
            {
                int offset = i * 4;
                r += _pixelData[offset] / 255.0;
                g += _pixelData[offset + 1] / 255.0;
                b += _pixelData[offset + 2] / 255.0;
                samples++;
            }

            return new Color((float)(r / samples), (float)(g / samples), (float)(b / samples), 1f);
        }

        public void Dispose()
        {
            TextureView?.Dispose();
            VeldridTexture?.Dispose();
            TextureView = null;
            VeldridTexture = null;
            _pixelData = null;
        }
    }
}
