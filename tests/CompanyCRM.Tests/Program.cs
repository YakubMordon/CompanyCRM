using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace CompanyCRM.Tests;

public class TestRunner
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================================================");
        Console.WriteLine("  CompanyCRM Adversarial & Empirical Test Runner (Milestone 3)");
        Console.WriteLine("========================================================================");

        var stopwatch = Stopwatch.StartNew();
        int passed = 0;
        int failed = 0;
        var failureDetails = new List<(string Name, Exception Ex)>();

        using var fixture = new CrmApiTestFixture();

        // Target test classes to execute
        var testClassTypes = new[]
        {
            typeof(ChallengerM3AdversarialTests),
            typeof(RbacSecurityIntegrationTests),
            typeof(PersonaSwitcherIntegrationTests),
            typeof(SyncWebhookIntegrationTests),
            typeof(WorkItemsAndTimerIntegrationTests),
            typeof(ReviewWorkflowIntegrationTests),
            typeof(PlanningAndDirectoryIntegrationTests)
        };

        foreach (var testClass in testClassTypes)
        {
            Console.WriteLine($"\n--- Running Suite: {testClass.Name} ---");
            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(testClass, fixture);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Failed to instantiate {testClass.Name}: {ex.Message}");
                failed++;
                continue;
            }

            var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var factAttr = method.GetCustomAttribute<FactAttribute>();
                var theoryAttr = method.GetCustomAttribute<TheoryAttribute>();
                var inlineDataAttrs = method.GetCustomAttributes<InlineDataAttribute>().ToList();

                if (theoryAttr != null || inlineDataAttrs.Count > 0)
                {
                    foreach (var inlineData in inlineDataAttrs)
                    {
                        var dataList = inlineData.GetData(method);
                        foreach (var testArgs in dataList)
                        {
                            var argDisplay = string.Join(", ", testArgs.Select(a => a?.ToString() ?? "null"));
                            var testName = $"{testClass.Name}.{method.Name}({argDisplay})";
                            var sw = Stopwatch.StartNew();
                            try
                            {
                                var result = method.Invoke(instance, testArgs);
                                if (result is Task asyncTask)
                                {
                                    await asyncTask;
                                }
                                sw.Stop();
                                Console.WriteLine($"  [PASS] {method.Name}({argDisplay}) ({sw.ElapsedMilliseconds} ms)");
                                passed++;
                            }
                            catch (Exception ex)
                            {
                                sw.Stop();
                                var actualEx = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                                Console.WriteLine($"  [FAIL] {method.Name}({argDisplay}) ({sw.ElapsedMilliseconds} ms)");
                                Console.WriteLine($"         Error: {actualEx.Message}");
                                failureDetails.Add((testName, actualEx));
                                failed++;
                            }
                        }
                    }
                }
                else if (factAttr != null)
                {
                    var testName = $"{testClass.Name}.{method.Name}";
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var result = method.Invoke(instance, null);
                        if (result is Task asyncTask)
                        {
                            await asyncTask;
                        }
                        sw.Stop();
                        Console.WriteLine($"  [PASS] {method.Name} ({sw.ElapsedMilliseconds} ms)");
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        var actualEx = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                        Console.WriteLine($"  [FAIL] {method.Name} ({sw.ElapsedMilliseconds} ms)");
                        Console.WriteLine($"         Error: {actualEx.Message}");
                        failureDetails.Add((testName, actualEx));
                        failed++;
                    }
                }
            }
        }

        stopwatch.Stop();
        Console.WriteLine("\n========================================================================");
        Console.WriteLine($"  Execution Complete: {passed} Passed, {failed} Failed (Total: {passed + failed}) in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine("========================================================================");

        if (failed > 0)
        {
            Console.WriteLine("\nFailures Summary:");
            foreach (var (name, ex) in failureDetails)
            {
                Console.WriteLine($"  - {name}: {ex.Message}");
            }
            return 1;
        }

        return 0;
    }
}
