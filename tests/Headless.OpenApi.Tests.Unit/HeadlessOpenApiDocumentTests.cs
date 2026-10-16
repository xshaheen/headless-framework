// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
using Headless.Testing.Tests;

namespace Tests;

public sealed class HeadlessOpenApiDocumentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync() =>
        OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi();
            },
            SampleApi.Map,
            "v1",
            AbortToken
        );

    [Fact]
    public async Task should_emit_openapi_31_with_the_product_title_and_response_conventions()
    {
        var document = await _GetDocumentAsync();

        ((string?)document["openapi"]).Should().StartWith("3.1.");
        ((string?)document["info"]?["title"]).Should().Be("Headless OpenApi Tests Unit API");
        ((string?)document["info"]?["description"]).Should().Contain("## Success Response");
    }

    [Fact]
    public async Task should_describe_numbers_as_plain_json_numbers()
    {
        var document = await _GetDocumentAsync();
        var order = document.Schema("OrderView")["properties"]!;

        order["count"]!.Types().Should().Equal("integer");
        order["count"]!["pattern"].Should().BeNull();
        order["optionalCount"]!.Types().Should().BeEquivalentTo(["null", "integer"]);
        order["total"]!.Types().Should().Equal("number");
        order["total"]!["pattern"].Should().BeNull();
        order["items"]!["items"]!.Types().Should().Equal("integer");

        var pageIndex = document.Operation("/orders", "get").Parameter("pageIndex");
        pageIndex["schema"]!.Types().Should().Equal("integer");
        pageIndex["schema"]!["pattern"].Should().BeNull();

        var pathId = document.Operation("/orders/{id}", "get").Parameter("id");
        pathId["schema"]!["pattern"].Should().BeNull();
    }

    [Fact]
    public async Task should_require_every_non_nullable_property()
    {
        var document = await _GetDocumentAsync();

        document
            .Schema("OrderView")
            .Required()
            .Should()
            .BeEquivalentTo(["count", "total", "plain", "price", "month", "account", "status", "items"]);
        document.Schema("ProductView").Required().Should().BeEquivalentTo(["name"]);
    }

    [Fact]
    public async Task should_describe_headless_primitives_as_their_underlying_json_type()
    {
        var document = await _GetDocumentAsync();
        var order = document.Schema("OrderView")["properties"]!;

        order["price"]!.Types().Should().Equal("number");
        ((string?)order["price"]!["format"]).Should().Be("decimal");
        order["discount"]!.Types().Should().BeEquivalentTo(["null", "number"]);
        order["month"]!.Types().Should().Equal("integer");
        ((string?)order["month"]!["format"]).Should().Be("int32");
        order["account"]!.Types().Should().Equal("string");
        order["owner"]!.Types().Should().BeEquivalentTo(["null", "string"]);

        var components = document["components"]!["schemas"]!.AsObject().Select(x => x.Key);
        components.Should().NotContain(["MoneyAmount", "Month", "AccountId", "UserId"]);
    }

    [Fact]
    public async Task should_give_controller_actions_and_unnamed_endpoints_operation_ids()
    {
        var document = await _GetDocumentAsync();

        ((string?)document.Operation("/api/products/{id}", "get")["operationId"]).Should().Be("Products_Get");
        ((string?)document.Operation("/api/products", "get")["operationId"]).Should().Be("ListProducts");
        ((string?)document.Operation("/api/products/{id}", "delete")["operationId"]).Should().Be("Products_Delete");
        ((string?)document.Operation("/orders/{id}", "get")["operationId"]).Should().Be("GetOrder");
        ((string?)document.Operation("/unnamed/items/{id}", "get")["operationId"]).Should().Be("GetUnnamedItems");
    }

    [Fact]
    public async Task should_number_duplicate_operation_ids()
    {
        var document = await _GetDocumentAsync();

        string?[] ids =
        [
            (string?)document.Operation("/api/products/find/by-name", "get")["operationId"],
            (string?)document.Operation("/api/products/find/by-code", "get")["operationId"],
        ];

        ids.Should().BeEquivalentTo(["Products_Find", "Products_Find2"]);
    }

    [Fact]
    public async Task should_camel_case_query_parameters_of_complex_types()
    {
        var document = await _GetDocumentAsync();

        var minimal = document.Operation("/orders", "get")["parameters"]!.AsArray().Select(x => (string?)x!["name"]);
        minimal.Should().BeEquivalentTo(["pageIndex", "pageSize", "sortBy"]);

        var mvc = document.Operation("/api/products", "get")["parameters"]!.AsArray().Select(x => (string?)x!["name"]);
        mvc.Should().BeEquivalentTo(["pageIndex", "pageSize"]);
    }

    [Fact]
    public async Task should_add_401_and_403_from_authorization_metadata()
    {
        var document = await _GetDocumentAsync();

        _ResponseCodes(document.Operation("/orders/{id}", "get")).Should().Contain(["401", "403"]);
        _ResponseCodes(document.Operation("/api/products/{id}", "delete")).Should().Contain(["401", "403"]);

        // A plain RequireAuthorization() only checks authentication, so it cannot produce 403.
        var authenticatedOnly = _ResponseCodes(document.Operation("/orders/{id}", "put"));
        authenticatedOnly.Should().Contain("401").And.NotContain("403");

        _ResponseCodes(document.Operation("/orders", "get")).Should().NotContain(["401", "403"]);
        _ResponseCodes(document.Operation("/api/products/{id}", "get")).Should().NotContain(["401", "403"]);
    }

    [Fact]
    public async Task should_type_error_responses_with_headless_problem_details_and_examples()
    {
        var document = await _GetDocumentAsync();
        var responses = document.Operation("/orders", "post")["responses"]!;

        _ProblemSchemaRef(responses["400"]!).Should().Be("#/components/schemas/BadRequestProblemDetails");
        _ProblemSchemaRef(responses["409"]!).Should().Be("#/components/schemas/ConflictProblemDetails");
        _ProblemSchemaRef(responses["422"]!).Should().Be("#/components/schemas/UnprocessableEntityProblemDetails");
        ((int?)responses["422"]!["content"]!["application/problem+json"]!["example"]!["status"]).Should().Be(422);
        _ProblemSchemaRef(responses["429"]!).Should().Be("#/components/schemas/TooManyRequestsProblemDetails");
        document.Schema("TooManyRequestsProblemDetails").Required().Should().Contain("retryAfter");

        var authorized = document.Operation("/orders/{id}", "get")["responses"]!;
        _ProblemSchemaRef(authorized["401"]!).Should().Be("#/components/schemas/UnauthorizedProblemDetails");
        _ProblemSchemaRef(authorized["403"]!).Should().Be("#/components/schemas/ForbiddenProblemDetails");

        var notFound = document.Operation("/api/products/{id}", "get")["responses"]!["404"]!;
        _ProblemSchemaRef(notFound).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");

        // The optional descriptor stays optional and nullable; the nested type is shared, not repeated.
        var badRequest = document.Schema("BadRequestProblemDetails");
        badRequest.Required().Should().NotContain("error").And.Contain(["type", "status", "traceId", "timestamp"]);
        var error = badRequest["properties"]!["error"]!["oneOf"]!.AsArray();
        ((string?)error[0]!["type"]).Should().Be("null");
        ((string?)error[1]!["$ref"]).Should().Be("#/components/schemas/ErrorDescriptor");
    }

    [Fact]
    public async Task should_document_the_if_match_header_and_its_428()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/orders/{id}", "put");

        var ifMatch = operation.Parameter("If-Match");
        ((string?)ifMatch["in"]).Should().Be("header");
        ((bool?)ifMatch["required"]).Should().BeTrue();
        _ProblemSchemaRef(operation["responses"]!["428"]!)
            .Should()
            .Be("#/components/schemas/PreconditionRequiredProblemDetails");
        _ProblemSchemaRef(operation["responses"]!["400"]!).Should().Be("#/components/schemas/BadRequestProblemDetails");

        document.Operation("/orders/{id}", "get")["parameters"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_duplicate_an_if_match_header_the_endpoint_binds_itself()
    {
        var document = await _GetDocumentAsync();

        var headers = document.Operation("/orders/{id}/bound", "put")["parameters"]!
            .AsArray()
            .Where(x => string.Equals((string?)x!["name"], "If-Match", StringComparison.OrdinalIgnoreCase))
            .ToList();

        headers.Should().ContainSingle();
        ((bool?)headers[0]!["required"]).Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_every_problem_example_within_its_schema()
    {
        var document = await _GetDocumentAsync();
        var problems = document["components"]!["schemas"]!
            .AsObject()
            .Where(x =>
                x.Key.EndsWith("ProblemDetails", StringComparison.Ordinal)
                && !string.Equals(x.Key, "ProblemDetails", StringComparison.Ordinal)
            )
            .ToList();
        var examples = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (
            var media in document["paths"]!
                .AsObject()
                .SelectMany(path => path.Value!.AsObject())
                .SelectMany(operation => operation.Value!["responses"]!.AsObject())
                .Select(response => response.Value!["content"]?["application/problem+json"])
        )
        {
            if (media?["example"] is JsonObject example)
            {
                examples.TryAdd((string)media["schema"]!["$ref"]!, example);
            }
        }

        problems.Should().HaveCount(8);

        foreach (var (name, schema) in problems)
        {
            var example = examples[$"#/components/schemas/{name}"];
            var properties = schema!["properties"]!.AsObject().Select(x => x.Key).ToArray();

            example.Select(x => x.Key).Should().BeSubsetOf(properties, name);
            example.Select(x => x.Key).Should().Contain(schema.Required(), name);
        }
    }

    [Fact]
    public async Task should_add_the_tenant_required_example_only_where_a_tenant_is_required()
    {
        var document = await _GetDocumentAsync();

        var required = document.Operation("/tenant/orders", "get")["responses"]!["403"]!["content"]![
            "application/problem+json"
        ]!;
        required["example"].Should().BeNull();
        ((string?)required["examples"]!["tenantRequired"]!["value"]!["error"]!["code"])
            .Should()
            .Be("g:tenant_required");
        var permissionDenied = required["examples"]!["forbidden"]!["value"]!;
        var tenantRequired = required["examples"]!["tenantRequired"]!["value"]!;
        ((int?)permissionDenied["status"]).Should().Be(403);
        // The tenant example differs from the permission-denied one only in its detail and descriptor.
        ((string?)tenantRequired["instance"])
            .Should()
            .Be((string?)permissionDenied["instance"]);
        ((string?)tenantRequired["timestamp"]).Should().Be((string?)permissionDenied["timestamp"]);
        ((string?)tenantRequired["detail"]).Should().NotBe((string?)permissionDenied["detail"]);

        var optional = document.Operation("/tenant/optional", "get")["responses"]!["403"]!["content"]![
            "application/problem+json"
        ]!;
        optional["examples"].Should().BeNull();
        optional["example"].Should().NotBeNull();
    }

    [Fact]
    public async Task should_project_fluent_validation_rules_onto_schemas_and_parameters()
    {
        var document = await _GetDocumentAsync();
        var request = document.Schema("CreateOrderRequest");
        var properties = request["properties"]!;

        request.Required().Should().Contain(["name", "code"]);
        properties["name"]!.Types().Should().Equal("string");
        ((int?)properties["name"]!["minLength"]).Should().Be(2);
        ((int?)properties["name"]!["maxLength"]).Should().Be(10);
        ((string?)properties["name"]!["pattern"]).Should().Be("^[a-z]+$");
        properties["code"]!.Types().Should().Equal("string");
        ((int?)properties["code"]!["minLength"]).Should().Be(1);
        ((decimal?)properties["age"]!["minimum"]).Should().Be(18);
        ((decimal?)properties["age"]!["exclusiveMaximum"]).Should().Be(100);
        properties["age"]!["maximum"].Should().BeNull();
        ((decimal?)properties["score"]!["exclusiveMinimum"]).Should().Be(1);
        ((decimal?)properties["score"]!["exclusiveMaximum"]).Should().Be(10);
        ((string?)properties["email"]!["pattern"]).Should().Be("^[^@]+@[^@]+$");

        // RuleForEach constrains the elements, not the array.
        properties["scores"]!["exclusiveMinimum"].Should().BeNull();
        ((decimal?)properties["scores"]!["items"]!["exclusiveMinimum"]).Should().Be(0);

        // A When() clause makes the rule conditional, so it does not describe every valid request.
        properties["note"]!["maxLength"].Should().BeNull();
        properties["note"]!.Types().Should().Contain("null");
        request.Required().Should().NotContain(["note", "comment"]);

        var pageSize = document.Operation("/orders", "get").Parameter("pageSize")["schema"]!;
        ((decimal?)pageSize["minimum"]).Should().Be(1);
        ((decimal?)pageSize["maximum"]).Should().Be(100);

        var productPageSize = document.Operation("/api/products", "get").Parameter("pageSize")["schema"]!;
        ((decimal?)productPageSize["maximum"]).Should().Be(50);
    }

    [Fact]
    public async Task should_mark_obsolete_endpoints_and_deprecated_api_versions_deprecated()
    {
        var document = await _GetDocumentAsync();

        ((bool?)document.Operation("/legacy", "get")["deprecated"]).Should().BeTrue();
        ((bool?)document.Operation("/versioned/old", "get")["deprecated"]).Should().BeTrue();
        document.Operation("/versioned/current", "get")["deprecated"].Should().BeNull();
        document.Operation("/orders", "get")["deprecated"].Should().BeNull();
    }

    [Fact]
    public async Task should_publish_the_bearer_scheme_and_require_it_on_authenticated_operations()
    {
        var document = await _GetDocumentAsync();

        ((string?)document["components"]!["securitySchemes"]!["Bearer"]!["scheme"]).Should().Be("bearer");
        document.Operation("/orders/{id}", "get")["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");
        document.Operation("/api/products/{id}", "delete")["security"]!
            .ToJsonString()
            .Should()
            .Be("""[{"Bearer":["admin"]}]""");
        document.Operation("/orders", "get")["security"].Should().BeNull();
    }

    private static string[] _ResponseCodes(JsonNode operation) =>
        [.. operation["responses"]!.AsObject().Select(x => x.Key)];

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}
