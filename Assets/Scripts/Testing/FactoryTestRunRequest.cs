using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Factory.Tests
{
    /// <summary>
    /// Allows an already-open Editor to run this assembly after a script reload.
    /// Create Temp/Factory.Tests.run, then refresh the Asset Database.
    /// </summary>
    [InitializeOnLoad]
    internal static class FactoryTestRunRequest
    {
        private const string RequestPath = "Temp/Factory.Tests.run";
        private const string ResultPath = "Temp/Factory.Tests.result";
        private static TestRunnerApi runner;

        static FactoryTestRunRequest()
        {
            if (!File.Exists(RequestPath))
            {
                return;
            }

            File.Delete(RequestPath);
            EditorApplication.delayCall += RunRequestedTests;
        }

        private static void RunRequestedTests()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new ResultCallbacks());
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "Factory.Tests" }
            }));
        }

        private sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string summary =
                    $"result={result.ResultState}\n" +
                    $"passed={result.PassCount}\n" +
                    $"failed={result.FailCount}\n" +
                    $"skipped={result.SkipCount}\n" +
                    $"duration={result.Duration:F6}\n";
                File.WriteAllText(ResultPath, summary);
                Object.DestroyImmediate(runner);
                runner = null;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
