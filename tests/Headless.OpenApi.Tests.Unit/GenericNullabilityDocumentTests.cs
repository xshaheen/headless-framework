// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Pins how a member typed by a generic type parameter is published: <c>G&lt;X&gt;</c> and <c>G&lt;X?&gt;</c> are one
/// runtime type, so only the use site can say whether the member may be null.
/// </summary>
public sealed class GenericNullabilityDocumentTests : TestBase
{
    private const string _Envelope = "#/components/schemas/DataEnvelopeOfEnvelopeUser";
    private const string _NullableEnvelope = "#/components/schemas/DataEnvelopeOfNullableEnvelopeUser";

    [Fact]
    public async Task should_publish_separate_envelopes_for_nullable_and_non_nullable_mvc_action_results()
    {
        var document = await _GetMvcDocumentAsync();

        document.ShouldBeValidOpenApi("the MVC envelopes");
        _ResponseReference(document, "/mvc/envelopes/user").Should().Be(_Envelope);
        _ResponseReference(document, "/mvc/envelopes/maybe-user").Should().Be(_NullableEnvelope);
        // A typeof() carries no annotation, so it reads as non-nullable, the way the generator reads an oblivious type.
        _ResponseReference(document, "/mvc/envelopes/declared").Should().Be(_Envelope);

        _ShouldBeNonNullableUser(document.Schema("DataEnvelopeOfEnvelopeUser"));
        _ShouldBeNullableUser(document.Schema("DataEnvelopeOfNullableEnvelopeUser"));
    }

    [Fact]
    public async Task should_resolve_a_json_request_body_from_the_annotation_of_its_parameter()
    {
        var document = await _GetMvcDocumentAsync();

        var body = document.Operation("/mvc/envelopes/maybe-user", "post")["requestBody"]!["content"]![
            "application/json"
        ]!["schema"]!;
        ((string?)body["$ref"]).Should().Be(_NullableEnvelope);
    }

    [Fact]
    public async Task should_publish_separate_envelopes_for_nullable_and_non_nullable_minimal_api_results()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
            {
                app.MapGet(
                        "/envelopes/user",
                        () => new Headless.Primitives.DataEnvelope<EnvelopeUser>(new EnvelopeUser("Ada"))
                    )
                    .AllowAnonymous();
                app.MapGet(
                        "/envelopes/maybe-user",
                        Results<Ok<Headless.Primitives.DataEnvelope<EnvelopeUser?>>, NotFound> () =>
                            TypedResults.NotFound()
                    )
                    .AllowAnonymous();
                app.MapGet("/envelopes/count", () => new Headless.Primitives.DataEnvelope<int>(1)).AllowAnonymous();
                app.MapGet("/envelopes/maybe-count", () => new Headless.Primitives.DataEnvelope<int?>(null))
                    .AllowAnonymous();
                app.MapGet("/envelopes/only-nullable", Headless.Primitives.DataEnvelope<EnvelopeOrder?> () => new(null))
                    .AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("the Minimal API envelopes");
        _ResponseReference(document, "/envelopes/user").Should().Be(_Envelope);
        _ResponseReference(document, "/envelopes/maybe-user").Should().Be(_NullableEnvelope);
        _ShouldBeNonNullableUser(document.Schema("DataEnvelopeOfEnvelopeUser"));
        _ShouldBeNullableUser(document.Schema("DataEnvelopeOfNullableEnvelopeUser"));

        // A Nullable<int> argument is part of the runtime type, which the default naming used to drop: both envelopes
        // were published as DataEnvelopeOfint, and the second one reused the first one's non-nullable schema.
        _ResponseReference(document, "/envelopes/count").Should().Be("#/components/schemas/DataEnvelopeOfint");
        _ResponseReference(document, "/envelopes/maybe-count")
            .Should()
            .Be("#/components/schemas/DataEnvelopeOfNullableint");
        document.Schema("DataEnvelopeOfint")["properties"]!["data"]!.Types().Should().Equal("integer");
        document.Schema("DataEnvelopeOfNullableint")["properties"]!["data"]!
            .Types()
            .Should()
            .BeEquivalentTo(["null", "integer"]);

        // A component only the nullable use referred to is not left behind with the wrong nullability.
        _ResponseReference(document, "/envelopes/only-nullable")
            .Should()
            .Be("#/components/schemas/DataEnvelopeOfNullableEnvelopeOrder");
        document["components"]!["schemas"]!["DataEnvelopeOfEnvelopeOrder"].Should().BeNull();
    }

