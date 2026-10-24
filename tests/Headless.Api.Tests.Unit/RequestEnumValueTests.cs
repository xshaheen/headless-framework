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

        foreach (var policy in new[] { "camel", "snake", "kebab", "none" })
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
    [InlineData("read", true, "Read")]
    [InlineData("read, Write", true, "Read, Write")]
    [InlineData("READ,write", true, "Read, Write")]
    [InlineData("read,bogus", false, "")]
    [InlineData("3", false, "")]
    public void should_read_a_flags_combination_as_the_body_does(string value, bool expected, string canonical)
    {
        var names = RequestEnumValue.For(typeof(Permissions), JsonConstants.DefaultWebJsonOptions);

        names!.TryGetCanonical(value, out var result).Should().Be(expected);
        result.Should().Be(canonical);
        _ReadBody<Permissions>(value, JsonConstants.DefaultWebJsonOptions).HasValue.Should().Be(expected);
    }

    [Fact]
    public void should_return_the_member_name_enum_try_parse_accepts()
    {
        var names = RequestEnumValue.For(typeof(Status), JsonConstants.DefaultWebJsonOptions);

        names!.TryGetCanonical("held", out var held).Should().BeTrue();
        names.TryGetCanonical("negative", out var negative).Should().BeTrue();

        held.Should().Be(nameof(Status.OnHold));
        Enum.Parse<Status>(negative).Should().Be(Status.Negative);
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
            "camel" => JsonConstants.DefaultWebJsonOptions,
            _ => new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters =
                {
                    new JsonStringEnumConverter(
                        policy switch
                        {
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
