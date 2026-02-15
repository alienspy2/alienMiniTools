using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace RoseEngine
{
    public class Font
    {
        public string name { get; private set; } = "";
        public int fontSize { get; private set; }
        public float lineHeight { get; private set; }    // world units (pixelsPerUnit applied)

        // Atlas
        internal Texture2D? atlasTexture;
        internal Dictionary<char, GlyphInfo> glyphs = new();
        internal float pixelsPerUnit = 100f;

        // Default character set (ASCII printable)
        private const string DefaultCharset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~";

        internal struct GlyphInfo
        {
            public Vector2 uvMin;       // atlas UV top-left
            public Vector2 uvMax;       // atlas UV bottom-right
            public float width;         // glyph bitmap width (px)
            public float height;        // glyph bitmap height (px)
            public float bearingX;      // baseline left offset (px)
            public float bearingY;      // baseline top offset (px)
            public float advance;       // horizontal advance to next char (px)
        }

        /// <summary>Load from system font by family name</summary>
        public static Font Create(string fontFamily, int size)
        {
            var font = new Font { name = fontFamily, fontSize = size };
            var slFamily = SystemFonts.Get(fontFamily);
            var slFont = slFamily.CreateFont(size, SixLabors.Fonts.FontStyle.Regular);
            font.BuildAtlas(slFont);
            return font;
        }

        /// <summary>Load from .ttf/.otf file</summary>
        public static Font CreateFromFile(string path, int size)
        {
            var font = new Font { name = Path.GetFileNameWithoutExtension(path), fontSize = size };
            var collection = new FontCollection();
            var slFamily = collection.Add(path);
            var slFont = slFamily.CreateFont(size, SixLabors.Fonts.FontStyle.Regular);
            font.BuildAtlas(slFont);
            return font;
        }

        /// <summary>Find any available system font as fallback</summary>
        public static Font CreateDefault(int size)
        {
            string[] fallbacks = { "DejaVu Sans", "Liberation Sans", "Arial", "Noto Sans" };
            foreach (var name in fallbacks)
            {
                if (SystemFonts.TryGet(name, out var family))
                {
                    var font = new Font { name = name, fontSize = size };
                    var slFont = family.CreateFont(size, SixLabors.Fonts.FontStyle.Regular);
                    font.BuildAtlas(slFont);
                    return font;
                }
            }
            throw new Exception("No system font found for fallback");
        }

        private void BuildAtlas(SixLabors.Fonts.Font slFont)
        {
            var options = new TextOptions(slFont);
            int padding = 2;  // glyph padding (prevents bleeding)

            // Phase 1: measure each glyph
            // MeasureAdvance = typographic advance (proper spacing)
            // MeasureBounds  = tight ink bounds (actual pixels)
            // MeasureSize    = visual bounding box (too narrow for spacing!)
            var measurements = new List<(char ch, FontRectangle bounds, float advance, float renderWidth)>();
            foreach (char ch in DefaultCharset)
            {
                var bounds = TextMeasurer.MeasureBounds(ch.ToString(), options);
                var advRect = TextMeasurer.MeasureAdvance(ch.ToString(), options);
                float advance = advRect.Width;
                // Render width: max of advance and actual pixel extent, +2px for AA fringe
                float renderWidth = bounds.Width > 0
                    ? MathF.Max(advance, bounds.X + bounds.Width) + 2f
                    : advance;
                measurements.Add((ch, bounds, advance, renderWidth));
            }

            // Phase 2: determine atlas size (row packing)
            int atlasWidth = 512;
            int rowHeight = fontSize + padding * 2;
            int cursorX = padding, cursorY = padding;
            int maxHeight = rowHeight + padding;

            // Simulate layout to calculate required height
            foreach (var (ch, bounds, advance, renderWidth) in measurements)
            {
                int glyphW = (int)MathF.Ceiling(renderWidth) + padding * 2;
                if (cursorX + glyphW > atlasWidth)
                {
                    cursorX = padding;
                    cursorY += rowHeight;
                    maxHeight = cursorY + rowHeight + padding;
                }
                cursorX += glyphW;
            }

            // Round up to power of two
            int atlasHeight = 1;
            while (atlasHeight < maxHeight) atlasHeight *= 2;

            // Phase 3: render glyphs into atlas image
            using var atlas = new Image<Rgba32>(atlasWidth, atlasHeight, new Rgba32(0, 0, 0, 0));
            cursorX = padding;
            cursorY = padding;

            float baseline = slFont.FontMetrics.HorizontalMetrics.Ascender
                * slFont.Size / slFont.FontMetrics.UnitsPerEm;

            foreach (var (ch, bounds, advance, renderWidth) in measurements)
            {
                int glyphW = (int)MathF.Ceiling(renderWidth) + padding * 2;
                if (cursorX + glyphW > atlasWidth)
                {
                    cursorX = padding;
                    cursorY += rowHeight;
                }

                // Render glyph in white (runtime color tint via MaterialUniforms.Color)
                atlas.Mutate(ctx => ctx.DrawText(
                    ch.ToString(),
                    slFont,
                    SixLabors.ImageSharp.Color.White,
                    new PointF(cursorX, cursorY)));

                // Store GlyphInfo — use renderWidth for UV/display, advance for cursor movement
                var info = new GlyphInfo
                {
                    uvMin = new Vector2(
                        (float)cursorX / atlasWidth,
                        (float)cursorY / atlasHeight),
                    uvMax = new Vector2(
                        (float)(cursorX + renderWidth) / atlasWidth,
                        (float)(cursorY + rowHeight - padding * 2) / atlasHeight),
                    width = renderWidth,
                    height = rowHeight - padding * 2,
                    bearingX = bounds.X,
                    bearingY = baseline,
                    advance = advance,
                };

                glyphs[ch] = info;
                cursorX += glyphW;
            }

            // Phase 4: Image -> byte[] -> Texture2D
            byte[] pixelData = new byte[atlasWidth * atlasHeight * 4];
            atlas.CopyPixelDataTo(pixelData);

            atlasTexture = new Texture2D(atlasWidth, atlasHeight);
            atlasTexture.SetPixels(pixelData);

            // lineHeight in world units
            lineHeight = (float)rowHeight / pixelsPerUnit;
        }
    }
}
