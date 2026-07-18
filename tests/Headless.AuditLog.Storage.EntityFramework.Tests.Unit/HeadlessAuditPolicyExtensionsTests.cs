// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.AuditLog;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class HeadlessAuditPolicyExtensionsTests : TestBase
{
    [Fact]
    public void should_configure_non_generic_entity_and_property_builders()
    {
        // given
        var modelBuilder = new ModelBuilder();
        var includedEntity = modelBuilder.Entity(typeof(IncludedEntity));
        var excludedEntity = modelBuilder.Entity(typeof(ExcludedEntity));
        var excludedProperty = includedEntity.Property(nameof(IncludedEntity.Excluded));
        var sensitiveProperty = includedEntity.Property(nameof(IncludedEntity.Sensitive));

        // when
        var includedResult = includedEntity.IsAudited();
        var excludedResult = excludedEntity.ExcludeFromAudit();
        var excludedPropertyResult = excludedProperty.ExcludeFromAudit();
        var sensitivePropertyResult = sensitiveProperty.IsAuditSensitive(SensitiveDataStrategy.Exclude);

        // then
        includedResult.Should().BeSameAs(includedEntity);
        excludedResult.Should().BeSameAs(excludedEntity);
        excludedPropertyResult.Should().BeSameAs(excludedProperty);
        sensitivePropertyResult.Should().BeSameAs(sensitiveProperty);

        includedEntity.Metadata.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited)!.Value.Should().Be(true);
        excludedEntity
            .Metadata.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited)!
            .Value.Should()
            .Be(false);
        excludedProperty
            .Metadata.FindAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsExcluded)!
            .Value.Should()
            .Be(true);
        sensitiveProperty
            .Metadata.FindAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsSensitive)!
            .Value.Should()
            .Be(true);
        sensitiveProperty
            .Metadata.FindAnnotation(HeadlessAuditPolicyAnnotations.PropertySensitiveStrategy)!
            .Value.Should()
            .Be((int)SensitiveDataStrategy.Exclude);
    }

    [Fact]
    public void should_reject_undefined_property_sensitive_strategy()
    {
        // given
        var modelBuilder = new ModelBuilder();
        var property = modelBuilder.Entity(typeof(IncludedEntity)).Property(nameof(IncludedEntity.Sensitive));

        // when
        var act = () => property.IsAuditSensitive((SensitiveDataStrategy)999);

        // then
        act.Should().Throw<InvalidEnumArgumentException>();
    }

    private sealed class IncludedEntity
    {
        public int Id { get; set; }

        public string Excluded { get; set; } = string.Empty;

        public string Sensitive { get; set; } = string.Empty;
    }

    private sealed class ExcludedEntity
    {
        public int Id { get; set; }
    }
}
