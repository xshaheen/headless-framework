// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Tests;

public sealed class ApiResultResponseMetadataTests : TestBase
{
    [Fact]
    public void should_publish_the_json_success_response_and_every_mapped_problem_status()
    {
        // given
        var builder = _CreateEndpointBuilder();

        // when
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status200OK, typeof(string));

        // then
        var responses = builder.Metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        responses.Select(x => x.StatusCode).Should().Equal(200, 401, 403, 404, 409, 422);
        responses[0].Type.Should().Be<string>();
        responses[0].ContentTypes.Should().Equal("application/json");
        responses
            .Skip(1)
            .Should()
            .AllSatisfy(response =>
            {
                response.Type.Should().Be<ProblemDetails>();
                response.ContentTypes.Should().Equal("application/problem+json");
            });
    }

    [Fact]
    public void should_publish_a_bodyless_success_response_as_void()
    {
        // given
        var builder = _CreateEndpointBuilder();

        // when
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status204NoContent, successType: null);

        // then
        var success = builder.Metadata.OfType<IProducesResponseTypeMetadata>().First();
        success.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        success.Type.Should().Be(typeof(void));
        success.ContentTypes.Should().BeEmpty();
    }

    private static RouteEndpointBuilder _CreateEndpointBuilder() =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), order: 0);
}
