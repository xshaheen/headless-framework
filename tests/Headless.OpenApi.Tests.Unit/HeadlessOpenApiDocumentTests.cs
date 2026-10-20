// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Idempotency;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

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
        ((string?)document.Operation("/unnamed/items/{id}", "get")["operationId"]).Should().Be("GetUnnamedItemsById");
        // A parameter sharing a segment with literal text contributes nothing, so no brace reaches the id.
        ((string?)document.Operation("/v{version}/things", "get")["operationId"])
            .Should()
            .Be("GetVThings");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_tell_a_list_from_a_get_by_id_whatever_the_mapping_order(bool mapByIdFirst)
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
            {
                if (mapByIdFirst)
                {
                    _MapThingById(app);
                    _MapThings(app);
                }
                else
                {
                    _MapThings(app);
                    _MapThingById(app);
                }

                app.MapGet(
                        "/orders/{orderId:guid}/items/{itemId?}",
                        (Guid orderId, string? itemId) => TypedResults.Ok()
                    )
                    .AllowAnonymous();
                app.MapGet("/files/{**path}", (string path) => TypedResults.Ok(path)).AllowAnonymous();
                app.MapGet("/pages/{index:int=1}", (int index) => TypedResults.Ok(index)).AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        var ids = document["paths"]!
            .AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => (string?)operation.Value!["operationId"]))
            .ToList();

        ids.Should()
            .BeEquivalentTo(
                "GetVThings",
                "GetVThingsById",
                "GetOrdersByOrderIdItemsByItemId",
                "GetFilesByPath",
                "GetPagesByIndex"
            );

        static void _MapThings(WebApplication app) =>
            app.MapGet("/v{version}/things", (string version) => TypedResults.Ok(version)).AllowAnonymous();

        static void _MapThingById(WebApplication app) =>
            app.MapGet("/v{version}/things/{id:int}", (string version, int id) => TypedResults.Ok(id)).AllowAnonymous();
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
    public async Task should_document_the_api_result_contract_of_mvc_actions_returning_api_action_results()
    {
        var document = await _GetDocumentAsync();

        // The controller requires no authorization, so no 401 or 403: the result type no longer lists them for every
        // action, and only the authorization facts decide them.
        var valued = document.Operation("/api/invoices/{id}", "get")["responses"]!;
        _ResponseCodes(document.Operation("/api/invoices/{id}", "get"))
            .Should()
            .BeEquivalentTo(["200", "400", "404", "409", "422"]);
        ((string?)valued["200"]!["content"]!["application/json"]!["schema"]!["$ref"])
            .Should()
            .Be("#/components/schemas/ProductView");
        _ProblemSchemaRef(valued["404"]!).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");
        _ProblemSchemaRef(valued["409"]!).Should().Be("#/components/schemas/ConflictProblemDetails");
        _ProblemSchemaRef(valued["422"]!).Should().Be("#/components/schemas/UnprocessableEntityProblemDetails");

        var unit = document.Operation("/api/invoices/{id}", "delete")["responses"]!;
        _ResponseCodes(document.Operation("/api/invoices/{id}", "delete"))
            .Should()
            .BeEquivalentTo(["204", "400", "404", "409", "422"]);
        unit["204"]!["content"].Should().BeNull();
        _ProblemSchemaRef(unit["404"]!).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");
        document["components"]!["schemas"]!.AsObject().Should().NotContainKey("ApiActionResultOfProductView");
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

        problems.Should().HaveCount(15);

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

    [Fact]
    public async Task should_add_403_and_the_tenant_example_where_a_tenant_is_required_under_the_default_policy()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/tenant/plain", "get");

        _ResponseCodes(operation).Should().Equal("200", "401", "403", "404", "409", "422");
        operation["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");
        var forbidden = operation["responses"]!["403"]!["content"]!["application/problem+json"]!;
        ((string?)forbidden["schema"]!["$ref"]).Should().Be("#/components/schemas/ForbiddenProblemDetails");
        forbidden["examples"]!.AsObject().Select(x => x.Key).Should().Equal("forbidden", "tenantRequired");
    }

    [Fact]
    public async Task should_add_the_rate_limiter_rejection_with_its_retry_after_header()
    {
        var document = await _GetDocumentAsync();
        var limited = document.Operation("/limited", "get");

        _ResponseCodes(limited).Should().Equal("200", "429", "404", "409", "422");
        var rejection = limited["responses"]!["429"]!;
        ((string?)rejection["description"]).Should().Be("The request was rejected by a rate limiter. Retry later.");
        _ProblemSchemaRef(rejection).Should().Be("#/components/schemas/TooManyRequestsProblemDetails");
        rejection["headers"]!
            .ToJsonString()
            .Should()
            .Be(
                """{"Retry-After":{"description":"The number of seconds to wait before retrying the request.","required":true,"schema":{"type":"integer","format":"int32"}}}"""
            );

        _ResponseCodes(document.Operation("/limited/disabled", "get")).Should().Equal("200", "404", "409", "422");
        _ResponseCodes(document.Operation("/orders", "get")).Should().NotContain("429");
    }

    [Fact]
    public async Task should_type_declared_timeout_size_server_and_availability_problems()
    {
        var document = await _GetDocumentAsync();
        var responses = document.Operation("/errors", "get")["responses"]!;

        _ProblemSchemaRef(responses["408"]!).Should().Be("#/components/schemas/RequestTimeoutProblemDetails");
        _ProblemSchemaRef(responses["413"]!).Should().Be("#/components/schemas/PayloadTooLargeProblemDetails");
        _ProblemSchemaRef(responses["500"]!).Should().Be("#/components/schemas/InternalServerErrorProblemDetails");
        _ProblemSchemaRef(responses["501"]!).Should().Be("#/components/schemas/NotImplementedProblemDetails");
        _ProblemSchemaRef(responses["503"]!).Should().Be("#/components/schemas/ServiceUnavailableProblemDetails");
        responses["503"]!["headers"]!
            .ToJsonString()
            .Should()
            .Be(
                """{"Retry-After":{"description":"The number of seconds to wait before retrying the request.","schema":{"type":"integer","format":"int32"}}}"""
            );
        responses["500"]!["headers"].Should().BeNull();

        var unavailable = document.Schema("ServiceUnavailableProblemDetails");
        unavailable.Required().Should().NotContain(["retryAfter", "error"]).And.Contain(["detail", "traceId"]);
        unavailable["properties"]!["retryAfter"]!.Types().Should().BeEquivalentTo(["null", "integer"]);
        document.Schema("InternalServerErrorProblemDetails")["properties"]!["error"].Should().BeNull();
    }

    [Fact]
    public async Task should_document_the_if_match_conflict_and_the_entity_tag_header()
    {
        var document = await _GetDocumentAsync();

        var update = document.Operation("/orders/{id}", "put");
        _ResponseCodes(update).Should().BeEquivalentTo(["204", "400", "401", "404", "409", "422", "428"]);
        _ProblemSchemaRef(update["responses"]!["409"]!).Should().Be("#/components/schemas/ConflictProblemDetails");
        ((string?)update["responses"]!["409"]!["description"])
            .Should()
            .Be("The If-Match entity tag no longer matches the resource (g:concurrency_failure).");
        update["responses"]!["204"]!["headers"].Should().BeNull();

        document.Operation("/orders/{id}", "get")["responses"]!["200"]!["headers"]!
            .ToJsonString()
            .Should()
            .Be(
                """{"ETag":{"description":"The strong entity tag of the returned representation. Send it back in If-Match to update it.","schema":{"type":"string"}}}"""
            );
        document.Operation("/orders", "get")["responses"]!["200"]!["headers"].Should().BeNull();
    }

    [Fact]
    public async Task should_type_the_responses_of_the_validator_filter()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/filtered", "post");

        // The 413 is the server's body size limit, which applies to every operation with a body.
        _ResponseCodes(operation).Should().BeEquivalentTo(["200", "400", "404", "409", "413", "422"]);
        _ProblemSchemaRef(operation["responses"]!["400"]!).Should().Be("#/components/schemas/BadRequestProblemDetails");
        _ProblemSchemaRef(operation["responses"]!["422"]!)
            .Should()
            .Be("#/components/schemas/UnprocessableEntityProblemDetails");
    }

    [Fact]
    public async Task should_describe_a_declared_validation_problem_with_its_own_shape()
    {
        var document = await _GetDocumentAsync();
        var badRequest = document.Operation("/validated", "post")["responses"]!["400"]!["content"]![
            "application/problem+json"
        ]!;

        ((string?)badRequest["schema"]!["$ref"]).Should().Be("#/components/schemas/BadRequestValidationProblemDetails");
        var expected = JsonNode.Parse(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"instance":"/public/some-endpoint","errors":{"name":["The name field is required."],"email":["The email field is not a valid e-mail address."]},"traceId":"<trace-id>","buildNumber":"<version>","commitNumber":"<commit>","timestamp":"2024-01-01T12:00:00+00:00"}"""
        );
        JsonNode.DeepEquals(badRequest["example"], expected).Should().BeTrue(badRequest["example"]?.ToJsonString());

        var schema = document.Schema("BadRequestValidationProblemDetails");
        schema
            .Required()
            .Should()
            .BeEquivalentTo([
                "type",
                "title",
                "status",
                "instance",
                "errors",
                "traceId",
                "buildNumber",
                "commitNumber",
                "timestamp",
            ]);
        schema["properties"]!["errors"]!
            .ToJsonString()
            .Should()
            .Be(
                """{"type":"object","additionalProperties":{"type":"array","items":{"type":"string"}},"description":"The validation messages keyed by the name of the member that failed."}"""
            );

        // The native component the declaration published is replaced, not left behind unreferenced.
        document["components"]!["schemas"]!["HttpValidationProblemDetails"].Should().BeNull();
    }

    [Fact]
    public async Task should_describe_primitive_route_and_query_parameters_as_their_underlying_type()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/accounts/{account}", "get");

        // The primitive's XML summary may ride along as the schema description; the type is what matters.
        var account = operation.Parameter("account")["schema"]!.AsObject();
        account.Select(x => x.Key).Should().BeSubsetOf(["type", "description"]);
        account.Types().Should().Equal("string");

        var month = operation.Parameter("month")["schema"]!.AsObject();
        month.Select(x => x.Key).Should().BeSubsetOf(["type", "format", "description"]);
        month.Types().Should().Equal("integer");
        ((string?)month["format"]).Should().Be("int32");
    }

    [Fact]
    public async Task should_document_the_required_idempotency_key_and_the_responses_the_middleware_writes()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/payments", "post");

        var key = operation.Parameter("Idempotency-Key");
        ((string?)key["in"]).Should().Be("header");
        ((bool?)key["required"]).Should().BeTrue();
        ((string?)key["schema"]!["type"]).Should().Be("string");

        var responses = operation["responses"]!;
        _ResponseCodes(operation).Should().BeEquivalentTo(["200", "400", "404", "409", "413", "422"]);
        _ProblemSchemaRef(responses["400"]!).Should().Be("#/components/schemas/BadRequestProblemDetails");
        ((string?)responses["400"]!["description"]).Should().Contain("g:idempotency_key_required");
        _ProblemSchemaRef(responses["409"]!).Should().Be("#/components/schemas/ConflictProblemDetails");
        ((string?)responses["409"]!["description"])
            .Should()
            .Contain("g:idempotency_in_flight")
            .And.NotContain("g:idempotency_key_reused");
        _ProblemSchemaRef(responses["422"]!).Should().Be("#/components/schemas/UnprocessableEntityProblemDetails");
        ((string?)responses["422"]!["description"]).Should().Contain("g:idempotency_key_reused");
        _ProblemSchemaRef(responses["413"]!).Should().Be("#/components/schemas/PayloadTooLargeProblemDetails");

        responses["409"]!["headers"]!.AsObject().Select(x => x.Key).Should().Equal("Retry-After");
        responses["200"]!["headers"]!.AsObject().Select(x => x.Key).Should().Equal("Idempotent-Replayed");
        ((bool?)responses["200"]!["headers"]!["Idempotent-Replayed"]!["required"]).Should().NotBe(true);
        responses["400"]!["headers"].Should().BeNull();
    }

    [Fact]
    public async Task should_document_metadata_that_controller_conventions_attach()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(TransfersController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            // MVC's API Explorer reads the action descriptor, which never sees these conventions.
            app => app.MapControllers().RequireIdempotencyKey().RequireAuthorization(),
            "v1",
            AbortToken
        );

        var post = document.Operation("/mvc/transfers", "post");
        post["parameters"]
            .ShouldBeJson(
                """
                [
                  {
                    "name": "Idempotency-Key",
                    "in": "header",
                    "description": "A client-generated key, unique per operation and at most 255 characters, that makes retrying the request safe: a retry with the same key and body replays the first response instead of running the operation again.",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ]
                """
            );
        _ResponseCodes(post).Should().BeEquivalentTo(["204", "400", "401", "404", "409", "413", "422"]);
        post["responses"]!["204"]!
            ["headers"]
            .ShouldBeJson(
                """
                {
                  "Idempotent-Replayed": {
                    "description": "true when the response is the stored response of an earlier request with the same key, replayed instead of running the operation again.",
                    "schema": { "type": "string" }
                  }
                }
                """
            );
        post["security"].ShouldBeJson("""[{ "Bearer": [] }]""");

        // GET is never an idempotency method, so only the authorization convention shows.
        var get = document.Operation("/mvc/transfers", "get");
        get["parameters"].Should().BeNull();
        _ResponseCodes(get).Should().BeEquivalentTo(["200", "401", "404", "409", "422"]);
    }

    [Fact]
    public async Task should_follow_the_endpoint_idempotency_overrides()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/payments/{id}", "put");

        // The handler binds the header itself, so it is declared once and stays optional.
        var headers = operation["parameters"]!
            .AsArray()
            .Where(x => string.Equals((string?)x!["name"], "Idempotency-Key", StringComparison.Ordinal))
            .ToList();
        headers.Should().ContainSingle();
        ((bool?)headers[0]!["required"]).Should().NotBe(true);

        _ResponseCodes(operation).Should().BeEquivalentTo(["204", "400", "404", "409", "422"]);
        var responses = operation["responses"]!;
        ((string?)responses["400"]!["description"])
            .Should()
            .Contain("g:idempotency_key_malformed")
            .And.NotContain("g:idempotency_key_required");
        ((string?)responses["409"]!["description"])
            .Should()
            .Contain("g:idempotency_in_flight_timeout")
            .And.Contain("g:idempotency_key_reused");
        responses["204"]!["headers"]!.AsObject().Select(x => x.Key).Should().Equal("Idempotent-Replayed");

        // A route group's requirement and the endpoint's own override compose into one description.
        var entry = document.Operation("/ledger/entries", "post");
        entry["parameters"]!.AsArray().Should().ContainSingle();
        ((bool?)entry.Parameter("Idempotency-Key")["required"]).Should().BeTrue();
        _ResponseCodes(entry).Should().BeEquivalentTo(["204", "400", "404", "409", "413", "422"]);

        // GET is never an idempotency method, so the middleware passes the request through untouched.
        var read = document.Operation("/payments/{id}", "get");
        read["parameters"]!.AsArray().Select(x => (string?)x!["name"]).Should().Equal("id");
        // The 400 is the route value that may fail to bind, not an idempotency response.
        _ResponseCodes(read).Should().Equal("200", "400", "404", "409", "422");
    }

    [Fact]
    public async Task should_use_the_application_idempotency_header_name()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.Configure<HttpIdempotencyOptions>(options => options.HeaderName = "X-Request-Key");
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
                app.MapPost("/transfers", () => TypedResults.NoContent())
                    // The middleware reads the application-level name before it resolves endpoint overrides.
                    .WithIdempotency(options => options.HeaderName = "Ignored-Key")
                    .RequireIdempotencyKey()
                    .AllowAnonymous(),
            "v1",
            AbortToken
        );

        var operation = document.Operation("/transfers", "post");
        ((bool?)operation.Parameter("X-Request-Key")["required"]).Should().BeTrue();
        operation["parameters"]!.AsArray().Should().ContainSingle();
        ((string?)operation["responses"]!["400"]!["description"]).Should().Contain("X-Request-Key");
    }

    [Fact]
    public async Task should_render_http_contract_metadata_as_headers_and_typed_responses()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/contract", "get");

        var language = operation.Parameter("Accept-Language");
        ((string?)language["in"]).Should().Be("header");
        ((bool?)language["required"]).Should().NotBe(true);
        ((string?)language["description"]).Should().Be("The preferred language of the response.");

        // A header the handler binds is reused, not repeated, and becomes required.
        var source = operation["parameters"]!
            .AsArray()
            .Where(x => string.Equals((string?)x!["name"], "X-Request-Source", StringComparison.Ordinal))
            .ToList();
        source.Should().ContainSingle();
        ((bool?)source[0]!["required"]).Should().BeTrue();

        var responses = operation["responses"]!;
        // The 400 is the bound X-Request-Source header, which may fail to bind.
        _ResponseCodes(operation).Should().BeEquivalentTo(["200", "400", "404", "409", "422"]);
        ((string?)responses["404"]!["description"]).Should().Be("No resource matches the request.");
        _ProblemSchemaRef(responses["404"]!).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");

        responses["200"]!["headers"]!
            .ToJsonString()
            .Should()
            .Be("""{"Content-Language":{"description":"The language of the response.","schema":{"type":"string"}}}""");
        responses["404"]!["headers"].Should().BeNull();
    }

    private static string[] _ResponseCodes(JsonNode operation) => operation["responses"]!.ExactStatusCodes();

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}

[NonController]
[ApiController]
[Route("mvc/transfers")]
public sealed class TransfersController : ControllerBase
{
    [HttpPost]
    public NoContent Post() => TypedResults.NoContent();

    [HttpGet]
    public Ok<string> Get() => TypedResults.Ok("transfers");
}
