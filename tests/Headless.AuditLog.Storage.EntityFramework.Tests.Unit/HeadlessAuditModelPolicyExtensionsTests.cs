// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

public sealed partial class EfAuditChangeCaptureTests
{
    [Fact]
    public async Task should_report_entity_policy_from_explicit_configuration_and_default()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var options = new AuditLogOptions();

            var order = _Policy<Order>(db, options);
            order.ConfiguredIsAudited.Should().BeTrue();
            order.IsAudited.Should().BeTrue();

            var product = _Policy<Product>(db, options);
            product.ConfiguredIsAudited.Should().BeNull();
            product.IsAudited.Should().BeFalse();

            var internalLog = _Policy<InternalLog>(db, new AuditLogOptions { AuditByDefault = true });
            internalLog.ConfiguredIsAudited.Should().BeFalse();
            internalLog.IsAudited.Should().BeFalse();

            _Policy<Product>(db, new AuditLogOptions { AuditByDefault = true }).IsAudited.Should().BeTrue();
        }
    }

    [Fact]
    public async Task should_report_entity_filter_veto_only_for_audited_types()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var filtered = new List<Type>();
            var options = new AuditLogOptions
            {
                EntityFilter = type =>
                {
                    filtered.Add(type);
                    return type == typeof(Order);
                },
            };

            _Policy<Order>(db, options).IsAudited.Should().BeFalse();
            _Policy<InternalLog>(db, options).IsAudited.Should().BeFalse();
            filtered.Should().Equal(typeof(Order));
        }
    }

    [Fact]
    public async Task should_report_property_sensitivity_strategy_and_exclusion()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var options = new AuditLogOptions
            {
                SensitiveDataStrategy = SensitiveDataStrategy.Transform,
                PropertyFilter = (type, name) => type == typeof(Order) && name == nameof(Order.Amount),
            };

            var order = _Policy<Order>(db, options);

            var email = _Property(order, nameof(Order.Email));
            email.IsSensitive.Should().BeTrue();
            email.SensitiveDataStrategy.Should().Be(SensitiveDataStrategy.Transform);
            email.IsCaptured.Should().BeTrue();

            _Property(order, nameof(Order.Phone)).SensitiveDataStrategy.Should().Be(SensitiveDataStrategy.Exclude);

            var name = _Property(order, nameof(Order.CustomerName));
            name.IsSensitive.Should().BeFalse();
            name.SensitiveDataStrategy.Should().BeNull();
            name.IsCaptured.Should().BeTrue();

            _Property(order, nameof(Order.LastComputedAt)).IsCaptured.Should().BeFalse();
            _Property(order, nameof(Order.Amount)).IsCaptured.Should().BeFalse();

            var managed = _Policy<FrameworkManagedOrder>(db, options);
            _Property(managed, nameof(FrameworkManagedOrder.CreatedAt)).IsCaptured.Should().BeFalse();

            var shadow = _Property(_Policy<ShadowPropertyOrder>(db, options), "Secret");
            shadow.IsSensitive.Should().BeTrue();
            shadow.IsCaptured.Should().BeTrue();
        }
    }

    [Fact]
    public async Task should_report_owned_and_derived_types_through_their_owner_and_base()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var options = new AuditLogOptions();

            var address = db
                .Model.GetEntityTypes()
                .Single(type => type.ClrType == typeof(Address))
                .GetAuditPolicy(options);

            address.PolicyEntityType.ClrType.Should().Be<Customer>();
            address.IsAudited.Should().BeTrue();
            var city = _Property(address, nameof(Address.City));
            city.IsSensitive.Should().BeTrue();
            city.IsCaptured.Should().BeFalse();

            var inherited = _Policy<InheritedAuditEntity>(db, options);
            inherited.ConfiguredIsAudited.Should().BeTrue();
            inherited.IsAudited.Should().BeTrue();

            _Policy<ExcludedDerivedAuditEntity>(db, options).IsAudited.Should().BeFalse();
        }
    }

    [Fact]
    public async Task should_return_one_policy_per_entity_type_matching_capture()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var options = new AuditLogOptions();

            var policies = db.Model.GetAuditPolicies(options);

            policies.Select(policy => policy.EntityType).Should().Equal(db.Model.GetEntityTypes());

            // What the reader reports is what a save captures.
            db.Orders.Add(new Order { Id = Guid.NewGuid(), Email = "a@b.c" });
            db.Products.Add(new Product { Id = Guid.NewGuid(), Name = "p" });
            db.InternalLogs.Add(new InternalLog { Id = Guid.NewGuid(), Message = "m" });

            var captured = _Capture(_CreateSut(), db).Select(entry => entry.EntityType).ToList();

            foreach (var type in new[] { typeof(Order), typeof(Product), typeof(InternalLog) })
            {
                captured
                    .Contains(type.FullName)
                    .Should()
                    .Be(policies.Single(policy => policy.EntityType.ClrType == type).IsAudited);
            }
        }
    }

    [Fact]
    public void should_reject_null_arguments()
    {
        var model = (IReadOnlyModel)new ModelBuilder().Model;

        var nullModel = () => ((IReadOnlyModel)null!).GetAuditPolicies(new AuditLogOptions());
        var nullOptions = () => model.GetAuditPolicies(null!);
        var nullEntityType = () => ((IReadOnlyEntityType)null!).GetAuditPolicy(new AuditLogOptions());

        nullModel.Should().Throw<ArgumentNullException>();
        nullOptions.Should().Throw<ArgumentNullException>();
        nullEntityType.Should().Throw<ArgumentNullException>();
    }

    private static AuditEntityPolicy _Policy<TEntity>(DbContext db, AuditLogOptions options)
    {
        return db.Model.FindEntityType(typeof(TEntity))!.GetAuditPolicy(options);
    }

    private static AuditPropertyPolicy _Property(AuditEntityPolicy policy, string name)
    {
        return policy.Properties.Single(property => property.Property.Name == name);
    }
}
