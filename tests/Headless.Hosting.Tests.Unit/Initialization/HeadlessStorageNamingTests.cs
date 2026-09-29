// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;

namespace Tests.Initialization;

public sealed class HeadlessStorageNamingTests : TestBase
{
    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", StorageNamingStyle.SnakeCase)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", StorageNamingStyle.PascalCase)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", StorageNamingStyle.PascalCase)]
    [InlineData("npgsql.entityframeworkcore.postgresql", StorageNamingStyle.PascalCase)]
    [InlineData(null, StorageNamingStyle.PascalCase)]
    public void should_pick_the_style_of_the_ef_provider(string? providerName, StorageNamingStyle expected)
    {
        HeadlessStorageNaming.ForProvider(providerName).Should().Be(expected);
    }

    [Theory]
    [InlineData("FeatureValues", "feature_values")]
    [InlineData("TenantId", "tenant_id")]
    [InlineData("Id", "id")]
    [InlineData("IsVisibleToClients", "is_visible_to_clients")]
    [InlineData("HTTPStatus", "http_status")]
    [InlineData("ProviderKey2", "provider_key2")]
    [InlineData("already_snake", "already_snake")]
    public void should_convert_pascal_names_to_snake_case(string pascalName, string expected)
    {
        HeadlessStorageNaming.Apply(StorageNamingStyle.SnakeCase, pascalName).Should().Be(expected);
    }

    [Theory]
    [InlineData("FeatureValues")]
    [InlineData("TenantId")]
    public void should_keep_pascal_names_unchanged_for_pascal_case(string pascalName)
    {
        HeadlessStorageNaming.Apply(StorageNamingStyle.PascalCase, pascalName).Should().Be(pascalName);
    }

    [Fact]
    public void should_reject_an_empty_name()
    {
        var act = () => HeadlessStorageNaming.Apply(StorageNamingStyle.SnakeCase, " ");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(StorageNamingStyle.PascalCase, "FeatureValues", "PK_FeatureValues")]
    [InlineData(StorageNamingStyle.SnakeCase, "feature_values", "pk_feature_values")]
    [InlineData(StorageNamingStyle.SnakeCase, "MyValues", "pk_MyValues")]
    public void should_build_primary_key_names_from_the_resolved_table(
        StorageNamingStyle style,
        string tableName,
        string expected
    )
    {
        HeadlessStorageNaming.PrimaryKeyName(style, tableName).Should().Be(expected);
    }

    [Theory]
    [InlineData(StorageNamingStyle.PascalCase, "FeatureValues", "IX_FeatureValues_Name_ProviderName_NullProviderKey")]
    [InlineData(
        StorageNamingStyle.SnakeCase,
        "feature_values",
        "ix_feature_values_name_provider_name_null_provider_key"
    )]
    [InlineData(StorageNamingStyle.SnakeCase, "MyValues", "ix_MyValues_name_provider_name_null_provider_key")]
    public void should_build_index_names_from_converted_parts_and_a_verbatim_table(
        StorageNamingStyle style,
        string tableName,
        string expected
    )
    {
        HeadlessStorageNaming
            .IndexName(style, tableName, "Name", "ProviderName", "NullProviderKey")
            .Should()
            .Be(expected);
    }

    [Fact]
    public void should_use_a_configured_name_verbatim_and_convert_only_the_default()
    {
        HeadlessStorageNaming
            .Resolve("MyValues", StorageNamingStyle.SnakeCase, "FeatureValues")
            .Should()
            .Be("MyValues");
        HeadlessStorageNaming
            .Resolve(null, StorageNamingStyle.SnakeCase, "FeatureValues")
            .Should()
            .Be("feature_values");
        HeadlessStorageNaming
            .Resolve(null, StorageNamingStyle.PascalCase, "FeatureValues")
            .Should()
            .Be("FeatureValues");
    }

    [Theory]
    [InlineData(StorageNamingStyle.SnakeCase, "values", "ix_values_name_provider_name_null_provider_key")]
    [InlineData(StorageNamingStyle.PascalCase, "Values", "IX_Values_Name_ProviderName_NullProviderKey")]
    public void should_return_the_longest_derived_name(StorageNamingStyle style, string table, string expected)
    {
        HeadlessStorageNaming
            .LongestDerivedName(style, table, ["Name"], ["Name", "ProviderName", "NullProviderKey"], ["ProviderKey"])
            .Should()
            .Be(expected);
    }

    [Fact]
    public void should_return_the_primary_key_when_the_table_has_no_index()
    {
        HeadlessStorageNaming
            .LongestDerivedName(StorageNamingStyle.SnakeCase, "values_table")
            .Should()
            .Be("pk_values_table");
    }

    [Theory]
    [InlineData(55, true)] // ix_ + 55 + _name = 63 bytes
    [InlineData(56, false)]
    public void should_bound_a_table_name_by_its_longest_derived_postgresql_name(int length, bool valid)
    {
        // given
        var validator = new InlineValidator<NamedTable>();
        validator.RuleFor(x => x.TableName).FitsDerivedPostgreSqlNames(["Name"]);

        // when
        var result = validator.Validate(new NamedTable(new string('t', length)));

        // then
        result.IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_leave_a_missing_table_name_to_the_identifier_rule(string? tableName)
    {
        // given
        var validator = new InlineValidator<NamedTable>();
        validator.RuleFor(x => x.TableName).FitsDerivedPostgreSqlNames(["Name"]);

        // when
        var result = validator.Validate(new NamedTable(tableName));

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_name_the_derived_name_that_does_not_fit()
    {
        // given
        var table = new string('t', 60);
        var validator = new InlineValidator<NamedTable>();
        validator.RuleFor(x => x.TableName).FitsDerivedPostgreSqlNames(["Name"]);

        // when
        var result = validator.Validate(new NamedTable(table));

        // then
        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain($"ix_{table}_name");
    }

    public sealed record NamedTable(string? TableName);
}
