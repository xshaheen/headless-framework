// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Headless.Testing.Tests;

namespace Tests.Policy;

/// <summary>
/// Keeps the messaging fast tier in-process: a unit project starts no container and opens no socket. Real-time
/// waits are held by the repository-wide ratchet (<c>scripts/test-discipline.py</c>), which allows no opt-out in
/// these projects because they run on simulated time.
/// </summary>
public sealed partial class FastTierPolicyTests : TestBase
{
    private const string _SelfFileName = $"{nameof(FastTierPolicyTests)}.cs";

    [Fact]
    public void should_keep_testcontainers_and_sockets_out_of_messaging_unit_projects()
    {
        // given
        var unitProjects = Directory.EnumerateDirectories(
            Path.Combine(_FindRepositoryRoot(), "tests"),
            "Headless.Messaging*.Tests.Unit"
        );

        // when
        var violations = unitProjects.SelectMany(_FindIsolationViolations).ToList();

        // then
        violations
            .Should()
            .BeEmpty("a unit project runs in the fast tier, which starts no container and opens no socket");
    }

    [Theory]
    [InlineData("""<PackageReference Include="Testcontainers.Kafka" />""")]
    [InlineData("""<PackageReference Include="Testcontainers" />""")]
    [InlineData(
        """<ProjectReference Include="..\..\src\Headless.Testing.Testcontainers\Headless.Testing.Testcontainers.csproj" />"""
    )]
    public void should_flag_a_unit_project_that_references_testcontainers(string reference)
    {
        var project = $"<Project><ItemGroup>{reference}</ItemGroup></Project>";

        _FindProjectViolations("Sample.Tests.Unit.csproj", project).Should().ContainSingle();
    }

    [Theory]
    [InlineData("using var listener = new TcpListener(IPAddress.Loopback, 0);")]
    [InlineData("using var client = new System.Net.Sockets.TcpClient();")]
    [InlineData("var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);")]
    [InlineData("using DotNet.Testcontainers.Builders;")]
    [InlineData("using Testcontainers.Kafka;")]
    [InlineData("using Headless.Testing.Testcontainers;")]
    public void should_flag_a_unit_test_that_opens_a_socket_or_starts_a_container(string line)
    {
        _FindSourceViolations("SampleTests.cs", line).Should().ContainSingle();
    }

    [Fact]
    public void should_allow_a_unit_test_that_only_simulates_a_socket_failure()
    {
        const string source = """
            using System.Net.Sockets;
            throw new SocketException((int)SocketError.ConnectionRefused);
            """;

        _FindSourceViolations("SampleTests.cs", source).Should().BeEmpty();
    }

    private static IEnumerable<string> _FindIsolationViolations(string projectDirectory)
    {
        var projectViolations = Directory
            .EnumerateFiles(projectDirectory, "*.csproj")
            .SelectMany(path => _FindProjectViolations(path, File.ReadAllText(path)));

        var sourceViolations = _EnumerateSources(projectDirectory)
            .SelectMany(path => _FindSourceViolations(path, File.ReadAllText(path)));

        return projectViolations.Concat(sourceViolations);
    }

    private static IEnumerable<string> _FindProjectViolations(string path, string projectXml)
    {
        return XDocument
            .Parse(projectXml)
            .Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase))
            .Select(include => $"{path} references {include}");
    }

    private static IEnumerable<string> _FindSourceViolations(string path, string source)
    {
        return _SocketOrContainer
            .Matches(_LineComment.Replace(source, string.Empty))
            .Select(match => $"{path} uses {match.Value.Trim()}");
    }

    private static IEnumerable<string> _EnumerateSources(string directory)
    {
        // This file holds sample socket calls as test inputs; scanning it would count them.
        return Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');

                return !relative.StartsWith("bin/", StringComparison.Ordinal)
                    && !relative.StartsWith("obj/", StringComparison.Ordinal)
                    && !string.Equals(Path.GetFileName(path), _SelfFileName, StringComparison.Ordinal);
            });
    }

    private static string _FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }

    // The look-behind keeps a URL scheme such as http:// from reading as the start of a comment.
    [GeneratedRegex(@"(?<!:)//[^\r\n]*", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _LineComment { get; }

    [GeneratedRegex(
        @"\bnew\s+(?:System\.Net\.Sockets\.)?(?:TcpListener|TcpClient|UdpClient|Socket)\s*\(|\busing\s+[\w.]*Testcontainers[\w.]*\s*;",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _SocketOrContainer { get; }
}
