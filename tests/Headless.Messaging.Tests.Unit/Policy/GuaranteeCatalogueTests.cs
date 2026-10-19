// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Tests.Guarantees;

namespace Tests.Policy;

public sealed class GuaranteeCatalogueTests : TestBase
{
    private const string _IssueUrl = "https://github.com/xshaheen/headless-framework/issues/1";

    [Fact]
    public void should_bind_every_catalogued_guarantee_or_track_it_with_an_issue()
    {
        var bindings = GuaranteeBindings
            .DiscoverDeclared(typeof(MessagingGuarantees).Assembly, GuaranteeEvidenceTier.SharedSuite)
            .Concat(GuaranteeBindings.DiscoverDeclared(_FastTierTypes(), GuaranteeEvidenceTier.Simulator))
            .ToList();

        var errors = GuaranteeBindings.GetCatalogueErrors(bindings);

        _ReportCounts();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void should_run_every_guarantee_a_fast_tier_leaf_inherits()
    {
        var leaves = GuaranteeBindings.DiscoverLeaves(_FastTierTypes(), GuaranteeEvidenceTier.Simulator);

        GuaranteeBindings.GetLeafErrors(MessagingGuarantees.All.ToList(), leaves).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_critical_guarantee_with_no_bound_test_and_no_issue()
    {
        MessagingGuarantee[] catalogue = [_Guarantee("MSG-TEST-01", GuaranteeStatus.Bound)];

        var errors = GuaranteeBindings.GetCatalogueErrors(catalogue, [], _EmptyManifest);

        errors.Should().ContainSingle().Which.Should().Contain("MSG-TEST-01").And.Contain("no bound test");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("issues/1247")]
    [InlineData("ftp://example.com/issues/1247")]
    public void should_reject_a_no_test_yet_entry_without_an_absolute_issue_url(string? issueUrl)
    {
        MessagingGuarantee[] catalogue =
        [
            _Guarantee("MSG-TEST-01", GuaranteeStatus.NoTestYet) with
            {
                IssueUrl = issueUrl,
            },
        ];

        var errors = GuaranteeBindings.GetCatalogueErrors(catalogue, [], _EmptyManifest);

        errors.Should().ContainSingle().Which.Should().Contain("MSG-TEST-01").And.Contain("issue URL");
    }

    [Fact]
    public void should_accept_a_no_test_yet_entry_with_an_absolute_issue_url()
    {
        MessagingGuarantee[] catalogue =
        [
            _Guarantee("MSG-TEST-01", GuaranteeStatus.NoTestYet) with
            {
                IssueUrl = _IssueUrl,
            },
        ];

        GuaranteeBindings.GetCatalogueErrors(catalogue, [], _EmptyManifest).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_guarantee_attribute_naming_an_id_absent_from_the_catalogue()
    {
        MessagingGuarantee[] catalogue =
        [
            _Guarantee("MSG-TEST-01", GuaranteeStatus.NoTestYet) with
            {
                IssueUrl = _IssueUrl,
            },
        ];
        var bindings = GuaranteeBindings.DiscoverDeclared(
            [typeof(PolicyFixtures.UnknownIdTarget)],
            GuaranteeEvidenceTier.SharedSuite
        );

        var errors = GuaranteeBindings.GetCatalogueErrors(catalogue, bindings, _EmptyManifest);

        errors.Should().ContainSingle().Which.Should().Contain("MSG-UNKNOWN-01").And.Contain("not in the catalogue");
    }

    [Fact]
    public void should_reject_a_suite_guarantee_that_a_leaf_inherits_without_a_test_attribute()
    {
        MessagingGuarantee[] catalogue = [_Guarantee("MSG-TEST-01", GuaranteeStatus.Bound)];
        var leaves = GuaranteeBindings.DiscoverLeaves(
            [typeof(PolicyFixtures.LeafThatInheritsWithoutFact)],
            GuaranteeEvidenceTier.SharedSuite
        );

        var errors = GuaranteeBindings.GetLeafErrors(catalogue, leaves);

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(PolicyFixtures.LeafThatInheritsWithoutFact))
            .And.Contain("MSG-TEST-01")
            .And.Contain("not an xUnit test");
    }

    [Fact]
    public void should_accept_a_suite_guarantee_that_a_leaf_overrides_with_a_test_attribute()
    {
        MessagingGuarantee[] catalogue = [_Guarantee("MSG-TEST-01", GuaranteeStatus.Bound)];
        var leaves = GuaranteeBindings.DiscoverLeaves(
            [typeof(PolicyFixtures.LeafThatOverridesWithFact)],
            GuaranteeEvidenceTier.SharedSuite
        );

        leaves.Should().ContainSingle().Which.GuaranteeId.Should().Be("MSG-TEST-01");
        GuaranteeBindings.GetLeafErrors(catalogue, leaves).Should().BeEmpty();
    }

    [Fact]
    public void should_accept_an_overridable_suite_method_as_a_catalogue_binding()
    {
        MessagingGuarantee[] catalogue = [_Guarantee("MSG-TEST-01", GuaranteeStatus.Bound)];
        var bindings = GuaranteeBindings.DiscoverDeclared(
            [typeof(PolicyFixtures.SuiteBase)],
            GuaranteeEvidenceTier.SharedSuite
        );

        GuaranteeBindings.GetCatalogueErrors(catalogue, bindings, _EmptyManifest).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_a_real_system_guarantee_bound_only_to_simulator_tests()
    {
        MessagingGuarantee[] catalogue = [_Guarantee("MSG-TEST-01", GuaranteeStatus.Bound, requiresRealSystem: true)];
        var bindings = GuaranteeBindings.DiscoverDeclared(
            [typeof(PolicyFixtures.EvidenceTarget)],
            GuaranteeEvidenceTier.Simulator
        );

        var errors = GuaranteeBindings.GetCatalogueErrors(catalogue, bindings, _EmptyManifest);

        errors.Should().ContainSingle().Which.Should().Contain("MSG-TEST-01").And.Contain("only to simulator tests");
    }

    [Fact]
    public void should_accept_a_real_system_guarantee_bound_to_a_shared_suite()
    {
        MessagingGuarantee[] catalogue = [_Guarantee("MSG-TEST-01", GuaranteeStatus.Bound, requiresRealSystem: true)];
        var bindings = GuaranteeBindings.DiscoverDeclared(
            [typeof(PolicyFixtures.EvidenceTarget)],
            GuaranteeEvidenceTier.SharedSuite
        );

        GuaranteeBindings.GetCatalogueErrors(catalogue, bindings, _EmptyManifest).Should().BeEmpty();
    }

    [Fact]
    public void should_reject_manifest_evidence_that_no_real_broker_leaf_supports()
    {
        MessagingGuarantee[] catalogue =
        [
            _Guarantee("MSG-TEST-01", GuaranteeStatus.Bound, requiresRealSystem: true) with
            {
                ManifestScenarios = [TransportConformanceScenario.QueueOwnership],
            },
        ];
        var simulatorOnly = new Dictionary<string, TransportConformanceProfile>(StringComparer.Ordinal)
        {
            ["Simulator"] = TransportConformanceProfile
                .CreateDisabled("Simulator")
                .WithScenario(TransportConformanceScenario.QueueOwnership, ConformanceSupport.Supported),
        };

        var errors = GuaranteeBindings.GetCatalogueErrors(catalogue, [], simulatorOnly);

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("MSG-TEST-01")
            .And.Contain(nameof(TransportConformanceScenario.QueueOwnership));
    }

    [Fact]
    public void should_skip_a_test_whose_guarantee_is_unsupported()
    {
        MessagingGuarantee[] catalogue =
        [
            _Guarantee("MSG-TEST-01", GuaranteeStatus.Unsupported) with
            {
                Rationale = "the store drops the second write",
                IssueUrl = _IssueUrl,
            },
            _Guarantee("MSG-TEST-02", GuaranteeStatus.Bound),
        ];

        var skip = () => MessagingGuarantees.RequireSupport(catalogue, "MSG-TEST-01");
        var run = () => MessagingGuarantees.RequireSupport(catalogue, "MSG-TEST-02");

        skip.Should().Throw<Exception>().WithMessage($"*MSG-TEST-01*{_IssueUrl}*");
        run.Should().NotThrow();
    }

    private static IReadOnlyDictionary<string, TransportConformanceProfile> _EmptyManifest { get; } =
        new Dictionary<string, TransportConformanceProfile>(StringComparer.Ordinal);

    private static MessagingGuarantee _Guarantee(string id, GuaranteeStatus status, bool requiresRealSystem = false) =>
        new(id, GuaranteeArea.Storage, "A test guarantee.", requiresRealSystem, status);

    // The policy fixtures below are deliberately invalid, so the scans of this assembly leave them out.
    private static IEnumerable<Type> _FastTierTypes() =>
        typeof(GuaranteeCatalogueTests).Assembly.GetTypes().Where(type => !_IsPolicyFixture(type));

    private static bool _IsPolicyFixture(Type type)
    {
        for (var declaring = type.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
        {
            if (declaring == typeof(PolicyFixtures))
            {
                return true;
            }
        }

        return type == typeof(PolicyFixtures);
    }

    private static void _ReportCounts()
    {
        var output = TestContext.Current.TestOutputHelper;
        if (output is null)
        {
            return;
        }

        var all = MessagingGuarantees.All;
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Guarantees: {all.Count} catalogued, {_Count(GuaranteeStatus.Bound)} bound, "
                    + $"{_Count(GuaranteeStatus.NoTestYet)} no test yet, {_Count(GuaranteeStatus.Unsupported)} Unsupported."
            )
        );

        foreach (var area in all.GroupBy(guarantee => guarantee.Area))
        {
            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {area.Key}: {area.Count(g => g.Status == GuaranteeStatus.Bound)} bound, "
                        + $"{area.Count(g => g.Status == GuaranteeStatus.NoTestYet)} no test yet, "
                        + $"{area.Count(g => g.Status == GuaranteeStatus.Unsupported)} Unsupported."
                )
            );
        }

        int _Count(GuaranteeStatus status) => all.Count(guarantee => guarantee.Status == status);
    }

    private static class PolicyFixtures
    {
        public sealed class EvidenceTarget
        {
            [Fact]
            [Guarantee("MSG-TEST-01")]
            public void bound_test() { }
        }

        public sealed class UnknownIdTarget
        {
            [Fact]
            [Guarantee("MSG-UNKNOWN-01")]
            public void test_naming_an_unknown_guarantee() { }
        }

        public abstract class SuiteBase
        {
            [Guarantee("MSG-TEST-01")]
            public virtual Task suite_case() => Task.CompletedTask;
        }

        public sealed class LeafThatInheritsWithoutFact : SuiteBase;

        public sealed class LeafThatOverridesWithFact : SuiteBase
        {
            [Fact]
            public override Task suite_case() => base.suite_case();
        }

        // Stands in for xUnit's attribute so these fixtures are judged as tests but never discovered and run.
        [AttributeUsage(AttributeTargets.Method)]
        private sealed class FactAttribute : Attribute
        {
            public string? Skip { get; set; }
        }
    }
}
