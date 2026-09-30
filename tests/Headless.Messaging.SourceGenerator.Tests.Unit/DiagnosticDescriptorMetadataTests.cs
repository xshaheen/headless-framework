// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.SourceGenerator;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Keeps every generator rule consistent: text comes from resources, and the help link points at the rule's own row in
/// the Messaging guide, which must exist. Release tracking of IDs and severities is enforced separately at build time.
/// </summary>
public sealed class DiagnosticDescriptorMetadataTests : TestBase
{
    private const string _HelpLinkBase =
        "https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#";

    public static TheoryData<string> DiagnosticIds => [.. _Descriptors().Select(descriptor => descriptor.Id)];

    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public void should_read_title_and_message_from_resources(string id)
    {
        var descriptor = _Descriptor(id);

        descriptor.Title.Should().BeOfType<LocalizableResourceString>();
        descriptor.MessageFormat.Should().BeOfType<LocalizableResourceString>();
        descriptor
            .Title.ToString(CultureInfo.InvariantCulture)
            .Should()
            .NotBeNullOrWhiteSpace()
            .And.NotEndWith("Title");
        descriptor
            .MessageFormat.ToString(CultureInfo.InvariantCulture)
            .Should()
            .NotBeNullOrWhiteSpace()
            .And.NotEndWith("Message");
        descriptor.Category.Should().Be("Headless.Messaging.SourceGenerator");
        descriptor.IsEnabledByDefault.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public async Task should_link_to_the_rule_row_in_the_messaging_guide(string id)
    {
        var anchor = id.ToLowerInvariant();

        _Descriptor(id).HelpLinkUri.Should().Be(_HelpLinkBase + anchor);
        (await _ReadMessagingGuideAsync()).Should().Contain($"""<a id="{anchor}"></a>{id}""");
    }

    private static Task<string> _ReadMessagingGuideAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run inside the repository checkout");
        return File.ReadAllTextAsync(Path.Combine(directory!.FullName, "docs", "llms", "messaging.md"), AbortToken);
    }

    private static DiagnosticDescriptor _Descriptor(string id) =>
        _Descriptors().Single(descriptor => string.Equals(descriptor.Id, id, StringComparison.Ordinal));

    private static IEnumerable<DiagnosticDescriptor> _Descriptors() =>
        typeof(MessagingIncrementalSourceGenerator)
            .Assembly.GetType(
                "Headless.Messaging.SourceGenerator.Validation.DiagnosticDescriptors",
                throwOnError: true
            )!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<DiagnosticDescriptor>();
}