    [Fact]
    public async Task should_resolve_generic_members_from_the_annotations_of_the_type_that_uses_them()
    {
        var document = await _GetMvcDocumentAsync();

        var holder = document.Schema("EnvelopeHolder");
        ((string?)holder["properties"]!["current"]!["$ref"]).Should().Be(_Envelope);
        ((string?)holder["properties"]!["previous"]!["$ref"]).Should().Be(_NullableEnvelope);
        holder.Required().Should().BeEquivalentTo(["current", "previous"]);
    }

    [Fact]
    public async Task should_carry_the_argument_into_nested_generic_and_inherited_members()
    {
        var document = await _GetMvcDocumentAsync();

        _ResponseReference(document, "/mvc/pairs/user").Should().Be("#/components/schemas/PairOfEnvelopeUser");
        _ResponseReference(document, "/mvc/pairs/maybe-user")
            .Should()
            .Be("#/components/schemas/PairOfNullableEnvelopeUser");

        var pair = document.Schema("PairOfEnvelopeUser");
        ((string?)pair["properties"]!["first"]!["$ref"]).Should().Be("#/components/schemas/EnvelopeUser");
        ((string?)pair["properties"]!["inner"]!["$ref"]).Should().Be(_Envelope);
        // A member declared T? may be null whatever the argument is.
        _ShouldBeNullableReference(pair["properties"]!["fallback"]!, "#/components/schemas/EnvelopeUser");
        pair.Required().Should().BeEquivalentTo(["first", "inner"]);

        var nullablePair = document.Schema("PairOfNullableEnvelopeUser");
        _ShouldBeNullableReference(nullablePair["properties"]!["first"]!, "#/components/schemas/EnvelopeUser");
        ((string?)nullablePair["properties"]!["inner"]!["$ref"]).Should().Be(_NullableEnvelope);
        // A required member stays required where it may be null.
        nullablePair.Required().Should().BeEquivalentTo(["first", "inner"]);

        // A member a generic base type declares follows the argument the derived type passes it.
        _ResponseReference(document, "/mvc/pages/maybe-user")
            .Should()
            .Be("#/components/schemas/NamedPageOfNullableEnvelopeUser");
        _ShouldBeNullableReference(
            document.Schema("NamedPageOfNullableEnvelopeUser")["properties"]!["head"]!,
            "#/components/schemas/EnvelopeUser"
        );
        ((string?)document.Schema("NamedPageOfEnvelopeUser")["properties"]!["head"]!["$ref"])
            .Should()
            .Be("#/components/schemas/EnvelopeUser");
    }

    [Fact]
    public async Task should_reuse_the_plain_component_when_a_nullable_argument_changes_no_member()
    {
        var document = await _GetMvcDocumentAsync();

        // Batch<T> uses T only as a collection element, which is published non-nullable either way.
        _ResponseReference(document, "/mvc/batches/user").Should().Be("#/components/schemas/BatchOfEnvelopeUser");
        _ResponseReference(document, "/mvc/batches/maybe-user").Should().Be("#/components/schemas/BatchOfEnvelopeUser");
        document["components"]!["schemas"]!["BatchOfNullableEnvelopeUser"].Should().BeNull();
    }

    [Fact]
    public async Task should_resolve_recursive_and_polymorphic_members_and_avoid_taken_ids()
    {
        var document = await _GetMvcDocumentAsync();

        // A variant of a recursive type refers to itself, not to the plain component.
        _ResponseReference(document, "/mvc/chains/maybe-user")
            .Should()
            .Be("#/components/schemas/ChainOfNullableEnvelopeUser");
        var chain = document.Schema("ChainOfNullableEnvelopeUser")["properties"]!;
        _ShouldBeNullableReference(chain["value"]!, "#/components/schemas/EnvelopeUser");
        _ShouldBeNullableReference(chain["next"]!, "#/components/schemas/ChainOfNullableEnvelopeUser");

        // The members of each derived type of a polymorphic response are resolved from that type's own annotations; the
        // generator names a derived type's component after its base.
        ((string?)document.Schema("EnvelopeShapeEnvelopeCircle")["properties"]!["label"]!["$ref"])
            .Should()
            .Be(_NullableEnvelope);

        // A variant id another component already uses gets the first free numeric suffix.
        _ResponseReference(document, "/mvc/boxes/maybe-user")
            .Should()
            .Be("#/components/schemas/BoxOfNullableEnvelopeUser2");
        document.Schema("BoxOfNullableEnvelopeUser")["properties"]!["label"]!.Types().Should().Equal("string");
    }

