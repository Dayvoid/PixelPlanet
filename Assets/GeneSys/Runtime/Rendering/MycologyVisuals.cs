using UnityEngine;

namespace GeneSys.Rendering
{
    public static class MycologyVisuals
    {
        public static readonly Color SoilColonized = new(0.10f, 0.32f, 0.11f, 1f);
        public static readonly Color SedimentColonized = new(0.62f, 0.68f, 0.48f, 1f);
        public const int OverlayMode = 16;

        public static Color BlendSoil(Color baseColor, float mycoValue) =>
            Color.Lerp(baseColor, SoilColonized, Mathf.Clamp01(mycoValue));

        public static Color BlendSediment(Color baseColor, float mycoValue) =>
            Color.Lerp(baseColor, SedimentColonized, Mathf.Clamp01(mycoValue));
    }

    public static class FloraVisuals
    {
        public static readonly Color Active = new Color(0.18f, 0.62f, 0.24f, 1f);
        public static readonly Color Dormant = new Color(0.28f, 0.42f, 0.22f, 1f);
        public static readonly Color Desiccated = new Color(0.45f, 0.32f, 0.14f, 1f);
        public static readonly Color Spore = new Color(0.72f, 0.82f, 0.38f, 1f);
        public const int OverlayMode = 20;
        public const int LightOverlayMode = 21;
        public const int GenomeOverlayMode = 22;

        public static Color StageColor(uint stage)
        {
            if (stage == 1) return Active;
            if (stage == 2) return Dormant;
            if (stage == 3) return Desiccated;
            return Spore;
        }

        public static int SizeTier(float biomass)
        {
            biomass = Mathf.Clamp01(biomass);
            if (biomass < 0.33f) return 0;
            if (biomass < 0.66f) return 1;
            return 2;
        }
    }

    public static class GrassVisuals
    {
        public static readonly Color Blade = new Color(0.22f, 0.62f, 0.18f, 1f);
        public static readonly Color Root = new Color(0.28f, 0.16f, 0.08f, 1f);
        public static readonly Color Flower = new Color(0.92f, 0.82f, 0.35f, 1f);
        public const int OverlayMode = 20;

        public static Color BladeColor(byte gene)
        {
            float t = gene / 255f;
            return Color.Lerp(new Color(0.16f, 0.48f, 0.14f, 1f), new Color(0.32f, 0.78f, 0.22f, 1f), t);
        }

        public static Color FlowerColor(uint traits)
        {
            Color color = Flower;
            if ((traits & 16u) != 0u || (traits & 32u) != 0u)
                color = Color.Lerp(color, new Color(1f, 0.35f, 0.12f, 1f), 0.55f);
            if ((traits & 1u) != 0u || (traits & 2u) != 0u)
                color = Color.Lerp(color, new Color(0.85f, 0.72f, 0.2f, 1f), 0.4f);
            if ((traits & 4u) != 0u || (traits & 8u) != 0u)
                color = Color.Lerp(color, new Color(0.25f, 0.85f, 1f, 1f), 0.4f);
            return color;
        }
    }

    public static class FaunaVisuals
    {
        public static readonly Color Juvenile = new Color(0.55f, 0.38f, 0.16f, 1f);
        public static readonly Color Adult = new Color(0.42f, 0.26f, 0.10f, 1f);
        public static readonly Color Egg = new Color(0.82f, 0.74f, 0.48f, 1f);
        public const int OverlayMode = 23;
        public const int AcousticOverlayMode = 24;

        public static Color StageColor(uint stage)
        {
            if (stage == 3) return Adult;
            if (stage == 2) return Juvenile;
            return Egg;
        }
    }
}
