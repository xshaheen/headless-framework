// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// The base constructor builds the model, so model-shaping state must reach the context before the base constructor
/// runs: a primary-constructor parameter or a field initializer does, a derived constructor body does not.
/// </summary>
public sealed class ModelBuildTimingTests : TestBase
{
    [Fact]
    public async Task should_build_the_model_from_a_primary_constructor_parameter()
    {
        // given
        var options = new DbContextOptionsBuilder<ParameterTableDbContext>().UseSqlite("Data Source=:memory:").Options;

        // when
        await using var db = new ParameterTableDbContext(options, "rows_from_parameter");

        // then
        db.Model.FindEntityType(typeof(TimingRow))!.GetTableName().Should().Be("rows_from_parameter");
    }

    [Fact]
    public async Task should_build_the_model_before_a_derived_constructor_body_runs()
    {
        // given
        var options = new DbContextOptionsBuilder<BodyTableDbContext>().UseSqlite("Data Source=:memory:").Options;

        // when
        await using var db = new BodyTableDbContext(options);

        // then: the body's assignment came after the model was built and cached
        db.Model.FindEntityType(typeof(TimingRow))!.GetTableName().Should().Be(BodyTableDbContext.FallbackTable);
    }

    public sealed class TimingRow : Entity<Guid>;

    public sealed class ParameterTableDbContext(DbContextOptions<ParameterTableDbContext> options, string table)
        : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TimingRow>().ToTable(table);
        }
    }

    public sealed class BodyTableDbContext : HeadlessDbContext
    {
        public const string FallbackTable = "rows_before_body";

        private readonly string? _table;

#pragma warning disable IDE0290 // False positive: the assignment must stay in a constructor body, the case under test.
        public BodyTableDbContext(DbContextOptions<BodyTableDbContext> options)
            : base(options)
        {
            _table = "rows_from_body";
        }
#pragma warning restore IDE0290

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TimingRow>().ToTable(_table ?? FallbackTable);
        }
    }
}
