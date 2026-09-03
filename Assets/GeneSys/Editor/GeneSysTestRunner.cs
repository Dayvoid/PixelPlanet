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
                File.AppendAllText(ResultPath,
                    $"FINISH status={result.TestStatus} pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount} duration={result.Duration:F3}\n");
                Debug.Log($"GENESYS_TESTS_FINISHED status={result.TestStatus} pass={result.PassCount} fail={result.FailCount}");
            }
        }
    }
}