    private static Task<JsonNode> _GetMvcDocumentAsync()
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(EnvelopesController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            app => app.MapControllers(),
            "v1",
            AbortToken
        );
    }

    private static string? _ResponseReference(JsonNode document, string path)
    {
        return (string?)
            document.Operation(path, "get")["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"];
    }

    private static void _ShouldBeNonNullableUser(JsonNode envelope)
    {
        // The member's comment stays beside the reference.
        envelope["properties"]!["data"]!.ShouldBeJson(
            """{"$ref":"#/components/schemas/EnvelopeUser","description":"The wrapped value."}"""
        );
        envelope.Required().Should().Equal("data");
    }

    private static void _ShouldBeNullableUser(JsonNode envelope)
    {
        _ShouldBeNullableReference(envelope["properties"]!["data"]!, "#/components/schemas/EnvelopeUser");
        // The constructor parameter keeps the member required even where it may be null.
        envelope.Required().Should().Equal("data");
    }

    private static void _ShouldBeNullableReference(JsonNode property, string reference)
    {
        property["oneOf"].ShouldBeJson($$"""[{"$ref":"{{reference}}"},{"type":"null"}]""");
    }
}

public sealed record EnvelopeUser(string Name);

public sealed record EnvelopeOrder(string Number);

public sealed class EnvelopeHolder
{
    public required Headless.Primitives.DataEnvelope<EnvelopeUser> Current { get; init; }

    public required Headless.Primitives.DataEnvelope<EnvelopeUser?> Previous { get; init; }
}

public sealed class Pair<T>
{
    public required T First { get; init; }

    public required Headless.Primitives.DataEnvelope<T> Inner { get; init; }

    public T? Fallback { get; init; }
}

public class Page<T>
{
    public required T Head { get; init; }
}

public sealed class NamedPage<T> : Page<T>
{
    public required string Name { get; init; }
}

public sealed class Batch<T>
{
    public required IReadOnlyList<T> Items { get; init; }
}

public sealed class Chain<T>
{
    public required T Value { get; init; }

    public Chain<T>? Next { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EnvelopeCircle), "circle")]
public abstract class EnvelopeShape;

public sealed class EnvelopeCircle : EnvelopeShape
{
    public required Headless.Primitives.DataEnvelope<EnvelopeUser?> Label { get; init; }
}

public sealed class Box<T>
{
    public required T Value { get; init; }
}

public sealed class BoxOfNullableEnvelopeUser
{
    public required string Label { get; init; }
}

[NonController]
[ApiController]
[Route("mvc")]
public sealed class EnvelopesController : ControllerBase
{
    [HttpGet("envelopes/user")]
    public Task<ActionResult<Headless.Primitives.DataEnvelope<EnvelopeUser>>> ReadUser() =>
        throw new NotSupportedException();

    [HttpGet("envelopes/maybe-user")]
    public Task<ActionResult<Headless.Primitives.DataEnvelope<EnvelopeUser?>>> ReadMaybeUser() =>
        throw new NotSupportedException();

    [HttpPost("envelopes/maybe-user")]
    public string PostMaybeUser([FromBody] Headless.Primitives.DataEnvelope<EnvelopeUser?> envelope) =>
        envelope.Data?.Name ?? "";

    [HttpGet("envelopes/declared")]
    [ProducesResponseType(typeof(Headless.Primitives.DataEnvelope<EnvelopeUser>), StatusCodes.Status200OK)]
    public IActionResult ReadDeclared() => throw new NotSupportedException();

    [HttpGet("holders")]
    public ActionResult<EnvelopeHolder> ReadHolder() => throw new NotSupportedException();

    [HttpGet("pairs/user")]
    public ActionResult<Pair<EnvelopeUser>> ReadPair() => throw new NotSupportedException();

    [HttpGet("pairs/maybe-user")]
    public ActionResult<Pair<EnvelopeUser?>> ReadMaybePair() => throw new NotSupportedException();

    [HttpGet("batches/user")]
    public ActionResult<Batch<EnvelopeUser>> ReadBatch() => throw new NotSupportedException();

    [HttpGet("batches/maybe-user")]
    public ActionResult<Batch<EnvelopeUser?>> ReadMaybeBatch() => throw new NotSupportedException();

    [HttpGet("chains/maybe-user")]
    public ActionResult<Chain<EnvelopeUser?>> ReadMaybeChain() => throw new NotSupportedException();

    [HttpGet("shapes")]
    public ActionResult<EnvelopeShape> ReadShape() => throw new NotSupportedException();

    [HttpGet("boxes/taken")]
    public ActionResult<BoxOfNullableEnvelopeUser> ReadTakenBox() => throw new NotSupportedException();

    [HttpGet("boxes/maybe-user")]
    public ActionResult<Box<EnvelopeUser?>> ReadMaybeBox() => throw new NotSupportedException();

    [HttpGet("pages/user")]
    public ActionResult<NamedPage<EnvelopeUser>> ReadPage() => throw new NotSupportedException();

    [HttpGet("pages/maybe-user")]
    public ActionResult<NamedPage<EnvelopeUser?>> ReadMaybePage() => throw new NotSupportedException();
}
