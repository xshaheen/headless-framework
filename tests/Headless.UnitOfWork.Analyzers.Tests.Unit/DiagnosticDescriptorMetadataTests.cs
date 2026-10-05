// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Testing.Tests;
using Headless.UnitOfWork.Analyzers;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// Keeps every rule consistent: text comes from resources, the rule is a suggestion by default, and the help link points
/// at the rule's own row in the unit-of-work guide, which must exist. Release tracking of IDs and severities is enforced
/// separately at build time.
/// </summary>
public sealed class DiagnosticDescriptorMetadataTests : TestBase
{
    private const string _HelpLinkBase =
        "https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#";

    public static TheoryData<string> DiagnosticIds => [.. _Descriptors().Select(descriptor => descriptor.Id)];

    [Fact]
    public void should_declare_exactly_the_shipped_rules()
    {
        // HF2004 is deliberately absent: a sequence name's configured mode, not the receiver, decides gap-free numbering.
        _Descriptors().Select(descriptor => descriptor.Id).Should().BeEquivalentTo(["HF2001"]);
        new AutonomousReceiverAnalyzer()
            .SupportedDiagnostics.Select(descriptor => descriptor.Id)
            .Should()
            .BeEquivalentTo(_Descriptors().Select(descriptor => descriptor.Id));
    }

    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public void should_read_text_from_resources_and_default_to_a_suggestion(string id)
    {
        var descriptor = _Descriptor(id);

        descriptor.Title.Should().BeOfType<LocalizableResourceString>();
        descriptor.MessageFormat.Should().BeOfType<LocalizableResourceString>();
        descriptor.Description.Should().BeOfType<LocalizableResourceString>();
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
        descriptor
            .Description.ToString(CultureInfo.InvariantCulture)
            .Should()
            .NotBeNullOrWhiteSpace()
            .And.NotEndWith("Description");
        descriptor.Category.Should().Be("Headless.UnitOfWork.Analyzers");
        descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Info);
        descriptor.IsEnabledByDefault.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public async Task should_link_to_the_rule_row_in_the_unit_of_work_guide(string id)
    {
        var anchor = id.ToLowerInvariant();

        _Descriptor(id).HelpLinkUri.Should().Be(_HelpLinkBase + anchor);
        (await _ReadGuideAsync()).Should().Contain($"""<a id="{anchor}"></a>{id}""");
    }

    private static Task<string> _ReadGuideAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run inside the repository checkout");
        return File.ReadAllTextAsync(Path.Combine(directory!.FullName, "docs", "llms", "unit-of-work.md"), AbortToken);
    }

    private static DiagnosticDescriptor _Descriptor(string id) =>
        _Descriptors().Single(descriptor => string.Equals(descriptor.Id, id, StringComparison.Ordinal));

    private static IEnumerable<DiagnosticDescriptor> _Descriptors() =>
        typeof(AutonomousReceiverAnalyzer)
            .Assembly.GetType("Headless.UnitOfWork.Analyzers.DiagnosticDescriptors", throwOnError: true)!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<DiagnosticDescriptor>();
}
