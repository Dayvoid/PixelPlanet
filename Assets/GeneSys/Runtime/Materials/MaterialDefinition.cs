using UnityEngine;

namespace GeneSys.Materials
{
    public enum MaterialCategory { Empty, Gas, Liquid, Granular, Solid, Magma, Biological }

    [CreateAssetMenu(menuName = "GeneSys/Material Definition", fileName = "MaterialDefinition")]
    public sealed class MaterialDefinition : ScriptableObject
    {
        [Min(0)] public int stableId;
        public string displayName = "Material";
        public MaterialCategory category;
        public Color displayColor = Color.magenta;
        [Range(0f, 0.5f)] public float shadeVariation = 0.18f;

        [Header("Physical")]
        [Min(0f)] public float density = 1f;
        [Range(0f, 1f)] public float rigidity;
        [Range(0f, 89f)] public float angleOfRepose = 30f;
        [Min(0.01f)] public float grainSize = 1f;
        [Range(-2f, 2f)] public float buoyancyBias;
        [Tooltip("When enabled, this material may density-sort against liquids. Density alone decides float vs sink; buoyancyBias only scales exchange rate.")]
        public bool densityDisplaceable;

        [Header("Transport")]
        [Min(0f)] public float thermalConductivity = 0.1f;
        [Min(0.001f)] public float heatCapacity = 1f;
        [Min(0f)] public float electricalConductivity;
        [Range(0f, 1f)] public float absorbency;
        [Range(0f, 1f)] public float porosity;

        [Header("Phase and expansion")]
        public float meltingTemperature = 1000f;
        public float boilingTemperature = 2000f;
        public float thermalExpansion;
        public float electricalExpansion;
        [Min(0f)] public float latentHeat;
        [Min(0)] public int solidPhaseId;
        [Min(0)] public int liquidPhaseId;
        [Min(0)] public int gasPhaseId;

        [Header("Biology")]
        [Min(0f)] public float toxicity;
        [Min(0f)] public float caloricContent;
        public bool bioModifiable = true;

        [Header("Combustion")]
        public float ignitionTemperature = 10000f;
        public float flashPoint = 10000f;
        [Min(0f)] public float oxygenDemand;
        [Min(0f)] public float smokeYield;

        public MaterialGpuData ToGpuData()
        {
            uint phaseIds = (uint)(solidPhaseId & 1023) | ((uint)(liquidPhaseId & 1023) << 10) | ((uint)(gasPhaseId & 1023) << 20);
            return new MaterialGpuData
            {
                color = displayColor,
                physical = new Vector4(density, rigidity, angleOfRepose * Mathf.Deg2Rad, grainSize),
                transport = new Vector4(thermalConductivity, heatCapacity, electricalConductivity, absorbency),
                phase = new Vector4(meltingTemperature, boilingTemperature, thermalExpansion, electricalExpansion),
                biology = new Vector4(toxicity, caloricContent, porosity, buoyancyBias),
                metadata = new Vector4((float)category, phaseIds, bioModifiable ? 1f : 0f, stableId),
                motion = new Vector4(densityDisplaceable ? 1f : 0f, latentHeat, 0f, 0f),
                combustion = new Vector4(ignitionTemperature, flashPoint, oxygenDemand, smokeYield)
            };
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MaterialGpuData
    {
        public Vector4 color;
        public Vector4 physical;
        public Vector4 transport;
        public Vector4 phase;
        public Vector4 biology;
        public Vector4 metadata;
        public Vector4 motion;
        public Vector4 combustion;
        public const int Stride = 128;
    }
}
