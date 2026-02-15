using System;
using Veldrid;

namespace UnityEngine
{
    public class Cubemap : IDisposable
    {
        public int faceSize { get; }
        internal TextureView? TextureView { get; private set; }

        private readonly byte[][] _faceData; // 6 faces, RGBA
        private Veldrid.Texture? _veldridTexture;

        private Cubemap(int faceSize, byte[][] faceData)
        {
            this.faceSize = faceSize;
            _faceData = faceData;
        }

        // OpenGL cubemap face order: +X, -X, +Y, -Y, +Z, -Z
        private enum Face { PosX = 0, NegX, PosY, NegY, PosZ, NegZ }

        public static Cubemap CreateFromEquirectangular(Texture2D equirect, int faceSize = 512)
        {
            if (equirect._pixelData == null)
                throw new InvalidOperationException("Texture2D has no pixel data");

            var faceData = new byte[6][];
            int srcW = equirect.width;
            int srcH = equirect.height;
            byte[] src = equirect._pixelData;

            for (int face = 0; face < 6; face++)
            {
                faceData[face] = new byte[faceSize * faceSize * 4];

                for (int y = 0; y < faceSize; y++)
                {
                    for (int x = 0; x < faceSize; x++)
                    {
                        // Map pixel (x,y) on face to a 3D direction
                        float u = ((x + 0.5f) / faceSize) * 2f - 1f;
                        float v = ((y + 0.5f) / faceSize) * 2f - 1f;

                        float dx, dy, dz;
                        switch ((Face)face)
                        {
                            case Face.PosX: dx = 1f;  dy = -v; dz = -u; break;
                            case Face.NegX: dx = -1f; dy = -v; dz = u;  break;
                            case Face.PosY: dx = u;   dy = 1f; dz = v;  break;
                            case Face.NegY: dx = u;   dy = -1f; dz = -v; break;
                            case Face.PosZ: dx = u;   dy = -v; dz = 1f; break;
                            default:        dx = -u;  dy = -v; dz = -1f; break; // NegZ
                        }

                        // Normalize to unit vector
                        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                        dx /= len; dy /= len; dz /= len;

                        // Direction → equirectangular UV
                        float eqU = MathF.Atan2(dz, dx) / (2f * MathF.PI) + 0.5f;
                        float eqV = MathF.Asin(Math.Clamp(dy, -1f, 1f)) / MathF.PI + 0.5f;
                        eqV = 1f - eqV; // flip V so top = zenith

                        // Bilinear sample from equirectangular source
                        SampleBilinear(src, srcW, srcH, eqU, eqV,
                            out byte r, out byte g, out byte b, out byte a);

                        int dstOffset = (y * faceSize + x) * 4;
                        faceData[face][dstOffset + 0] = r;
                        faceData[face][dstOffset + 1] = g;
                        faceData[face][dstOffset + 2] = b;
                        faceData[face][dstOffset + 3] = a;
                    }
                }
            }

            Console.WriteLine($"[Cubemap] Created from equirectangular ({equirect.width}x{equirect.height}) → {faceSize}x{faceSize} cubemap");
            return new Cubemap(faceSize, faceData);
        }

        public static Cubemap CreateWhiteCubemap()
        {
            var faceData = new byte[6][];
            for (int face = 0; face < 6; face++)
            {
                faceData[face] = new byte[4]; // 1x1 white pixel
                faceData[face][0] = 255;
                faceData[face][1] = 255;
                faceData[face][2] = 255;
                faceData[face][3] = 255;
            }
            return new Cubemap(1, faceData);
        }

