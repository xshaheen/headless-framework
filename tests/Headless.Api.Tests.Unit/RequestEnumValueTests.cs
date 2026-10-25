// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Headless.Api;
using Headless.Api.Resources;
using Headless.Serializer;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Pins that a route, query, header, or form enum value is accepted exactly when the JSON body would read the same text
/// as a string, under the Headless options and under other naming policies.
/// </summary>
public sealed class RequestEnumValueTests : TestBase
{
    public static TheoryData<string, string> Inputs()
    {
        var data = new TheoryData<string, string>();
        string[] values =
        [
            "active",
            "Active",
            "ACTIVE",
            " active ",
            "onHold",
            "OnHold",
            "on_hold",
            "on-hold",
            "held",
            "Held",
            "negative",
            "1",
            "-1",
            "bogus",
            "",
        ];

        foreach (var policy in new[] { "web", "camel", "snake", "kebab" })
        {
            foreach (var value in values)
            {
                data.Add(policy, value);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void should_accept_a_value_exactly_when_the_json_body_reads_it(string policy, string value)
    {
        var options = _Options(policy);
        var names = RequestEnumValue.For(typeof(Status), options);

        var accepted = names!.TryParse(value, out var parsed);

        var body = _ReadBody<Status>(value, options);
        accepted.Should().Be(body.HasValue, $"the body {(body.HasValue ? "reads" : "rejects")} \"{value}\"");
        parsed.Should().Be(body.HasValue ? body.Value : null);
    }

    [Theory]
    // The web preset writes member names, so a member name passes as it is.
    [InlineData("Active", true, null)]
    [InlineData(" Active ", true, null)]
    [InlineData("Negative", true, null)]
    // An alias is the wire name; Enum.TryParse does not know it, so the member name takes its place.
    [InlineData("held", true, nameof(Status.OnHold))]
    // Enum.TryParse is case-sensitive, so other casing, a replaced member name, and numbers are refused.
    [InlineData("active", false, null)]
    [InlineData("Held", false, null)]
    [InlineData("OnHold", false, null)]
    [InlineData("1", false, null)]
    [InlineData("bogus", false, null)]
    public void should_pass_only_the_exact_spelling_enum_try_parse_reads(string value, bool expected, string? rewrite)
    {
        var names = RequestEnumValue.For(typeof(Status), JsonConstants.DefaultWebJsonOptions)!;

        names.TryGetExact(value, out var result).Should().Be(expected);
        result.Should().Be(rewrite);
    }

    [Theory]
    [InlineData("Read", true, null)]
    [InlineData("Read, Write", true, null)]
    [InlineData("Read,Write", true, null)]
    [InlineData("READ,write", false, null)]
    [InlineData("Read,bogus", false, null)]
    [InlineData("3", false, null)]
    public void should_pass_an_exact_flags_combination_as_it_is(string value, bool expected, string? rewrite)
    {
        var names = RequestEnumValue.For(typeof(Permissions), JsonConstants.DefaultWebJsonOptions)!;

        names.TryGetExact(value, out var result).Should().Be(expected);
        result.Should().Be(rewrite);
    }

    [Fact]
    public void should_rewrite_names_a_camel_case_policy_derives_to_member_names()
    {
        var names = RequestEnumValue.For(typeof(Permissions), _Options("camel"))!;

        names.TryGetExact("read", out var single).Should().BeTrue();
        names.TryGetExact("read,write", out var flags).Should().BeTrue();

        single.Should().Be(nameof(Permissions.Read));
        Enum.Parse<Permissions>(flags!).Should().Be(Permissions.Read | Permissions.Write);
    }

    [Fact]
    public void should_check_a_value_without_allocating()
    {
        var names = RequestEnumValue.For(typeof(Status), JsonConstants.DefaultWebJsonOptions)!;
        var accepted = 0;

        // Warm up first, so the measured calls see only steady-state work.
        for (var i = 0; i < 1_000; i++)
        {
            accepted += names.TryParse("Active", out _) && names.TryGetExact("held", out _) ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1_000; i++)
        {
            accepted += names.TryParse("active", out _) ? 1 : 0;
            accepted += names.TryGetExact(" Active ", out _) ? 1 : 0;
            accepted += names.TryGetExact("held", out _) ? 1 : 0;
            accepted += names.TryParse("1", out _) ? 1 : 0;
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        accepted.Should().Be(4_000, "the names are accepted and the number is not");
    }

    [Fact]
    public void should_have_no_names_when_the_options_write_the_enum_as_a_number()
    {
        RequestEnumValue.For(typeof(Status), new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().BeNull();
    }

    [Fact]
    public void should_name_the_parameter_in_the_invalid_parameter_value_error()
    {
        var error = GeneralMessageDescriber.InvalidParameterValue("status");

        error.Code.Should().Be("g:invalid_parameter_value");
        error.Description.Should().Contain("status");
        error.Params.Should().ContainKey("parameter").WhoseValue.Should().Be("status");
    }

    private static JsonSerializerOptions _Options(string policy) =>
        policy switch
        {
            "web" => JsonConstants.DefaultWebJsonOptions,
            _ => new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters =
                {
                    new JsonStringEnumConverter(
                        policy switch
                        {
                            "camel" => JsonNamingPolicy.CamelCase,
                            "snake" => JsonNamingPolicy.SnakeCaseLower,
                            "kebab" => JsonNamingPolicy.KebabCaseLower,
                            _ => null,
                        },
                        allowIntegerValues: false
                    ),
                },
            },
        };

    private static T? _ReadBody<T>(string value, JsonSerializerOptions options)
        where T : struct, Enum
    {
        try
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value), options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

#pragma warning disable CA1008 // Deliberate: application enums commonly start at 1, and the negative member checks signed values.
    public enum Status
    {
        Negative = -1,
        Active = 1,

        [JsonStringEnumMemberName("held")]
        OnHold = 2,
    }
#pragma warning restore CA1008

    [Flags]
    public enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
    }
}
