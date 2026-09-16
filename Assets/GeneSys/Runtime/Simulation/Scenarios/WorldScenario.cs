namespace GeneSys.Simulation.Scenarios
{
    public enum WorldScenario
    {
        Sandbox = 0,
        WorldWideWater = 1,
        CometStruckMoon = 2
    }

    public static class WorldScenarioLabels
    {
        public static readonly string[] Choices =
        {
            "Sandbox",
            "World-Wide Water",
            "Comet-Struck Moon"
        };

        public static string DisplayName(WorldScenario scenario)
        {
            int index = (int)scenario;
            return index >= 0 && index < Choices.Length ? Choices[index] : scenario.ToString();
        }
    }
}