        public void UploadToGPU(GraphicsDevice device, bool generateMipmaps = false)
        {
            var factory = device.ResourceFactory;

            // Dispose old resources
            TextureView?.Dispose();
            _veldridTexture?.Dispose();

            uint size = (uint)faceSize;
            uint mipLevels = 1;
            var usage = TextureUsage.Sampled | TextureUsage.Cubemap;

            if (generateMipmaps)
            {
                mipLevels = (uint)MathF.Floor(MathF.Log2(faceSize)) + 1;
                usage |= TextureUsage.GenerateMipmaps;
            }

            // Veldrid cubemap: Texture2D with 6 array layers
            _veldridTexture = factory.CreateTexture(new TextureDescription(
                size, size, 1, mipLevels, 6,
                PixelFormat.R8_G8_B8_A8_UNorm,
                usage,
                TextureType.Texture2D));

            // Upload each face as an array layer
            for (uint face = 0; face < 6; face++)
            {
                device.UpdateTexture(_veldridTexture, _faceData[face],
                    0, 0, 0,
                    size, size, 1,
                    0, face);
            }

            // Generate mipmaps
            if (generateMipmaps && mipLevels > 1)
            {
                using var cl = factory.CreateCommandList();
                cl.Begin();
                cl.GenerateMipmaps(_veldridTexture);
                cl.End();
                device.SubmitCommands(cl);
            }

            TextureView = factory.CreateTextureView(_veldridTexture);
        }

        public Color GetAverageColor()
        {
            double r = 0, g = 0, b = 0;
            int totalSamples = 0;

            for (int face = 0; face < 6; face++)
            {
                int pixelCount = _faceData[face].Length / 4;
                int step = Math.Max(1, pixelCount / 256);

                for (int i = 0; i < pixelCount; i += step)
                {
                    int offset = i * 4;
                    r += _faceData[face][offset] / 255.0;
                    g += _faceData[face][offset + 1] / 255.0;
                    b += _faceData[face][offset + 2] / 255.0;
                    totalSamples++;
                }
            }

            if (totalSamples == 0)
                return Color.gray;

            return new Color((float)(r / totalSamples), (float)(g / totalSamples), (float)(b / totalSamples), 1f);
        }

        public void Dispose()
        {
            TextureView?.Dispose();
            _veldridTexture?.Dispose();
            TextureView = null;
            _veldridTexture = null;
        }

        private static void SampleBilinear(byte[] src, int srcW, int srcH, float u, float v,
            out byte outR, out byte outG, out byte outB, out byte outA)
        {
            // Wrap u, keep v clamped
            float fx = u * srcW - 0.5f;
            float fy = v * srcH - 0.5f;

            int x0 = (int)MathF.Floor(fx);
            int y0 = (int)MathF.Floor(fy);
            float fracX = fx - x0;
            float fracY = fy - y0;

            // Wrap x, clamp y
            int x1 = x0 + 1;
            x0 = ((x0 % srcW) + srcW) % srcW;
            x1 = ((x1 % srcW) + srcW) % srcW;
            y0 = Math.Clamp(y0, 0, srcH - 1);
            int y1 = Math.Clamp(y0 + 1, 0, srcH - 1);

            int i00 = (y0 * srcW + x0) * 4;
            int i10 = (y0 * srcW + x1) * 4;
            int i01 = (y1 * srcW + x0) * 4;
            int i11 = (y1 * srcW + x1) * 4;

            float w00 = (1 - fracX) * (1 - fracY);
            float w10 = fracX * (1 - fracY);
            float w01 = (1 - fracX) * fracY;
            float w11 = fracX * fracY;

            outR = (byte)Math.Clamp(src[i00] * w00 + src[i10] * w10 + src[i01] * w01 + src[i11] * w11 + 0.5f, 0, 255);
            outG = (byte)Math.Clamp(src[i00 + 1] * w00 + src[i10 + 1] * w10 + src[i01 + 1] * w01 + src[i11 + 1] * w11 + 0.5f, 0, 255);
            outB = (byte)Math.Clamp(src[i00 + 2] * w00 + src[i10 + 2] * w10 + src[i01 + 2] * w01 + src[i11 + 2] * w11 + 0.5f, 0, 255);
            outA = (byte)Math.Clamp(src[i00 + 3] * w00 + src[i10 + 3] * w10 + src[i01 + 3] * w01 + src[i11 + 3] * w11 + 0.5f, 0, 255);
        }
    }
}
