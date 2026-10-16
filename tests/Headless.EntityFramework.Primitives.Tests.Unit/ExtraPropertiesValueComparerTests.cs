// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

public sealed class ExtraPropertiesValueComparerTests : TestBase
{
    private readonly ExtraPropertiesValueComparer _comparer = new();

    [Fact]
    public void should_treat_separately_boxed_equal_values_as_equal()
    {
        ExtraProperties left = new() { ["count"] = 5, ["at"] = new DateOnly(2026, 1, 1) };
        ExtraProperties right = new() { ["count"] = 5, ["at"] = new DateOnly(2026, 1, 1) };

        _comparer.Equals(left, right).Should().BeTrue();
        _comparer.GetHashCode(left).Should().Be(_comparer.GetHashCode(right));
    }

    [Fact]
    public void should_treat_equal_strings_with_different_instances_as_equal()
    {
        ExtraProperties left = new() { ["name"] = new string('a', 3) };
        ExtraProperties right = new() { ["name"] = new string('a', 3) };

        _comparer.Equals(left, right).Should().BeTrue();
    }

    [Fact]
    public void should_ignore_insertion_order()
    {
        ExtraProperties left = new() { ["a"] = 1, ["b"] = 2 };
        ExtraProperties right = new() { ["b"] = 2, ["a"] = 1 };

        _comparer.Equals(left, right).Should().BeTrue();
        _comparer.GetHashCode(left).Should().Be(_comparer.GetHashCode(right));
    }

    [Fact]
    public void should_detect_changed_value_and_extra_key()
    {
        ExtraProperties original = new() { ["a"] = 1 };

        _comparer.Equals(original, new ExtraProperties { ["a"] = 2 }).Should().BeFalse();
        _comparer.Equals(original, new ExtraProperties { ["a"] = 1, ["b"] = 1 }).Should().BeFalse();
        _comparer.Equals(original, new ExtraProperties { ["c"] = 1 }).Should().BeFalse();
    }

    [Fact]
    public void should_detect_a_change_of_offset_or_kind_alone()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        _comparer
            .Equals(
                new ExtraProperties { ["at"] = instant },
                new ExtraProperties { ["at"] = instant.ToOffset(TimeSpan.FromHours(2)) }
            )
            .Should()
            .BeFalse();
        _comparer
            .Equals(
                new ExtraProperties { ["at"] = utc },
                new ExtraProperties { ["at"] = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) }
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_not_update_when_loaded_value_is_reassigned_the_same_value()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var commands = 0;
        var options = new DbContextOptionsBuilder<BagDbContext>()
            .UseSqlite(connection)
            .LogTo(_ => commands++, (id, _) => id == RelationalEventId.CommandExecuted, DbContextLoggerOptions.None)
            .Options;

        await using (var seed = new BagDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(AbortToken);
            seed.Add(new BagRow { Bag = new ExtraProperties { ["count"] = 5 } });
            await seed.SaveChangesAsync(AbortToken);
        }

        await using var db = new BagDbContext(options);
        var row = await db.Set<BagRow>().SingleAsync(AbortToken);

        // when: the stored int reads back as long, and the application writes the same number as an int
        row.Bag["count"] = 5;
        commands = 0;
        await db.SaveChangesAsync(AbortToken);

        // then
        commands.Should().Be(0);
    }

    public sealed class BagRow
    {
        public int Id { get; set; }

        public ExtraProperties Bag { get; set; } = [];
    }

    private sealed class BagDbContext(DbContextOptions<BagDbContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BagRow>();
        }
    }
}
