// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

public sealed class DateTimeNormalizationTests : TestBase
{
    [Fact]
    public void should_normalize_date_time_properties_declared_on_a_derived_type()
    {
        var derived = _Model().FindEntityType(typeof(DerivedEvent))!;

        derived
            .FindProperty(nameof(DerivedEvent.ShippedAt))!
            .GetValueConverter()
            .Should()
            .BeOfType<NormalizeDateTimeValueConverter>();
        derived
            .FindProperty(nameof(DerivedEvent.DeliveredAt))!
            .GetValueConverter()
            .Should()
            .BeOfType<NullableNormalizeDateTimeValueConverter>();
    }

    [Fact]
    public void should_normalize_date_time_properties_declared_on_the_root_type()
    {
        var root = _Model().FindEntityType(typeof(BaseEvent))!;

        root.FindProperty(nameof(BaseEvent.OccurredAt))!
            .GetValueConverter()
            .Should()
            .BeOfType<NormalizeDateTimeValueConverter>();
    }

    [Fact]
    public void should_normalize_date_time_properties_declared_on_an_owned_type()
    {
        var owned = _Model()
            .FindEntityType(typeof(BaseEvent))!
            .FindNavigation(nameof(BaseEvent.Window))!
            .TargetEntityType;

        owned
            .FindProperty(nameof(DeliveryWindow.OpensAt))!
            .GetValueConverter()
            .Should()
            .BeOfType<NormalizeDateTimeValueConverter>();
        owned
            .FindProperty(nameof(DeliveryWindow.ClosesAt))!
            .GetValueConverter()
            .Should()
            .BeOfType<NullableNormalizeDateTimeValueConverter>();
    }

    private static IModel _Model()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = new EventsDbContext(
            new DbContextOptionsBuilder<EventsDbContext>().UseSqlite(connection).Options
        );

        return db.Model;
    }

    public class BaseEvent : Entity<Guid>
    {
        public DateTime OccurredAt { get; set; }

        public DeliveryWindow Window { get; set; } = new();
    }

    public sealed class DeliveryWindow
    {
        public DateTime OpensAt { get; set; }

        public DateTime? ClosesAt { get; set; }
    }

    public sealed class DerivedEvent : BaseEvent
    {
        public DateTime ShippedAt { get; set; }

        public DateTime? DeliveredAt { get; set; }
    }

    private sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<BaseEvent> Events => Set<BaseEvent>();

        public DbSet<DerivedEvent> DerivedEvents => Set<DerivedEvent>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<BaseEvent>().OwnsOne(x => x.Window);
        }
    }
}
