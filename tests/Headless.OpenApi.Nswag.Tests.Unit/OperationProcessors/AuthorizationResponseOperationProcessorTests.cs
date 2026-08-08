// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.OpenApi.Nswag.OperationProcessors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors.Contexts;

namespace Tests.OperationProcessors;

public sealed class AuthorizationResponseOperationProcessorTests
{
    private readonly UnauthorizedResponseOperationProcessor _unauthorized = new();
    private readonly ForbiddenResponseOperationProcessor _forbidden = new();

    [Fact]
    public void should_add_unauthorized_only_for_plain_authorize_metadata()
    {
        // given
        var context = _CreateContext(endpointMetadata: [new AuthorizeAttribute()]);

        // when
        _unauthorized.Process(context);
        _forbidden.Process(context);

        // then
        context.OperationDescription.Operation.Responses.Should().ContainKey("401");
        context.OperationDescription.Operation.Responses.Should().NotContainKey("403");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_add_forbidden_for_authorize_policy_or_roles(bool usePolicy)
    {
        // given
        var authorize = new AuthorizeAttribute();
        if (usePolicy)
        {
            authorize.Policy = "orders.write";
        }
        else
        {
            authorize.Roles = "admin";
        }

        var context = _CreateContext(endpointMetadata: [authorize]);

        // when
        _forbidden.Process(context);

        // then
        var response = context.OperationDescription.Operation.Responses["403"];
        response.Description.Should().Contain("necessary permissions");
    }

    [Fact]
    public void should_add_authentication_and_forbidden_responses_from_effective_policy_requirements()
    {
        // given
        var policy = new AuthorizationPolicy(
            [new DenyAnonymousAuthorizationRequirement(), new ClaimsAuthorizationRequirement("scope", ["orders"])],
            []
        );
        var filter = new FilterDescriptor(new AuthorizeFilter(policy), FilterScope.Action);
        var context = _CreateContext(filterDescriptors: [filter]);

        // when
        _unauthorized.Process(context);
        _forbidden.Process(context);

        // then
        context.OperationDescription.Operation.Responses.Keys.Should().Contain(["401", "403"]);
    }

    [Fact]
    public void should_skip_auth_responses_when_endpoint_allows_anonymous()
    {
        // given
        var policy = new AuthorizationPolicy([new DenyAnonymousAuthorizationRequirement()], []);
        var context = _CreateContext(
            endpointMetadata: [new AllowAnonymousAttribute(), new AuthorizeAttribute { Roles = "admin" }],
            filterDescriptors: [new FilterDescriptor(new AuthorizeFilter(policy), FilterScope.Action)]
        );

        // when
        _unauthorized.Process(context);
        _forbidden.Process(context);

        // then
        context.OperationDescription.Operation.Responses.Should().BeEmpty();
    }

    [Fact]
    public void should_preserve_existing_auth_responses()
    {
        // given
        var unauthorized = new OpenApiResponse { Description = "custom unauthorized" };
        var forbidden = new OpenApiResponse { Description = "custom forbidden" };
        var context = _CreateContext(endpointMetadata: [new AuthorizeAttribute { Roles = "admin" }]);
        context.OperationDescription.Operation.Responses["401"] = unauthorized;
        context.OperationDescription.Operation.Responses["403"] = forbidden;

        // when
        _unauthorized.Process(context);
        _forbidden.Process(context);

        // then
        context.OperationDescription.Operation.Responses["401"].Should().BeSameAs(unauthorized);
        context.OperationDescription.Operation.Responses["403"].Should().BeSameAs(forbidden);
    }

    [Fact]
    public void should_ignore_non_aspnet_operation_contexts()
    {
        // given
        var description = _CreateDescription();
        var context = new OperationProcessorContext(
            new OpenApiDocument(),
            description,
            typeof(object),
            _Method,
            null!,
            null!,
            null!,
            [description]
        );

        // when
        var unauthorizedResult = _unauthorized.Process(context);
        var forbiddenResult = _forbidden.Process(context);

        // then
        unauthorizedResult.Should().BeTrue();
        forbiddenResult.Should().BeTrue();
        description.Operation.Responses.Should().BeEmpty();
    }

    private static readonly MethodInfo _Method = typeof(object).GetMethod(
        nameof(ToString),
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
        Type.EmptyTypes
    )!;

    private static AspNetCoreOperationProcessorContext _CreateContext(
        IList<object>? endpointMetadata = null,
        IList<FilterDescriptor>? filterDescriptors = null
    )
    {
        var description = _CreateDescription();
        var context = new AspNetCoreOperationProcessorContext(
            new OpenApiDocument(),
            description,
            typeof(object),
            _Method,
            null!,
            null!,
            null!,
            [description]
        )
        {
            ApiDescription = new ApiDescription
            {
                ActionDescriptor = new ActionDescriptor
                {
                    EndpointMetadata = endpointMetadata ?? [],
                    FilterDescriptors = filterDescriptors ?? [],
                },
            },
        };

        return context;
    }

    private static OpenApiOperationDescription _CreateDescription()
    {
        return new()
        {
            Operation = new OpenApiOperation(),
            Path = "/orders",
            Method = "GET",
        };
    }
}
