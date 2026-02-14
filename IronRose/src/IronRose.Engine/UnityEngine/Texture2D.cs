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

        public void UploadToGPU(GraphicsDevice device)
        {
            if (!_isDirty || _pixelData == null)
                return;

            var factory = device.ResourceFactory;

            // Dispose old resources
            TextureView?.Dispose();
            VeldridTexture?.Dispose();

            VeldridTexture = factory.CreateTexture(new TextureDescription(
                (uint)width, (uint)height, 1, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled,
                TextureType.Texture2D));

            device.UpdateTexture(VeldridTexture, _pixelData,
                0, 0, 0,
                (uint)width, (uint)height, 1,
                0, 0);

            TextureView = factory.CreateTextureView(VeldridTexture);
            _isDirty = false;
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
