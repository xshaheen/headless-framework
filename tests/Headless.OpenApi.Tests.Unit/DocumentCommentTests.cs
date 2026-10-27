// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Pins what a reader of the document and of a client generated from it sees: the XML comments of the application's
/// own types and actions, and a nullable object read as the object first.
/// </summary>
public sealed class DocumentCommentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync()
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(MembersController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            app => app.MapControllers(),
            "v1",
            AbortToken
        );
    }

    [Fact]
    public async Task should_describe_a_type_and_its_properties_from_their_xml_comments()
    {
        var document = await _GetDocumentAsync();

        var member = document.Schema("MemberView");
        ((string?)member["description"]).Should().Be("A charity staff member as the portal shows it.");
        ((string?)member["properties"]!["charityId"]!["description"])
            .Should()
            .Be("The charity the member belongs to; null for a platform member.");
        ((string?)member["properties"]!["isOwner"]!["description"]).Should().Be("Whether this member owns CharityId.");
    }

    [Fact]
    public async Task should_describe_a_positional_record_property_from_its_param_comment()
    {
        var document = await _GetDocumentAsync();

        ((string?)document.Schema("InviteMember")["properties"]!["email"]!["description"])
            .Should()
            .Be("The address the invitation goes to.");
    }

    [Fact]
    public async Task should_describe_an_action_from_its_summary_remarks_and_parameter_comments()
    {
        var document = await _GetDocumentAsync();

        var operation = document.Operation("/members", "get");
        ((string?)operation["summary"]).Should().Be("Lists the members of a charity.");
        ((string?)operation["description"]).Should().Be("Pending invitations are left out.\n\nOrdered by name.");
        ((string?)operation.Parameter("search")["description"]).Should().Be("Matches the start of a name.");
    }

    [Fact]
    public async Task should_describe_an_enum_and_an_object_that_only_a_property_reaches_from_their_type_comments()
    {
        var document = await _GetDocumentAsync();

        ((string?)document.Schema("MemberRole")["description"]).Should().Be("The part a member plays in a charity.");
        ((string?)document.Schema("MemberAddress")["description"]).Should().Be("A postal address.");
    }

    [Fact]
    public async Task should_describe_a_property_that_refers_to_a_shared_schema_on_its_reference()
    {
        var document = await _GetDocumentAsync();

        // Each reference carries its own property's comment; the shared component keeps the type's.
        var properties = document.Schema("MemberView")["properties"]!;
        properties["role"]!.ShouldBeJson(
            """{"$ref":"#/components/schemas/MemberRole","description":"What the member may do in the charity."}"""
        );
        properties["invitedAs"]!.ShouldBeJson("""{"$ref":"#/components/schemas/MemberRole"}""");
        ((string?)properties["address"]!["description"]).Should().Be("Where the member works; null when not recorded.");
    }

    [Fact]
    public async Task should_list_the_null_alternative_of_a_nullable_object_last()
    {
        var document = await _GetDocumentAsync();

        var alternatives = document.Schema("MemberView")["properties"]!["address"]!["oneOf"]!.AsArray();
        ((string?)alternatives[0]!["$ref"]).Should().Be("#/components/schemas/MemberAddress");
        ((string?)alternatives[1]!["type"]).Should().Be("null");

        // The property comment moved onto the oneOf, so the reference inside it carries none.
        alternatives[0]!.ShouldBeJson("""{"$ref":"#/components/schemas/MemberAddress"}""");
    }
}

/// <summary>A charity staff member as the portal shows it.</summary>
public sealed class MemberView
{
    /// <summary>The charity the member belongs to; null for a platform member.</summary>
    public string? CharityId { get; init; }

    /// <summary>Whether this member owns <see cref="CharityId" />.</summary>
    public bool IsOwner { get; init; }

    /// <summary>Where the member works; null when not recorded.</summary>
    public MemberAddress? Address { get; init; }

    /// <summary>What the member may do in the charity.</summary>
    public MemberRole Role { get; init; }

    public MemberRole InvitedAs { get; init; }
}

/// <summary>A postal address.</summary>
public sealed record MemberAddress(string City);

/// <summary>The part a member plays in a charity.</summary>
public enum MemberRole
{
    Staff,
    Owner,
}

/// <summary>Invites a member.</summary>
/// <param name="Email">The address the invitation goes to.</param>
public sealed record InviteMember(string Email);

[NonController]
[ApiController]
[AllowAnonymous]
[Route("members")]
public sealed class MembersController : ControllerBase
{
    /// <summary>Lists the members of a charity.</summary>
    /// <remarks>
    /// <para>Pending invitations are left out.</para>
    /// <para>Ordered by name.</para>
    /// </remarks>
    /// <param name="search">Matches the start of a name.</param>
    [HttpGet]
    public ActionResult<MemberView[]> List([FromQuery] string? search) =>
        Ok(new[] { new MemberView { CharityId = search } });

    [HttpPost]
    public ActionResult<MemberView> Invite(InviteMember request) => Ok(new MemberView { CharityId = request.Email });
}
