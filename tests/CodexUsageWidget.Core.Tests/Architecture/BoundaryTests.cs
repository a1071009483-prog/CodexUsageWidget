using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CodexUsageWidget.Core.Tests.Architecture;

public sealed class BoundaryTests
{
    private const string CoreAssemblyName = "CodexUsageWidget.Core";
    private const string AbstractionsNamespace = "CodexUsageWidget.Core.Abstractions";

    private static readonly string[] RequiredInterfaces =
    [
        "IClock",
        "IDelay",
        "IProcessHost",
        "IAppFileSystem",
        "IUserNotifier",
        "IModelBoundary",
        "IRedactingLog",
    ];

    [Fact]
    public void CoreExposesTheRequiredDeterministicBoundaries()
    {
        Assembly coreAssembly = LoadCoreAssembly();

        string[] missingInterfaces = RequiredInterfaces
            .Where(name => coreAssembly.GetType($"{AbstractionsNamespace}.{name}") is null)
            .ToArray();

        Assert.True(
            missingInterfaces.Length == 0,
            $"Missing Core boundary interfaces: {string.Join(", ", missingInterfaces)}");

        foreach (string interfaceName in RequiredInterfaces)
        {
            Type boundary = coreAssembly.GetType($"{AbstractionsNamespace}.{interfaceName}")!;
            Assert.True(boundary.IsInterface, $"{boundary.FullName} must be an interface.");
        }

        Type clock = coreAssembly.GetType($"{AbstractionsNamespace}.IClock")!;
        PropertyInfo utcNow = Assert.Single(clock.GetProperties());
        Assert.Equal("UtcNow", utcNow.Name);
        Assert.Equal(typeof(DateTimeOffset), utcNow.PropertyType);
        Assert.True(utcNow.CanRead);
        Assert.Null(utcNow.SetMethod);

        Type delay = coreAssembly.GetType($"{AbstractionsNamespace}.IDelay")!;
        MethodInfo delayAsync = Assert.Single(delay.GetMethods());
        Assert.Equal("DelayAsync", delayAsync.Name);
        Assert.Equal(typeof(Task), delayAsync.ReturnType);
        Assert.Collection(
            delayAsync.GetParameters(),
            parameter => Assert.Equal(typeof(TimeSpan), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void CoreDoesNotReferencePlatformOrInfrastructureImplementationTypes()
    {
        Assembly coreAssembly = LoadCoreAssembly();
        AssemblyName[] referencedAssemblies = coreAssembly.GetReferencedAssemblies();

        string[] forbiddenAssemblies = referencedAssemblies
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbiddenAssembly)
            .Order()
            .ToArray();

        using FileStream assemblyStream = File.OpenRead(coreAssembly.Location);
        using var peReader = new PEReader(assemblyStream);
        MetadataReader metadata = peReader.GetMetadataReader();

        string[] forbiddenTypes = metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Select(reference => $"{metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)}")
            .Where(IsForbiddenType)
            .Distinct()
            .Order()
            .ToArray();

        Assert.True(
            forbiddenAssemblies.Length == 0,
            $"Core references forbidden assemblies: {string.Join(", ", forbiddenAssemblies)}");
        Assert.True(
            forbiddenTypes.Length == 0,
            $"Core references forbidden implementation types: {string.Join(", ", forbiddenTypes)}");
    }

    [Fact]
    public void CoreBoundariesExposeUsableAsynchronousContracts()
    {
        Assembly coreAssembly = LoadCoreAssembly();
        string[] requiredTypes =
        [
            "IHostedProcess",
            "ProcessStartRequest",
            "ProcessExitResult",
            "AppFileReadRequest",
            "AppFileReadResult",
            "AppFileWriteRequest",
            "AppFileWriteResult",
            "UserNotificationRequest",
            "UserNotificationResult",
            "ModelGenerationRequest",
            "ModelGenerationResult",
            "StructuredLogEvent",
        ];

        string[] missingTypes = requiredTypes
            .Where(name => coreAssembly.GetType($"{AbstractionsNamespace}.{name}") is null)
            .ToArray();

        Assert.True(
            missingTypes.Length == 0,
            $"Missing operational boundary types: {string.Join(", ", missingTypes)}");

        Type TypeNamed(string name) => coreAssembly.GetType($"{AbstractionsNamespace}.{name}")!;

        AssertMethod(
            TypeNamed("IProcessHost"),
            "StartAsync",
            typeof(Task<>).MakeGenericType(TypeNamed("IHostedProcess")),
            TypeNamed("ProcessStartRequest"),
            typeof(CancellationToken));
        AssertMethod(
            TypeNamed("IHostedProcess"),
            "WaitForExitAsync",
            typeof(Task<>).MakeGenericType(TypeNamed("ProcessExitResult")),
            typeof(CancellationToken));
        AssertMethod(
            TypeNamed("IAppFileSystem"),
            "ReadTextAsync",
            typeof(Task<>).MakeGenericType(TypeNamed("AppFileReadResult")),
            TypeNamed("AppFileReadRequest"),
            typeof(CancellationToken));
        AssertMethod(
            TypeNamed("IAppFileSystem"),
            "WriteTextAsync",
            typeof(Task<>).MakeGenericType(TypeNamed("AppFileWriteResult")),
            TypeNamed("AppFileWriteRequest"),
            typeof(CancellationToken));
        AssertMethod(
            TypeNamed("IUserNotifier"),
            "NotifyAsync",
            typeof(Task<>).MakeGenericType(TypeNamed("UserNotificationResult")),
            TypeNamed("UserNotificationRequest"),
            typeof(CancellationToken));
        AssertMethod(
            TypeNamed("IModelBoundary"),
            "StartGenerationAsync",
            typeof(Task<>).MakeGenericType(TypeNamed("ModelGenerationResult")),
            TypeNamed("ModelGenerationRequest"),
            typeof(CancellationToken));
        AssertMethod(
            TypeNamed("IRedactingLog"),
            "WriteAsync",
            typeof(ValueTask),
            TypeNamed("StructuredLogEvent"),
            typeof(CancellationToken));
    }

    private static Assembly LoadCoreAssembly() => Assembly.Load(CoreAssemblyName);

    private static void AssertMethod(
        Type declaringType,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo? method = declaringType.GetMethod(methodName, parameterTypes);

        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
    }

    private static bool IsForbiddenAssembly(string assemblyName) =>
        assemblyName is "PresentationCore"
            or "PresentationFramework"
            or "WindowsBase"
            or "System.Windows.Forms"
            or "Microsoft.Data.Sqlite"
            or "System.Diagnostics.Process"
            or "Microsoft.Win32.Registry"
            or "System.IO.FileSystem";

    private static bool IsForbiddenType(string fullName) =>
        fullName.StartsWith("System.Windows.", StringComparison.Ordinal)
        || fullName.StartsWith("Microsoft.Data.Sqlite.", StringComparison.Ordinal)
        || fullName.StartsWith("System.Diagnostics.Process", StringComparison.Ordinal)
        || fullName.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal)
        || fullName.StartsWith("System.IO.File", StringComparison.Ordinal)
        || fullName.StartsWith("System.IO.Directory", StringComparison.Ordinal)
        || fullName.StartsWith("System.IO.FileSystem", StringComparison.Ordinal);
}
