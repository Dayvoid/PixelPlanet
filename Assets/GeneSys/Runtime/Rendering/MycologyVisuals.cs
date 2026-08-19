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
}
