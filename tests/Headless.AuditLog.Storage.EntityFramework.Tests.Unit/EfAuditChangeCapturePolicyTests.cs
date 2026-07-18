// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

public sealed partial class EfAuditChangeCaptureTests
{
    [Fact]
    public async Task finalized_model_exposes_primitive_audit_policy_annotations()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = db.Model.FindEntityType(typeof(Order));
            var product = db.Model.FindEntityType(typeof(Product));
            var internalLog = db.Model.FindEntityType(typeof(InternalLog));

            order.Should().NotBeNull();
            product.Should().NotBeNull();
            internalLog.Should().NotBeNull();

            order!.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited)?.Value.Should().Be(true);
            product!.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited).Should().BeNull();
            internalLog!.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited)?.Value.Should().Be(false);

            order
                .FindProperty(nameof(Order.LastComputedAt))!
                .FindAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsExcluded)
                ?.Value.Should()
                .Be(true);
            order
                .FindProperty(nameof(Order.Email))!
                .FindAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsSensitive)
                ?.Value.Should()
                .Be(true);
            order
                .FindProperty(nameof(Order.Phone))!
                .FindAnnotation(HeadlessAuditPolicyAnnotations.PropertySensitiveStrategy)
                ?.Value.Should()
                .Be((int)SensitiveDataStrategy.Exclude);
        }
    }

    [Fact]
    public async Task sensitive_shadow_property_is_captured_from_model_metadata()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new ShadowPropertyOrder { Id = Guid.NewGuid() };
            db.ShadowPropertyOrders.Add(order);
            db.Entry(order).Property<string>("Secret").CurrentValue = "classified";

            var result = _Capture(_CreateSut(), db);

            result.Should().ContainSingle();
            result[0].NewValues.Should().ContainKey("Secret").WhoseValue.Should().Be("***");
        }
    }

    [Fact]
    public async Task derived_entity_inherits_nearest_base_policy_and_can_override_it()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.InheritedAuditEntities.Add(new InheritedAuditEntity { Id = Guid.NewGuid(), Name = "included" });
            db.ExcludedDerivedAuditEntities.Add(
                new ExcludedDerivedAuditEntity { Id = Guid.NewGuid(), Name = "excluded" }
            );

            var result = _Capture(_CreateSut(), db);

            result.Should().ContainSingle();
            result[0].EntityType.Should().Be(typeof(InheritedAuditEntity).FullName);
        }
    }

    [Fact]
    public async Task owned_entry_uses_derived_owner_policy_override()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.ExcludedDerivedAuditEntities.Add(
                new ExcludedDerivedAuditEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "excluded",
                    Details = new OwnedAuditDetails { Value = "private" },
                }
            );

            var result = _Capture(_CreateSut(), db);

            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task owned_entry_policy_resolution_uses_exact_entry_when_clr_instance_is_ambiguous()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
#pragma warning disable EF1001 // Regression coverage requires constructing the ambiguous internal EF state directly.
            var details = new OwnedAuditDetails { Value = "private" };
            var owner = new ExcludedDerivedAuditEntity { Id = Guid.NewGuid(), Name = "excluded" };
            db.ExcludedDerivedAuditEntities.Add(owner);

            var stateManager = db.GetDependencies().StateManager;
            var internalEntries = db
                .Model.GetEntityTypes()
                .Where(type => type.ClrType == typeof(OwnedAuditDetails))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .Select(type => stateManager.GetOrCreateEntry(details, type))
                .ToArray();

            internalEntries.Should().HaveCount(2);

            foreach (var internalEntry in internalEntries)
            {
                var ownership = internalEntry.EntityType.FindOwnership()!;
                internalEntry[ownership.Properties.Single()] = owner.Id;
            }

            stateManager.TryGetEntry(details, throwOnNonUniqueness: false).Should().BeNull();

            var method = typeof(EfAuditChangeCapture).GetMethod(
                "_GetPolicyEntityType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            )!;
            var policyEntityType = (IEntityType)method.Invoke(null, [internalEntries[0].ToEntityEntry()])!;

            policyEntityType.ClrType.Should().Be<ExcludedDerivedAuditEntity>();
#pragma warning restore EF1001
        }
    }

    [Fact]
    public async Task owned_entry_entity_filter_uses_derived_owner_type()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.InheritedAuditEntities.Add(
                new InheritedAuditEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "filtered",
                    Details = new OwnedAuditDetails { Value = "private" },
                }
            );

            var result = _Capture(
                _CreateSut(opts => opts.EntityFilter = type => type == typeof(InheritedAuditEntity)),
                db
            );

            result.Should().BeEmpty();
        }
    }
}
