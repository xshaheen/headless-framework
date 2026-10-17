// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;

namespace Tests;

/// <summary>The operation transformers driven directly, for inputs a generated document does not produce.</summary>
public sealed class OperationTransformerTests : TestBase
{
    [Fact]
    public async Task should_keep_an_operation_deprecated_when_an_earlier_transformer_already_deprecated_it()
    {
        var operation = new OpenApiOperation { Deprecated = true };

        await new ApiExtraInformationTransformer().TransformAsync(
            operation,
            TransformerContexts.Operation(TransformerContexts.Description("GET", "legacy", new ObsoleteAttribute())),
            AbortToken
        );

        operation.Deprecated.Should().BeTrue();
    }

    [Fact]
    public async Task should_leave_parameters_without_an_api_explorer_description_and_keep_existing_descriptions()
    {
        var undescribed = new OpenApiParameter { Name = "trace", In = ParameterLocation.Header };
        var described = new OpenApiParameter
        {
            Name = "term",
            In = ParameterLocation.Query,
            Description = "Set by an earlier transformer.",
        };
        var operation = new OpenApiOperation { Parameters = [undescribed, described] };
        var description = TransformerContexts.Description("GET", "search");
        description.ParameterDescriptions.Add(new ApiParameterDescription { Name = "term" });

        await new ApiExtraInformationTransformer().TransformAsync(
            operation,
            TransformerContexts.Operation(description),
            AbortToken
        );

        undescribed.Description.Should().BeNull();
        described.Description.Should().Be("Set by an earlier transformer.");
    }

    [Fact]
    public async Task should_add_the_if_match_header_beside_a_query_parameter_of_the_same_name()
    {
        var query = new OpenApiParameter { Name = "If-Match", In = ParameterLocation.Query };
        var operation = new OpenApiOperation { Parameters = [query] };

        await new IfMatchTransformer().TransformAsync(
            operation,
            TransformerContexts.Operation(
                TransformerContexts.Description("PUT", "orders", new RequireIfMatchAttribute())
            ),
            AbortToken
        );

        operation.Parameters.Should().HaveCount(2);
        query.Required.Should().BeFalse();
        var header = operation.Parameters![1].Should().BeOfType<OpenApiParameter>().Subject;
        header.Name.Should().Be("If-Match");
        header.In.Should().Be(ParameterLocation.Header);
        header.Required.Should().BeTrue();
        header.Description.Should().Be("One strong entity tag returned by the resource.");
        header.Schema!.Type.Should().Be(JsonSchemaType.String);
        operation.Responses!["400"].Description.Should().Be("The supplied If-Match value was invalid.");
        operation.Responses["428"].Description.Should().Be("The required If-Match precondition was not supplied.");
    }

    [Fact]
    public async Task should_add_401_and_403_beside_the_responses_the_operation_already_declares()
    {
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } },
        };
        var context = TransformerContexts.Operation(
            TransformerContexts.Description("GET", "orders", new AuthorizeAttribute { Roles = "admin" })
        );

        await new UnauthorizedResponseTransformer().TransformAsync(operation, context, AbortToken);
        await new ForbiddenResponseTransformer().TransformAsync(operation, context, AbortToken);

        operation.Responses.Keys.Should().Equal("200", "401", "403");
        operation
            .Responses["401"]
            .Description.Should()
            .Be("Unauthorized - The user has not supplied the necessary credentials to access the resource.");
        operation
            .Responses["403"]
            .Description.Should()
            .Be("Forbidden - The user does not have the necessary permissions to access the resource.");
    }

    [Fact]
    public void should_trim_role_names_and_skip_empty_ones()
    {
        var description = TransformerContexts.Description(
            "GET",
            "orders",
            new AuthorizeAttribute { Roles = " admin , ,ops" },
            new AuthorizeAttribute { Roles = "ops" }
        );

        EndpointAuthorization.GetRoles(description).Should().Equal("admin", "ops");
    }

    [Fact]
    public async Task should_name_the_permission_denied_and_tenant_required_examples()
    {
        var permissionDenied = new JsonObject { ["status"] = 403, ["detail"] = "denied" };
        var mediaType = new OpenApiMediaType { Example = permissionDenied };
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["403"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                    {
                        ["application/problem+json"] = mediaType,
                    },
                },
            },
        };

        await new TenantRequiredExampleTransformer().TransformAsync(
            operation,
            TransformerContexts.Operation(
                TransformerContexts.Description("GET", "tenant", new RequireTenantAttribute(), new AuthorizeAttribute())
            ),
            AbortToken
        );

        mediaType.Example.Should().BeNull();
        mediaType.Examples!.Keys.Should().Equal("forbidden", "tenantRequired");
        mediaType.Examples["forbidden"].Summary.Should().Be("Permission denied");
        mediaType.Examples["forbidden"].Value.Should().BeSameAs(permissionDenied);
        mediaType.Examples["tenantRequired"].Summary.Should().Be("Tenant context required");
        ((string?)mediaType.Examples["tenantRequired"].Value!["error"]!["code"]).Should().Be("g:tenant_required");
    }
}
