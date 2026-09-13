using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using System.IO;

namespace GeneSys.Editor
{
    public static class GeneSysTestRunner
    {
        [MenuItem("Tools/GeneSys/Run EditMode Tests")]
        public static void RunEditMode()
        {
            GeneSysTestObserver.Run(TestMode.EditMode);
        }

        [MenuItem("Tools/GeneSys/Run PlayMode Tests")]
        public static void RunPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode);
        }

        [MenuItem("Tools/GeneSys/Run Weather PlayMode Tests")]
        public static void RunWeatherPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.WeatherIntegrationTests");
        }

        [MenuItem("Tools/GeneSys/Run IceCappedWater PlayMode Tests")]
        public static void RunIceCappedWaterPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.IceCappedWaterTests");
        }

        [MenuItem("Tools/GeneSys/Run Density PlayMode Tests")]
        public static void RunDensityPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.DensityDisplacementIntegrationTests");
        }

        [MenuItem("Tools/GeneSys/Run Hydrology PlayMode Tests")]
        public static void RunHydrologyPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.HydrologyIntegrationTests");
        }

        [MenuItem("Tools/GeneSys/Run MaCE EditMode Tests")]
        public static void RunMaceEditMode()
        {
            GeneSysTestObserver.Run(TestMode.EditMode, "GeneSys.Tests.MaceTests");
        }

        [MenuItem("Tools/GeneSys/Run MaCE Sediment PlayMode Tests")]
        public static void RunMaceSedimentPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MaceSedimentIntegrationTests");
        }

        [MenuItem("Tools/GeneSys/Run MaCE Shoreline PlayMode Tests")]
        public static void RunMaceShorelinePlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MaceShorelineMixtureTests");
        }

        [MenuItem("Tools/GeneSys/Run MaCE Karst PlayMode Tests")]
        public static void RunMaceKarstPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MaceKarstSoluteTests");
        }

        [MenuItem("Tools/GeneSys/Run MaCE Geology PlayMode Tests")]
        public static void RunMaceGeologyPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MaceMobileGeologyTests");
        }

        [MenuItem("Tools/GeneSys/Run Geodynamics EditMode Tests")]
        public static void RunGeodynamicsEditMode()
        {
            GeneSysTestObserver.Run(TestMode.EditMode, "GeneSys.Tests.GeodynamicsContractTests");
        }

        [MenuItem("Tools/GeneSys/Run Geodynamics PlayMode Tests")]
        public static void RunGeodynamicsPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.GeodynamicsIntegrationTests");
        }

        [MenuItem("Tools/GeneSys/Run Geology PlayMode Tests")]
        public static void RunGeologyPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.GeologyIntegrationTests");
        }

        [MenuItem("Tools/GeneSys/Run Margolus EditMode Tests")]
        public static void RunMargolusEditMode()
        {
            GeneSysTestObserver.Run(TestMode.EditMode, "GeneSys.Tests.MargolusContractTests");
        }

        [MenuItem("Tools/GeneSys/Run Margolus Prototype PlayMode Tests")]
        public static void RunMargolusPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MargolusPrototypeTests");
        }

        [MenuItem("Tools/GeneSys/Run Margolus Metric PlayMode Tests")]
        public static void RunMargolusMetricPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MargolusMetricTests");
        }

        [MenuItem("Tools/GeneSys/Run Margolus GeoInterface PlayMode Tests")]
        public static void RunMargolusGeoInterfacePlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MargolusGeoInterfaceTests");
        }

        [MenuItem("Tools/GeneSys/Run Margolus WorldGen Stability PlayMode Tests")]
        public static void RunMargolusWorldGenStabilityPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.MargolusWorldGenStabilityTests");
        }

        [MenuItem("Tools/GeneSys/Run WorldGen PlayMode Tests")]
        public static void RunWorldGenPlayMode()
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, "GeneSys.Tests.WorldGenIntegrationTests");
        }

        public static void RunPlayModeGroup(string groupName)
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, groupName);
        }

        public static void RunPlayModeTests(params string[] testNames)
        {
            GeneSysTestObserver.Run(TestMode.PlayMode, null, testNames);
        }
    }

    [InitializeOnLoad]
    internal static class GeneSysTestObserver
    {
        private static readonly string ResultPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/GeneSysTestResults.log"));
        private static TestRunnerApi api;
        private static Callback callback;
        private static bool running;

        static GeneSysTestObserver()
        {
            EditorApplication.delayCall += EnsureRegistered;
        }

        private static void EnsureRegistered()
        {
            if (api != null) return;
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            callback = new Callback();
            api.RegisterCallbacks(callback, 100);
        }

        public static void Run(TestMode mode, string groupName = null, string[] testNames = null)
        {
            EnsureRegistered();
            if (running && !EditorApplication.isPlaying)
                running = false;
            if (running || EditorApplication.isPlaying)
            {
                Debug.LogWarning("GeneSys tests are already running. Wait for the current run to finish.");
                return;
            }
            running = true;
            File.AppendAllText(ResultPath, $"START {mode} {groupName} {System.DateTime.UtcNow:O}\n");
            var filter = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(groupName))
                filter.groupNames = new[] { groupName };
            if (testNames != null && testNames.Length > 0)
                filter.testNames = testNames;
            api.Execute(new ExecutionSettings(filter));
        }

        private sealed class Callback : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed)
                    File.AppendAllText(ResultPath, $"FAIL {result.FullName} :: {result.Message}\n{result.StackTrace}\n");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                running = false;
                File.AppendAllText(ResultPath,
                    $"FINISH status={result.TestStatus} pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount} duration={result.Duration:F3}\n");
                Debug.Log($"GENESYS_TESTS_FINISHED status={result.TestStatus} pass={result.PassCount} fail={result.FailCount}");
            }
        }
    }
}
