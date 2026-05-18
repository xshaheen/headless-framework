// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Tests.MultiTenancy;

public sealed class EndpointConventionBuilderExtensionsTests
{
    [Fact]
    public void should_define_attribute_usage_for_classes_and_methods_only()
    {
        // when
        var usage = typeof(AllowMissingTenantAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Should()
            .ContainSingle()
            .Subject.Should()
            .BeOfType<AttributeUsageAttribute>()
            .Subject;

        // then
        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Method);
        usage.Inherited.Should().BeFalse();
        usage.AllowMultiple.Should().BeFalse();
        typeof(AllowMissingTenantAttribute).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void should_attach_allow_missing_tenant_metadata_to_endpoint_builder()
    {
        // given
        var conventionBuilder = new CapturingEndpointConventionBuilder();
        var endpointBuilder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);

        // when
        var result = conventionBuilder.AllowMissingTenant();
        conventionBuilder.Apply(endpointBuilder);

        // then
        result.Should().BeSameAs(conventionBuilder);
        endpointBuilder.Metadata.OfType<AllowMissingTenantAttribute>().Should().ContainSingle();
    }

    private sealed class CapturingEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];

        public void Add(Action<EndpointBuilder> convention)
        {
            _conventions.Add(convention);
        }

        public void Finally(Action<EndpointBuilder> finallyConvention)
        {
            _conventions.Add(finallyConvention);
        }

        public void Apply(EndpointBuilder endpointBuilder)
        {
            foreach (var convention in _conventions)
            {
                convention(endpointBuilder);
            }
        }
    }
}
