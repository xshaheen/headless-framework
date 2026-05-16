// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;

namespace HeadlessShop.Tests.Architecture;

public sealed class ArchitectureRulesTests
{
    private static readonly DirectoryInfo _Root = _FindSolutionRoot();

    [Fact]
    public void modules_must_not_reference_each_other_internals()
    {
        var catalogProjects = _ProjectFiles("HeadlessShop.Catalog.");
        var orderingProjects = _ProjectFiles("HeadlessShop.Ordering.");

        catalogProjects.Should().NotContain(
            project => _Read(project).Contains("HeadlessShop.Ordering.", StringComparison.Ordinal),
            "Catalog must communicate through HeadlessShop.Contracts and Headless messaging, not Ordering internals"
        );

        orderingProjects.Should().NotContain(
            project => _Read(project).Contains("HeadlessShop.Catalog.", StringComparison.Ordinal),
            "Ordering must communicate through HeadlessShop.Contracts and Headless messaging, not Catalog internals"
        );
    }

    [Fact]
    public void endpoints_must_stay_thin()
    {
        var endpointFiles = _CodeFiles("HeadlessShop.Catalog.Api", "HeadlessShop.Ordering.Api");

        foreach (var file in endpointFiles)
        {
            var source = _Read(file);

            source.Should().NotContain("DbContext", "Minimal API endpoints should not use persistence directly");
            source.Should().NotContain("SaveChanges", "Minimal API endpoints should delegate persistence to handlers");
            source.Should().NotContain("new Product", "Minimal API endpoints should not construct domain aggregates");
            source.Should().NotContain("new Order", "Minimal API endpoints should not construct domain aggregates");
        }
    }

    [Fact]
    public void messaging_must_use_headless_abstractions()
    {
        var files = _CodeFiles("HeadlessShop.Catalog.Application", "HeadlessShop.Ordering.Module");

        foreach (var file in files)
        {
            var source = _Read(file);

            source.Should().NotContain("RabbitMQ.Client", "cross-module communication must use Headless messaging");
            source.Should().NotContain("Confluent.Kafka", "cross-module communication must use Headless messaging");
            source.Should().NotContain("Azure.Messaging.ServiceBus", "cross-module communication must use Headless messaging");
        }
    }

    [Fact]
    public void tenant_write_posture_must_be_enabled()
    {
        var program = _Read(_Root.File("HeadlessShop.Api/Program.cs"));

        program.Should().Contain("ResolveFromClaims", "HTTP tenant context must be resolved from authenticated claims");
        program.Should().Contain("RequireTenant", "Mediator commands must fail without tenant context");
        program.Should().Contain("RequireTenantOnPublish", "published integration events must carry tenant context");
        program.Should().Contain("GuardTenantWrites", "tenant-owned EF writes must be guarded");
    }

    private static FileInfo[] _ProjectFiles(string prefix)
    {
        return _Root.EnumerateFiles($"{prefix}*.csproj", SearchOption.AllDirectories).ToArray();
    }

    private static FileInfo[] _CodeFiles(params string[] directories)
    {
        return directories
            .SelectMany(directory => _Root.Directory(directory).EnumerateFiles("*.cs", SearchOption.AllDirectories))
            .ToArray();
    }

    private static string _Read(FileInfo file)
    {
        return File.ReadAllText(file.FullName);
    }

    private static DirectoryInfo _FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null && !current.EnumerateFiles("HeadlessShop.slnx").Any())
        {
            current = current.Parent;
        }

        return current ?? throw new InvalidOperationException("Could not find generated HeadlessShop.slnx root.");
    }
}

internal static class DirectoryInfoExtensions
{
    public static FileInfo File(this DirectoryInfo directory, string relativePath)
    {
        return new(Path.Combine(directory.FullName, relativePath));
    }

    public static DirectoryInfo Directory(this DirectoryInfo directory, string relativePath)
    {
        return new(Path.Combine(directory.FullName, relativePath));
    }
}
