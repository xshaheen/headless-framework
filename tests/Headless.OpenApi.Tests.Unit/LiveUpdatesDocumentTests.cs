// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tests;

/// <summary>
/// Live operations in the document. The endpoints carry only the metadata a live endpoint attaches, so the document
/// is shown to need no reference to the feature that streams.
/// </summary>
public sealed class LiveUpdatesDocumentTests : TestBase
{
    private const string _EventStream = "text/event-stream";
    private const string _Ndjson = "application/x-ndjson";

    private static Task<JsonNode> _GetDocumentAsync(bool withLiveEndpoints) =>
        OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
            {
                app.MapGet("/orders/{id:int}", (int id) => TypedResults.Ok(new OrderView())).AllowAnonymous();

                if (!withLiveEndpoints)
                {
                    return;
                }

                app.MapGet("/live/orders/{id:int}", (int id) => TypedResults.Ok(new OrderView()))
                    .WithMetadata(new LiveUpdatesMetadata())
                    .AllowAnonymous();
                app.MapGet("/live/names", () => TypedResults.Ok(new[] { "milk" }))
                    .WithMetadata(new LiveUpdatesMetadata())
                    .AllowAnonymous();
                app.MapGet("/live/untyped", () => Results.Ok())
                    .WithMetadata(new LiveUpdatesMetadata())
                    .AllowAnonymous();
            },
            "v1",
            AbortToken
        );

    [Fact]
    public async Task should_mark_a_live_operation_and_name_the_stream_formats_it_answers()
    {
        var document = await _GetDocumentAsync(withLiveEndpoints: true);

        document
            .Operation("/live/orders/{id}", "get")["x-headless-live"]
            .ShouldBeJson(
                """
                { "mediaTypes": ["text/event-stream", "application/x-ndjson"] }
                """
            );
    }

    [Fact]
    public async Task should_describe_a_server_sent_result_event_with_the_schema_of_the_single_response()
    {
        var document = await _GetDocumentAsync(withLiveEndpoints: true);
        var content = document.Operation("/live/orders/{id}", "get")["responses"]!["200"]!["content"]!;

        content.AsObject().Select(x => x.Key).Should().Equal("application/json", _EventStream, _Ndjson);
        content["application/json"]!["schema"].ShouldBeJson("""{ "$ref": "#/components/schemas/OrderView" }""");
        content[_EventStream]!["schema"].ShouldBeJson("""{ "$ref": "#/components/schemas/OrderView" }""");
    }

    [Fact]
    public async Task should_describe_a_newline_delimited_line_as_a_result_a_failure_or_a_heartbeat()
    {
        var document = await _GetDocumentAsync(withLiveEndpoints: true);
        var content = document.Operation("/live/orders/{id}", "get")["responses"]!["200"]!["content"]!;

        content[_Ndjson]!
            ["schema"]
            .ShouldBeJson(
                """
                {
                  "oneOf": [
                    {
                      "required": ["kind", "version", "value"],
                      "type": "object",
                      "properties": {
                        "kind": { "type": "string", "const": "result" },
                        "version": { "type": "string" },
                        "value": { "$ref": "#/components/schemas/OrderView" }
                      }
                    },
                    {
                      "required": ["kind", "version", "value"],
                      "type": "object",
                      "properties": {
                        "kind": { "type": "string", "const": "failure" },
                        "version": { "type": "string" },
                        "value": { "$ref": "#/components/schemas/HeadlessProblemDetails" }
                      }
                    },
                    {
                      "required": ["kind"],
                      "type": "object",
                      "properties": {
                        "kind": { "type": "string", "const": "heartbeat" }
                      }
                    }
                  ]
                }
                """
            );
        document.Schema("HeadlessProblemDetails")["properties"]!["status"].Should().NotBeNull();
    }

    [Fact]
    public async Task should_repeat_an_inline_success_schema_in_both_stream_formats()
    {
        var document = await _GetDocumentAsync(withLiveEndpoints: true);
        var content = document.Operation("/live/names", "get")["responses"]!["200"]!["content"]!;
        const string names = """{ "type": "array", "items": { "type": "string" } }""";

        content["application/json"]!["schema"].ShouldBeJson(names);
        content[_EventStream]!["schema"].ShouldBeJson(names);
        content[_Ndjson]!["schema"]!["oneOf"]![0]!["properties"]!["value"].ShouldBeJson(names);
    }

    [Fact]
    public async Task should_leave_the_stream_item_untyped_when_the_success_response_declares_no_body()
    {
        var document = await _GetDocumentAsync(withLiveEndpoints: true);
        var operation = document.Operation("/live/untyped", "get");
        var content = operation["responses"]!["200"]!["content"]!;

        operation["x-headless-live"].Should().NotBeNull();
        content.AsObject().Select(x => x.Key).Should().Equal(_EventStream, _Ndjson);
        content[_EventStream]!["schema"].Should().BeNull();

        var result = content[_Ndjson]!["schema"]!["oneOf"]![0]!;
        result.Required().Should().Equal("kind", "version", "value");
        result["properties"]!.AsObject().Select(x => x.Key).Should().Equal("kind", "version");
    }

    [Fact]
    public async Task should_describe_an_operation_beside_a_live_one_as_it_is_described_without_one()
    {
        var withLive = await _GetDocumentAsync(withLiveEndpoints: true);
        var without = await _GetDocumentAsync(withLiveEndpoints: false);

        var operation = withLive.Operation("/orders/{id}", "get");

        operation["x-headless-live"].Should().BeNull();
        operation["responses"]!["200"]!["content"]!.AsObject().Select(x => x.Key).Should().Equal("application/json");
        JsonNode
            .DeepEquals(operation, without.Operation("/orders/{id}", "get"))
            .Should()
            .BeTrue(operation.ToJsonString());
    }

    [Fact]
    public async Task should_add_nothing_to_the_document_of_a_host_without_live_endpoints()
    {
        var document = await _GetDocumentAsync(withLiveEndpoints: false);

        document
            .ToJsonString()
            .Should()
            .NotContain("x-headless-live")
            .And.NotContain(_EventStream)
            .And.NotContain(_Ndjson);
    }
}
