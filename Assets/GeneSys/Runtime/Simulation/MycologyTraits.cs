using System.Text;

namespace GeneSys.Simulation
{
    /// <summary>
    /// Compact dominant-strain trait bitmask shared with HLSL.
    /// Drought, electric, and heat axes are mutually exclusive (prone vs resistant).
    /// </summary>
    public static class MycologyTraits
    {
        public const uint Basic = 0;
        public const uint DroughtProne = 1u << 0;
        public const uint DroughtResistant = 1u << 1;
        public const uint ElectricProne = 1u << 2;
        public const uint ElectricResistant = 1u << 3;
        public const uint HeatProne = 1u << 4;
        public const uint HeatResistant = 1u << 5;
        public const uint AllFlags = DroughtProne | DroughtResistant | ElectricProne | ElectricResistant | HeatProne | HeatResistant;

        public static uint FromFloat(float packed) => Sanitize((uint)System.Math.Round(packed));

        public static uint Sanitize(uint flags)
        {
            flags &= AllFlags;
            if ((flags & DroughtProne) != 0 && (flags & DroughtResistant) != 0)
                flags &= ~DroughtProne;
            if ((flags & ElectricProne) != 0 && (flags & ElectricResistant) != 0)
                flags &= ~ElectricProne;
            if ((flags & HeatProne) != 0 && (flags & HeatResistant) != 0)
                flags &= ~HeatProne;
            return flags;
        }

        public static bool IsValid(uint flags) => flags == Sanitize(flags);

        public static bool HasContradictions(uint flags)
        {
            flags &= AllFlags;
            return ((flags & DroughtProne) != 0 && (flags & DroughtResistant) != 0)
                || ((flags & ElectricProne) != 0 && (flags & ElectricResistant) != 0)
                || ((flags & HeatProne) != 0 && (flags & HeatResistant) != 0);
        }

        public static string Describe(uint flags)
        {
            flags = Sanitize(flags);
            if (flags == Basic) return "Basic";
            var text = new StringBuilder();
            void Append(string label)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(label);
            }
            if ((flags & DroughtProne) != 0) Append("Drought prone");
            if ((flags & DroughtResistant) != 0) Append("Drought resistant");
            if ((flags & ElectricProne) != 0) Append("Electric prone");
            if ((flags & ElectricResistant) != 0) Append("Electric resistant");
            if ((flags & HeatProne) != 0) Append("Heat prone");
            if ((flags & HeatResistant) != 0) Append("Heat resistant");
            return text.ToString();
        }
    }
}
