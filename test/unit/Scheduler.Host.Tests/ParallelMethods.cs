using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Scheduler.Host.Tests.ParallelMethodsFramework", "Scheduler.Host.Tests")]

namespace Scheduler.Host.Tests;

/// <summary>
/// 1026-B: the tests of a class marked with this run beside each other, not one after another. xUnit runs
/// each class as one collection, in order, so a class of slow tests (a composed program over a temporary
/// repository, 10-40 s each) set the length of the whole run. Mark only a class whose tests share nothing
/// but what each builds for itself: no static state, no class or collection fixture, their own temp dirs,
/// ports and project names. Nothing else about the tests changes: same names, same checks.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ParallelMethodsAttribute : Attribute;

/// <summary>xUnit's own framework, with each test method of a <see cref="ParallelMethodsAttribute"/> class found in a collection of its own.</summary>
public sealed class ParallelMethodsFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo) =>
        new Discoverer(assemblyInfo, SourceInformationProvider, DiagnosticMessageSink);

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(AssemblyName name, ISourceInformationProvider source, IMessageSink diagnostics)
        : XunitTestFrameworkExecutor(name, source, diagnostics)
    {
        // A run of the whole assembly finds its tests again here, so it splits them the same way.
        protected override ITestFrameworkDiscoverer CreateDiscoverer() =>
            new Discoverer(AssemblyInfo, SourceInformationProvider, DiagnosticMessageSink);
    }

    private sealed class Discoverer(IAssemblyInfo assembly, ISourceInformationProvider source, IMessageSink diagnostics)
        : XunitTestFrameworkDiscoverer(assembly, source, diagnostics)
    {
        private static readonly string Marker = typeof(ParallelMethodsAttribute).AssemblyQualifiedName!;

        // xUnit's short form for a fact keeps only the class, the method and the collection's ID, and rebuilds
        // the class's own collection (its name) from them when the run reads it back; the runner then sees
        // several collections of one name. The full form keeps the collection this discoverer made.
        public override string Serialize(ITestCase testCase) =>
            testCase.TestMethod.TestClass.Class.GetCustomAttributes(Marker).Any()
                ? SerializationHelper.Serialize(testCase)
                : base.Serialize(testCase);

        protected override bool FindTestsForType(ITestClass testClass, bool includeSourceInformation, IMessageBus messageBus, ITestFrameworkDiscoveryOptions discoveryOptions)
        {
            if (!testClass.Class.GetCustomAttributes(Marker).Any())
            {
                return base.FindTestsForType(testClass, includeSourceInformation, messageBus, discoveryOptions);
            }

            var collection = testClass.TestCollection;
            foreach (var method in testClass.Class.GetMethods(true))
            {
                // Its own collection, named after the class's and the method, so the name (and the ID made
                // from it) is the same in every discovery and a test case found by the IDE still runs.
                var own = new TestCollection(collection.TestAssembly, collection.CollectionDefinition, $"{collection.DisplayName} / {method.Name}");
                var testMethod = new TestMethod(new TestClass(own, testClass.Class), method);
                if (!FindTestsForMethod(testMethod, includeSourceInformation, messageBus, discoveryOptions))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
