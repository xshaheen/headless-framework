// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;

namespace Tests.Persistence;

/// <summary>
/// The header text a correlation id search matches is the text the stored envelope holds, under the options the
/// envelope was written with.
/// </summary>
public sealed class StoredEnvelopeHeaderTests : TestBase
{
    public static TheoryData<string> Options => ["default", "indented", "upper-keys"];

    [Theory]
    [MemberData(nameof(Options))]
    public void should_match_the_header_inside_an_envelope_written_with_the_same_options(string variant)
    {
        // given
        var options = new MessagingOptions().JsonSerializerOptions;
        var json = new JsonSerializerOptions(options)
        {
            WriteIndented = variant == "indented",
            DictionaryKeyPolicy =
                variant == "upper-keys" ? JsonNamingPolicy.KebabCaseUpper : options.DictionaryKeyPolicy,
        };
        var envelope = JsonSerializer.Serialize(
            new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "m-1",
                    [Headers.CorrelationId] = "c-42 \"quoted\" <tag>",
                },
                new { Note = "c-42" }
            ),
            json
        );

        // when
        var exact = StoredEnvelopeHeader.Text(json, Headers.CorrelationId, "c-42 \"quoted\" <tag>");
        var prefix = StoredEnvelopeHeader.Text(json, Headers.CorrelationId, "c-42");

        // then: the full value matches, a prefix of it or a body holding it does not
        envelope.Should().Contain(exact);
        envelope.Should().NotContain(prefix);
    }
}
