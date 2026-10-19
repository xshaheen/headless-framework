// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Headless.Testing.Tests;

namespace Tests.Policy;

/// <summary>
/// Keeps the messaging fast tier fast and in-process. Every real-time wait in a scanned project costs wall time on
/// each run and turns into a flake under load, so the count per project may only fall; a rise fails here before it
/// lands. The harness has its own line because the in-process leaves inherit its test bodies.
/// </summary>
public sealed partial class FastTierPolicyTests : TestBase
{
    private const string _BaselineFileName = "real-wait-baseline.txt";
    private const string _SelfFileName = $"{nameof(FastTierPolicyTests)}.cs";

    private static readonly string[] _ScannedScopes =
    [
        "Headless.Messaging.Tests.Unit",
        "Headless.Messaging.InMemory.Tests.Unit",
        "Headless.Messaging.Tests.Harness",
    ];

    [Fact]
    public void should_keep_real_waits_at_the_checked_in_baseline()
    {
        // given
        var root = _FindRepositoryRoot();
        var baseline = _ParseBaseline(
            File.ReadAllText(Path.Combine(root, "tests", "Headless.Messaging.Tests.Unit", "Policy", _BaselineFileName))
        );

        baseline.Keys.Should().BeEquivalentTo(_ScannedScopes, "the baseline holds one line per scanned project");

        // when
        var failures = _ScannedScopes
            .Select(scope =>
                _CompareWithBaseline(scope, _CountRealWaits(Path.Combine(root, "tests", scope)), baseline[scope])
            )
            .OfType<string>();

        // then
        string.Join(Environment.NewLine + Environment.NewLine, failures).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Headless.Messaging.Tests.Unit")]
    [InlineData("Headless.Messaging.InMemory.Tests.Unit")]
    [InlineData("Headless.Messaging.Tests.Harness")]
    public void should_fail_naming_the_file_when_a_scope_gains_a_real_wait(string scope)
    {
        // given
        var counts = _CountRealWaits([
            new("ExistingTests.cs", "await Task.Delay(10, AbortToken);"),
            new("Retry/NewTests.cs", "await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);"),
        ]);

        // when
        var failure = _CompareWithBaseline(scope, counts, baseline: 1);

        // then
        failure.Should().NotBeNull();
        failure.Should().Contain(scope).And.Contain("above its baseline of 1").And.Contain("Retry/NewTests.cs: 1");
    }

    [Fact]
    public void should_fail_asking_to_lower_the_baseline_when_a_real_wait_is_removed()
    {
        // given
        var counts = _CountRealWaits([new("ConvertedTests.cs", "await clock.Delay(TimeSpan.FromSeconds(1));")]);

        // when
        var failure = _CompareWithBaseline("Headless.Messaging.Tests.Unit", counts, baseline: 1);

        // then
        failure.Should().NotBeNull();
        failure.Should().Contain("Lower its line in").And.Contain("to 0");
    }

    [Fact]
    public void should_pass_when_a_scope_matches_its_baseline()
    {
        var counts = _CountRealWaits([new("SomeTests.cs", "await Task.Delay(10, AbortToken);")]);

        _CompareWithBaseline("Headless.Messaging.Tests.Unit", counts, baseline: 1).Should().BeNull();
    }

    [Fact]
    public void should_count_sleeps_and_spins_but_not_cancellation_only_waits_or_comments()
    {
        // given
        const string source = """
            await Task.Delay(
                TimeSpan.FromMilliseconds(250),
                AbortToken
            );
            Thread.Sleep(5);
            SpinWait.SpinUntil(() => done, TimeSpan.FromMilliseconds(200));
            await TimeProvider.System.Delay(TimeSpan.FromSeconds(1), AbortToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            // await Task.Delay(100, AbortToken);
            /// <remarks>A Task.Delay(50) would flake here.</remarks>
            var url = "http://localhost"; // Thread.Sleep(1)
            """;

        // when
        var counts = _CountRealWaits([new("Sample.cs", source)]);

        // then
        counts.Should().ContainSingle().Which.Value.Should().Be(4);
    }

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

    private static Dictionary<string, int> _CountRealWaits(string scopeDirectory)
    {
        return _CountRealWaits(
            _EnumerateSources(scopeDirectory)
                .Select(path => new KeyValuePair<string, string>(
                    Path.GetRelativePath(scopeDirectory, path).Replace('\\', '/'),
                    File.ReadAllText(path)
                ))
        );
    }

    private static Dictionary<string, int> _CountRealWaits(IEnumerable<KeyValuePair<string, string>> sources)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (path, text) in sources)
        {
            var count = _RealWait.Count(_LineComment.Replace(text, string.Empty));

            if (count > 0)
            {
                counts[path] = count;
            }
        }

        return counts;
    }

    private static string? _CompareWithBaseline(string scope, Dictionary<string, int> counts, int baseline)
    {
        var total = counts.Values.Sum();

        if (total > baseline)
        {
            var files = string.Join(
                Environment.NewLine,
                counts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"  {pair.Key}: {pair.Value.ToString(CultureInfo.InvariantCulture)}")
            );

            return $"{scope} has {total} real-time waits, above its baseline of {baseline}. Drive the wait from a "
                + $"fake clock or a completion signal instead. Files with real-time waits:{Environment.NewLine}{files}";
        }

        if (total < baseline)
        {
            return $"{scope} has {total} real-time waits, below its baseline of {baseline}. Lower its line in "
                + $"tests/Headless.Messaging.Tests.Unit/Policy/{_BaselineFileName} to {total} so the gain is kept.";
        }

        return null;
    }

    private static Dictionary<string, int> _ParseBaseline(string text)
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var count))
            {
                throw new InvalidOperationException($"Baseline line '{line}' is not '<project> <count>'.");
            }

            if (!baseline.TryAdd(parts[0], count))
            {
                throw new InvalidOperationException($"Baseline lists {parts[0]} twice.");
            }
        }

        return baseline;
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
        // This file holds sample waits and socket calls as test inputs; scanning it would count them.
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

    // A wait on Timeout.Infinite ends only on cancellation, so it costs no wall time and is how a fake hangs on purpose.
    [GeneratedRegex(
        @"\bTask\.Delay\s*\((?!\s*Timeout\.Infinite)|\bThread\.Sleep\s*\(|\bSpinWait\.SpinUntil\s*\(|\bTimeProvider\.System\.Delay\s*\(",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _RealWait { get; }

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
