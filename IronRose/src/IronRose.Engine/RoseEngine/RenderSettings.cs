using IronRose.Rendering;

namespace RoseEngine
{
    /// <summary>
    /// Unity-compatible RenderSettings class.
    /// Controls global rendering settings such as skybox material and ambient lighting.
    /// </summary>
    public static class RenderSettings
    {
        /// <summary>
        /// Post-processing effect stack. Set by engine at init time.
        /// Use GetEffect&lt;T&gt;() to access individual effects.
        /// </summary>
        public static PostProcessStack? postProcessing { get; set; }
        /// <summary>
        /// The skybox material. Assign a Material with Shader "Skybox/Panoramic"
        /// and a mainTexture for environment map, or "Skybox/Procedural" for
        /// procedural atmospheric sky.
        /// </summary>
        public static Material? skybox { get; set; }

        /// <summary>
        /// Global ambient light color. Used as fallback when no skybox is set.
        /// </summary>
        public static Color ambientLight { get; set; } = new Color(0.2f, 0.2f, 0.2f, 1f);

        /// <summary>
        /// Ambient intensity multiplier for IBL.
        /// </summary>
        public static float ambientIntensity { get; set; } = 1.0f;

        /// <summary>
        /// Procedural sky zenith (top) color.
        /// </summary>
        public static Color skyZenithColor { get; set; } = new Color(0.15f, 0.3f, 0.65f);

        /// <summary>
        /// Procedural sky horizon color.
        /// </summary>
        public static Color skyHorizonColor { get; set; } = new Color(0.6f, 0.7f, 0.85f);

        /// <summary>
        /// Procedural sky zenith intensity.
        /// </summary>
        public static float skyZenithIntensity { get; set; } = 0.8f;

        /// <summary>
        /// Procedural sky horizon intensity.
        /// </summary>
        public static float skyHorizonIntensity { get; set; } = 1.0f;
    }
}
