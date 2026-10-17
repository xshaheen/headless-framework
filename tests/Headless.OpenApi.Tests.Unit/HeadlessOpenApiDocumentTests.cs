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
    public async Task should_be_valid_openapi()
    {
        var document = await _GetDocumentAsync();

        document.ShouldBeValidOpenApi("the sample document");
    }

    [Fact]
    public async Task should_keep_the_string_alternative_of_numbers_written_as_strings()
    {
        var document = await _GetDocumentAsync();

        var externalId = document.Schema("OrderView")["properties"]!["externalId"]!;
        externalId.Types().Should().BeEquivalentTo(["integer", "string"]);
    }

    [Fact]
    public async Task should_describe_a_nullable_component_property_as_an_optional_null_alternative()
    {
        var document = await _GetDocumentAsync();
        var order = document.Schema("OrderView");

        order.Required().Should().NotContain("shippingAddress");
        var alternatives = order["properties"]!["shippingAddress"]!["oneOf"]!.AsArray();
        alternatives.Should().HaveCount(2);
        ((string?)alternatives[0]!["type"]).Should().Be("null");
        ((string?)alternatives[1]!["$ref"]).Should().Be("#/components/schemas/Address");
        document.Schema("Address").Required().Should().Equal("street");
    }

    [Fact]
    public async Task should_share_one_schema_between_nullable_and_non_nullable_generic_arguments()
    {
        var document = await _GetDocumentAsync();

        // DataEnvelope<string> and DataEnvelope<string?> are one runtime type, so they share one component, and the
        // native generator describes an unconstrained T as nullable for both.
        string?[] references =
        [
            (string?)
                document.Operation("/envelopes/nullable", "get")["responses"]!["200"]!["content"]!["application/json"]![
                    "schema"
                ]!["$ref"],
            (string?)
                document.Operation("/envelopes/plain", "get")["responses"]!["200"]!["content"]!["application/json"]![
                    "schema"
                ]!["$ref"],
        ];
        references.Should().AllBe("#/components/schemas/DataEnvelopeOfstring");

        var envelope = document.Schema("DataEnvelopeOfstring");
        envelope["properties"]!["data"]!.Types().Should().BeEquivalentTo(["null", "string"]);
        // The constructor parameter keeps the member required even though it may be null.
        envelope.Required().Should().Equal("data");
    }

    [Fact]
    public async Task should_require_every_non_nullable_property()
    {
        var document = await _GetDocumentAsync();

        document
            .Schema("OrderView")
            .Required()
            .Should()
            .BeEquivalentTo(["count", "total", "plain", "price", "month", "account", "status", "items", "externalId"]);
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
        // A parameter sharing a segment with literal text contributes nothing, so no brace reaches the id.
        ((string?)document.Operation("/v{version}/things", "get")["operationId"])
            .Should()
            .Be("GetVThings");
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
        minimal.Should().BeEquivalentTo(["pageIndex", "pageSize", "sortBy", "tags"]);

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

        // The parameter's own schema takes the property rules; RuleForEach reaches the items, and nothing else does.
        var tags = document.Operation("/orders", "get").Parameter("tags")["schema"]!;
        tags.Types().Should().Equal("array");
        ((int?)tags["minItems"]).Should().Be(1);
        tags["minLength"].Should().BeNull();
        ((int?)tags["items"]!["maxLength"]).Should().Be(20);
        tags["items"]!["minLength"].Should().BeNull();
        tags["items"]!["minItems"].Should().BeNull();

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

    [Fact]
    public async Task should_apply_unconditionally_included_validators_and_skip_conditional_ones()
    {
        var document = await _GetDocumentAsync();
        var properties = document.Schema("RegisterRequest")["properties"]!;

        ((int?)properties["userName"]!["maxLength"]).Should().Be(30);
        properties["nickname"]!["maxLength"].Should().BeNull();
    }

    [Fact]
    public async Task should_bound_exact_and_minimum_length_rules()
    {
        var document = await _GetDocumentAsync();
        var properties = document.Schema("RegisterRequest")["properties"]!;

        ((int?)properties["pin"]!["minLength"]).Should().Be(4);
        ((int?)properties["pin"]!["maxLength"]).Should().Be(4);
        ((int?)properties["password"]!["minLength"]).Should().Be(8);
        properties["password"]!["maxLength"].Should().BeNull();
        // A minimum-length rule overrides the minLength: 1 that NotEmpty wrote before it.
        ((int?)properties["code"]!["minLength"])
            .Should()
            .Be(4);
    }

    [Fact]
    public async Task should_copy_model_metadata_descriptions_and_bound_required_flags_onto_parameters()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/api/catalog/search", "get");

        ((string?)operation.Parameter("term")["description"]).Should().Be("Text to search for.");
        ((bool?)operation.Parameter("term")["required"]).Should().NotBe(true);
        // [BindRequired] reaches the document through the native generator, which reads the same API Explorer flag.
        ((bool?)operation.Parameter("page")["required"])
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_skip_numbered_operation_ids_that_are_already_taken()
    {
        var first = await _GetDocumentAsync();
        var second = await _GetDocumentAsync();

        foreach (var document in new[] { first, second })
        {
            ((string?)document.Operation("/api/catalog/legacy", "get")["operationId"]).Should().Be("Catalog_Find2");

            string?[] finds =
            [
                (string?)document.Operation("/api/catalog/by-name", "get")["operationId"],
                (string?)document.Operation("/api/catalog/by-code", "get")["operationId"],
            ];
            finds.Should().BeEquivalentTo(["Catalog_Find", "Catalog_Find3"]);

            var ids = document["paths"]!
                .AsObject()
                .SelectMany(path => path.Value!.AsObject())
                .Select(operation => (string?)operation.Value!["operationId"])
                .ToList();
            ids.Should().OnlyHaveUniqueItems();
        }

        static string?[] Ids(JsonNode document) =>
            [
                (string?)document.Operation("/api/catalog/by-name", "get")["operationId"],
                (string?)document.Operation("/api/catalog/by-code", "get")["operationId"],
            ];

        Ids(second).Should().Equal(Ids(first));
    }

    [Theory]
    [InlineData("/policies/claims", true)]
    [InlineData("/policies/name", true)]
    [InlineData("/policies/assertion", true)]
    [InlineData("/policies/authenticated", false)]
    [InlineData("/policies/metadata-only", true)]
    public async Task should_document_inline_policy_requirements(string path, bool canForbid)
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation(path, "get");

        _ResponseCodes(operation).Should().Contain("401");

        if (canForbid)
        {
            _ResponseCodes(operation).Should().Contain("403");
        }
        else
        {
            _ResponseCodes(operation).Should().NotContain("403");
        }

        operation["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");
    }

    [Fact]
    public async Task should_document_mvc_authorization_filters()
    {
        var document = await _GetDocumentAsync();

        var status = document.Operation("/api/filter-secured/status", "get");
        _ResponseCodes(status).Should().Contain("401").And.NotContain("403");
        status["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");

        var manage = document.Operation("/api/filter-secured/manage", "get");
        _ResponseCodes(manage).Should().Contain(["401", "403"]);
        manage["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");

        var ping = document.Operation("/api/filter-secured/ping", "get");
        _ResponseCodes(ping).Should().NotContain(["401", "403"]);
        ping["security"].Should().BeNull();
    }

    private static string[] _ResponseCodes(JsonNode operation) =>
        [.. operation["responses"]!.AsObject().Select(x => x.Key)];

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}
