#if UNITY_EDITOR
#nullable enable

using System;
using System.IO;
using System.Linq;
using Kern.Core.Diagnostics;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Kern.Tests;

/// <summary>Exact-name batch selection for the existing explicit production harness.</summary>
public static class ExplicitProductionTestRunner
{
    private static readonly Observer s_observer = new();

    [InitializeOnLoadMethod]
    private static void ObserveDomainReload()
    {
        if (Application.isBatchMode && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KERN_PRODUCTION_TEST_NAMES")))
        {
            TestRunnerApi.RegisterTestCallback(s_observer);
        }
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException("Explicit production batch runner requires batch mode.");
        }
        string names = Environment.GetEnvironmentVariable("KERN_PRODUCTION_TEST_NAMES") ??
            throw new InvalidOperationException("KERN_PRODUCTION_TEST_NAMES is required.");
        string path = Environment.GetEnvironmentVariable("KERN_PRODUCTION_TEST_RESULTS") ??
            throw new InvalidOperationException("KERN_PRODUCTION_TEST_RESULTS is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Reject a memory-unsafe run before the test API starts any fixture.
        MemoryAllocationGuard.BeginTestRun();
        TestRunnerApi.RegisterTestCallback(s_observer);
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.PlayMode,
            testNames = names.Split(';', StringSplitOptions.RemoveEmptyEntries),
        }));
    }

    private sealed class Observer : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            string path = Environment.GetEnvironmentVariable("KERN_PRODUCTION_TEST_RESULTS") ??
                throw new InvalidOperationException("KERN_PRODUCTION_TEST_RESULTS is required.");
            TestRunnerApi.SaveResultToFile(result, path);
            bool passed = result.FailCount == 0 && result.PassCount > 0 && result.SkipCount == 0 &&
                result.InconclusiveCount == 0;
            string[] requested = (Environment.GetEnvironmentVariable("KERN_PRODUCTION_TEST_NAMES") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            var completed = CompletedNames(result).ToHashSet(StringComparer.Ordinal);
            string[] missing = requested.Where(name => !completed.Contains(name)).ToArray();
            if (missing.Length > 0)
            {
                passed = false;
                Debug.LogError($"Requested production tests did not finish: {string.Join(", ", missing)}");
            }
            Debug.Log($"Production batch tests: passed={result.PassCount}, failed={result.FailCount}, " +
                $"skipped={result.SkipCount}, inconclusive={result.InconclusiveCount}; report={path}");
            EditorApplication.Exit(passed ? 0 : 1);
        }

        private static System.Collections.Generic.IEnumerable<string> CompletedNames(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                yield return result.Test.FullName;
            }
            foreach (ITestResultAdaptor child in result.Children)
            {
                foreach (string name in CompletedNames(child)) { yield return name; }
            }
        }
    }
}
#endif
