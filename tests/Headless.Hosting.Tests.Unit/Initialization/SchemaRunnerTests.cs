// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;

namespace Tests.Initialization;

public sealed class SchemaRunnerTests : TestBase
{
    [Fact]
    public void should_use_the_bare_feature_as_its_identity_when_every_name_is_the_default()
    {
        SchemaContribution.FeatureId("Sequences", ("sequences", "sequences"), (null, "other")).Should().Be("Sequences");
    }

    [Fact]
    public void should_append_each_configured_name_to_the_identity_when_it_differs_from_the_default()
    {
        SchemaContribution
            .FeatureId("Features", ("my_values", "feature_values"), (null, "feature_definitions"), ("groups", "g"))
            .Should()
            .Be("Features:my_values:groups");
    }

    [Fact]
    public void should_reject_a_contribution_that_repeats_a_step_version()
    {
        var act = () => _Contribution("A", "public", [new("1", "a", "x"), new("1", "b", "y")]);

        act.Should().Throw<ArgumentException>().WithMessage("*repeat a step version*");
    }

    [Fact]
    public void should_default_to_no_host_state_tables()
    {
        var contribution = _Contribution("A", "public", [new("1", "a", "x")]);

        contribution.HostStateTables.Should().BeEmpty();
    }

    [Fact]
    public void should_keep_declared_host_state_tables()
    {
        var contribution = new SchemaContribution(
            "A",
            FakeDialect.Instance,
            () => throw new NotSupportedException("No connection is opened."),
            "public",
            [new("1", "a", "x")],
            hostStateTables: ["definitions", "groups"]
        );

        contribution.HostStateTables.Should().Equal("definitions", "groups");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_a_blank_host_state_table(string name)
    {
        var act = () =>
            new SchemaContribution(
                "A",
                FakeDialect.Instance,
                () => throw new NotSupportedException("No connection is opened."),
                "public",
                [new("1", "a", "x")],
                hostStateTables: [name]
            );

        act.Should().Throw<ArgumentException>().WithMessage("*host-state table*");
    }

    [Fact]
    public void should_reject_a_contribution_without_steps()
    {
        var act = () => _Contribution("A", "public", []);

        act.Should().Throw<ArgumentException>().WithMessage("*at least one step*");
    }

    [Fact]
    public void should_give_the_same_checksum_to_sql_that_differs_only_in_line_endings()
    {
        var unix = new SchemaStep("1", "d", "CREATE TABLE t (id int);\nCREATE INDEX i ON t (id);");
        var windows = new SchemaStep("1", "other description", "CREATE TABLE t (id int);\r\nCREATE INDEX i ON t (id);");

        SchemaRunnerChecksum.ForStep(windows).Should().Be(SchemaRunnerChecksum.ForStep(unix));
        SchemaRunnerChecksum.ForStep(unix).Should().HaveLength(64);
    }

    [Fact]
    public void should_give_a_different_checksum_to_changed_sql()
    {
        var before = new SchemaStep("1", "d", "CREATE TABLE t (id int NOT NULL);");
        var after = new SchemaStep("1", "d", "CREATE TABLE t (id int NULL);");

        SchemaRunnerChecksum.ForStep(after).Should().NotBe(SchemaRunnerChecksum.ForStep(before));
    }

    [Fact]
    public void should_export_the_history_table_then_each_step_followed_by_its_history_row_in_registration_order()
    {
        var runner = new SchemaRunner([
            _Contribution("Beta", "s1", [new("1", "beta one", "BETA 1;"), new("2", "beta two", "BETA 2;")]),
            _Contribution("Alpha", "s1", [new("1", "it's alpha", "ALPHA 1;")]),
        ]);

        var script = runner.ExportScript(FakeDialect.Instance);

        var order = new[]
        {
            "HISTORY s1",
            "BETA 1;",
            "INSERT s1 'Beta' '1'",
            "BETA 2;",
            "INSERT s1 'Beta' '2'",
            "ALPHA 1;",
        };
        order.Select(fragment => script.IndexOf(fragment, StringComparison.Ordinal)).Should().BeInAscendingOrder();
        order.Should().OnlyContain(fragment => script.Contains(fragment, StringComparison.Ordinal));
        script.Should().Contain("'it''s alpha'", "literals are quoted by doubling single quotes");
        script.Should().Contain($"'{SchemaRunnerChecksum.ForStep(new("1", "x", "ALPHA 1;"))}'");
    }

    [Fact]
    public void should_export_the_same_text_every_time()
    {
        var runner = new SchemaRunner([_Contribution("Alpha", "s1", [new("1", "a", "ALPHA 1;")])]);

        runner.ExportScript(FakeDialect.Instance).Should().Be(runner.ExportScript(FakeDialect.Instance));
    }

    [Fact]
    public void should_export_only_the_requested_dialect_and_separate_batches_with_its_separator()
    {
        var other = new FakeDialect("Other", separator: null);
        var runner = new SchemaRunner([
            _Contribution("Alpha", "s1", [new("1", "a", "ALPHA 1;")]),
            _Contribution("Gamma", "s2", [new("1", "g", "GAMMA 1;")], other),
        ]);

        var script = runner.ExportScript(FakeDialect.Instance);

        script.Should().Contain("ALPHA 1;").And.NotContain("GAMMA 1;");
        script
            .Split('\n')
            .Count(line => string.Equals(line.Trim(), "GO", StringComparison.Ordinal))
            .Should()
            .Be(3, "history, step, and history row are three batches");
    }

    [Fact]
    public async Task should_export_an_export_only_contribution_but_never_open_its_database_when_applying_or_verifying()
    {
        var runner = new SchemaRunner([_Contribution("Alpha", "s1", [new("1", "a", "ALPHA 1;")], exportOnly: true)]);

        runner.ExportScript(FakeDialect.Instance).Should().Contain("ALPHA 1;").And.Contain("INSERT s1 'Alpha' '1'");
        // The contribution's connection factory throws, so reaching its database would fail these calls.
        (await runner.ApplyAsync(AbortToken))
            .AppliedSteps.Should()
            .BeEmpty();
        (await runner.VerifyAsync(AbortToken)).Should().BeEmpty();
        (await runner.RunAsync(SchemaRunnerMode.Verify, AbortToken)).Should().BeEmpty();
    }

    private static SchemaContribution _Contribution(
        string feature,
        string schema,
        IReadOnlyList<SchemaStep> steps,
        ISchemaDialect? dialect = null,
        bool exportOnly = false
    )
    {
        return new SchemaContribution(
            feature,
            dialect ?? FakeDialect.Instance,
            () => throw new NotSupportedException("Export never opens a connection."),
            schema,
            steps,
            exportOnly: exportOnly
        );
    }

    private sealed class FakeDialect(string name = "Fake", string? separator = "GO") : ISchemaDialect
    {
        public static readonly FakeDialect Instance = new();

        public string Name => name;

        public string? ScriptBatchSeparator => separator;

        public string TryAcquireLockSql => throw new NotSupportedException();

        public string ReleaseLockSql => throw new NotSupportedException();

        public string DatabaseIdentity(DbConnection connection) => throw new NotSupportedException();

        public DbParameter CreateStringParameter(string name, string value) => throw new NotSupportedException();

        public string HistoryTableSql(string schema) => $"HISTORY {schema}";

        public string ReadHistorySql(string schema) => throw new NotSupportedException();

        public string InsertHistorySql(string schema) =>
            $"INSERT {schema} @Feature @StepVersion @Description @Checksum";

        public bool IsAlreadyCreatedRace(Exception exception) => false;

        public bool IsObjectNotFound(Exception exception) => false;
    }
}
