using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace GeneSys.Editor
{
    [InitializeOnLoad]
    public static class GeneSysFloraTestRunner
    {
        private static readonly string ResultPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/GeneSysFloraPlayMode.log"));

        private static TestRunnerApi api;
        private static Callback callback;

        static GeneSysFloraTestRunner()
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

        [MenuItem("Tools/GeneSys/Run Flora PlayMode Tests")]
        public static void Run()
        {
            EnsureRegistered();
            File.WriteAllText(ResultPath, $"START {System.DateTime.UtcNow:O}\n");
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                groupNames = new[] { "GeneSys.Tests.FloraIntegrationTests" }
            }));
        }

        private sealed class Callback : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                    File.AppendAllText(ResultPath, $"{result.TestStatus} {result.Name} :: {result.Message}\n");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                File.AppendAllText(ResultPath,
                    $"FINISH status={result.TestStatus} pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount} duration={result.Duration:F3}\n");
                Debug.Log($"FLORA_PLAYMODE_FINISHED status={result.TestStatus} pass={result.PassCount} fail={result.FailCount}");
            }
        }
    }
}
