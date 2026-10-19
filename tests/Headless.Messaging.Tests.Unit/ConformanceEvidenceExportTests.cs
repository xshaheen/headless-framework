// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests;

public sealed class ConformanceEvidenceExportTests : TestBase
{
    [Fact]
    public void should_snapshot_every_manifest_cell_with_its_status_and_issue()
    {
        var snapshot = ConformanceEvidenceExport.CreateSnapshot();

        snapshot["simulator"]!.GetValue<string>().Should().Be(SimulatedBroker.ProviderName);
        var providers = snapshot["providers"]!.AsArray().Select(node => node!.AsObject()).ToList();
        providers
            .Select(provider => provider["name"]!.GetValue<string>())
            .Should()
            .BeEquivalentTo(TransportConformanceManifest.Providers.Keys);

        foreach (var provider in providers)
        {
            var profile = TransportConformanceManifest.Providers[provider["name"]!.GetValue<string>()];
            provider["realBrokerLeaf"]!.GetValue<bool>().Should().Be(profile.IsRealBrokerLeafEnabled);

            var cells = provider["cells"]!.AsObject();
            cells.Select(cell => cell.Key).Should().BeEquivalentTo(Enum.GetNames<TransportConformanceScenario>());
            foreach (var (scenario, support) in profile.Scenarios)
            {
                var cell = cells[scenario.ToString()]!;
                cell["status"]!.GetValue<string>().Should().Be(support.Status.ToString());
                (cell["issueUrl"]?.GetValue<string>()).Should().Be(support.IssueUrl);
            }
        }
    }

    [Fact]
    public void should_snapshot_every_catalogued_guarantee_with_its_issue_and_scenarios()
    {
        var guarantees = ConformanceEvidenceExport.CreateSnapshot()["guarantees"]!
            .AsArray()
            .Select(node => node!.AsObject())
            .ToDictionary(node => node["id"]!.GetValue<string>(), StringComparer.Ordinal);

        guarantees.Keys.Should().BeEquivalentTo(MessagingGuarantees.All.Select(guarantee => guarantee.Id));
        foreach (var guarantee in MessagingGuarantees.All)
        {
            var node = guarantees[guarantee.Id];
            node["status"]!.GetValue<string>().Should().Be(guarantee.Status.ToString());
            (node["issueUrl"]?.GetValue<string>()).Should().Be(guarantee.IssueUrl);
            node["manifestScenarios"]!
                .AsArray()
                .Select(scenario => scenario!.GetValue<string>())
                .Should()
                .Equal(guarantee.ManifestScenarios.Select(scenario => scenario.ToString()));
        }
    }

    // The scheduled quarantine check runs this test with the export variable set and reads the file it leaves there;
    // without the variable it writes into a scratch directory and removes it.
    [Fact]
    public void should_write_a_snapshot_that_reads_back_unchanged()
    {
        var requested = Environment.GetEnvironmentVariable(ConformanceEvidenceExport.DirectoryVariable);
        var directory = string.IsNullOrWhiteSpace(requested)
            ? Directory.CreateTempSubdirectory("headless-conformance-").FullName
            : requested;

        try
        {
            var path = ConformanceEvidenceExport.WriteSnapshot(directory);

            var written = JsonNode.Parse(File.ReadAllText(path));
            JsonNode.DeepEquals(written, ConformanceEvidenceExport.CreateSnapshot()).Should().BeTrue();
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("QueueRoundTrip", "Redis", TransportConformanceScenario.QueueRoundTrip, true)]
    [InlineData(" QueueRoundTrip ", "NATS", TransportConformanceScenario.QueueRoundTrip, true)]
    [InlineData("QueueRoundTrip", "Redis", TransportConformanceScenario.HeaderRoundTrip, false)]
    [InlineData("QueueRoundTrip", "InMemory", TransportConformanceScenario.QueueRoundTrip, false)]
    [InlineData("QueueRoundTrip", SimulatedBroker.ProviderName, TransportConformanceScenario.QueueRoundTrip, false)]
    [InlineData("QueueRoundTrip", "NoSuchProvider", TransportConformanceScenario.QueueRoundTrip, false)]
    [InlineData(null, "Redis", TransportConformanceScenario.QueueRoundTrip, false)]
    [InlineData("", "Redis", TransportConformanceScenario.QueueRoundTrip, false)]
    public void should_drill_only_real_broker_leaves_of_the_named_scenario(
        string? drilledScenario,
        string provider,
        TransportConformanceScenario scenario,
        bool expected
    )
    {
        ConformanceDriftDrill.IsDrilled(drilledScenario, provider, scenario).Should().Be(expected);
    }
}
