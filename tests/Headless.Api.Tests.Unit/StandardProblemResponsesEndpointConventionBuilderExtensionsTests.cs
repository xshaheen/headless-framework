// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Tests;

public sealed class StandardProblemResponsesEndpointConventionBuilderExtensionsTests : TestBase
{
    [Fact]
    public void should_mark_the_endpoint_on()
    {
        // given
        var conventions = new RecordingConventionBuilder();

        // when
        var returned = conventions.WithStandardProblemResponses();

        // then
        returned.Should().BeSameAs(conventions);
        conventions.Build().Metadata.OfType<StandardProblemResponsesAttribute>().Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public void should_mark_the_endpoint_off()
    {
        // given
        var conventions = new RecordingConventionBuilder();

        // when
        conventions.WithoutStandardProblemResponses();

        // then
        conventions.Build().Metadata.OfType<StandardProblemResponsesAttribute>().Single().Enabled.Should().BeFalse();
    }

    private sealed class RecordingConventionBuilder : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];

        public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);

        public RouteEndpointBuilder Build()
        {
            var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);

            foreach (var convention in _conventions)
            {
                convention(builder);
            }

            return builder;
        }
    }
}
