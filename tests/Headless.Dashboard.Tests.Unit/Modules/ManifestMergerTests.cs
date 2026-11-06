// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;
using Tests.Modules.Fixtures;

namespace Tests.Modules;

public sealed class ManifestMergerTests : TestBase
{
    private static readonly Version _Shell14 = new(1, 4);

    [Theory]
    [InlineData("sdk_2_0", "2.0")]
    [InlineData("sdk_1_6", "1.6")]
    public void should_refuse_the_module_naming_both_versions_when_its_sdk_major_differs_or_is_newer(
        string fixture,
        string builtAgainst
    )
    {
        var manifest = ManifestMerger.Merge(
            [new FixtureModule("diag", fixture)],
            _Shell14,
            new ContributionOverrides()
        );

        var module = manifest.Modules.Should().ContainSingle().Subject;
        module.IsRefused.Should().BeTrue();
        module.Refusal!.BuiltAgainst.Should().Be(builtAgainst);
        module.Refusal.Message.Should().Contain(builtAgainst).And.Contain("1.4");
    }

    [Fact]
    public void should_load_the_module_when_it_was_built_against_an_older_minor_of_the_shell_major()
    {
        var manifest = ManifestMerger.Merge(
            [new FixtureModule("diag", "sdk_1_2")],
            _Shell14,
            new ContributionOverrides()
        );

        var module = manifest.Modules.Should().ContainSingle().Subject;
        module.IsRefused.Should().BeFalse();
        module
            .Manifest!.Sidebar.SelectMany(section => section.Entries)
            .Select(entry => entry.Id)
            .Should()
            .Equal("diag.nav.runs");
        module.ServerCommands.Select(command => command.Id).Should().Equal("diag.requeue");
    }

    [Fact]
    public void should_keep_a_refused_module_as_a_refusal_entry_with_no_sidebar_routes_or_commands()
    {
        var manifest = ManifestMerger.Merge(
            [new FixtureModule("diag", "sdk_2_0"), new FixtureModule("audit", "audit")],
            _Shell14,
            new ContributionOverrides()
        );

        manifest.Modules.Select(module => module.Id).Should().Equal("diag", "audit");

        var refused = manifest.Modules[0];
        refused.DisplayName.Should().Be("DIAG");
        refused.Refusal.Should().NotBeNull();
        refused.Manifest.Should().BeNull();
        refused.ServerCommands.Should().BeEmpty();

        // Its server contributions are withheld from the rest of the dashboard too.
        manifest.RelatedGroupIds.Should().Equal("audit.run-related");
        manifest.Modules[1].IsRefused.Should().BeFalse();
    }

    [Fact]
    public void should_carry_each_module_product_and_its_sidebar_entry_ids_as_known_contributions()
    {
        var manifest = ManifestMerger.Merge(
            [new FixtureModule("diag", "diag"), new FixtureModule("audit", "audit")],
            new Version(1, 0, 0),
            new ContributionOverrides()
        );

        manifest.Modules.Select(module => module.Product?.Id).Should().Equal("diagnostics", "diagnostics");
        manifest.KnownContributionIds.Should().Contain(["diag.nav.runs", "diag.nav.settings", "audit.nav.entries"]);
    }

    [Fact]
    public void should_fail_the_start_naming_the_product_when_a_loaded_module_names_an_unknown_product()
    {
        var act = () =>
            ManifestMerger.Merge(
                [new FixtureModule("diag", "bad_product")],
                new Version(1, 0, 0),
                new ContributionOverrides()
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*'diag'*'billing'*jobs, messaging*");
    }

    [Fact]
    public void should_drop_a_sidebar_section_when_the_host_disables_every_entry_in_it()
    {
        var manifest = ManifestMerger.Merge(
            [new FixtureModule("audit", "audit")],
            new Version(1, 0, 0),
            new ContributionOverrides().Disable("audit.nav.entries")
        );

        manifest.Modules.Should().ContainSingle().Subject.Manifest!.Sidebar.Should().BeEmpty();
    }
}
