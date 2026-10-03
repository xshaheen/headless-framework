// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Resources;
using Microsoft.CodeAnalysis;

namespace Headless.Messaging.SourceGenerator.Validation;

/// <summary>Contains all diagnostic descriptors used by the Messaging source generator.</summary>
/// <remarks>
/// Every descriptor is declared with a literal ID, category, and severity so the release-tracking analyzer can check it
/// against <c>AnalyzerReleases.Shipped.md</c> and <c>AnalyzerReleases.Unshipped.md</c>. Text comes from
/// <c>DiagnosticMessages.resx</c>, and each help link targets the rule's row in the Messaging guide.
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string _Category = "Headless.Messaging.SourceGenerator";

    private const string _HelpLinkBase =
        "https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#";

    private static readonly ResourceManager _Resources = new(
        "Headless.Messaging.SourceGenerator.Resources.DiagnosticMessages",
        typeof(DiagnosticDescriptors).Assembly
    );

    private static readonly string[] _CustomTags = [WellKnownDiagnosticTags.Telemetry];

    public static readonly DiagnosticDescriptor InvalidConsumerIdentity = new(
        "HM001",
        _Resource("InvalidConsumerIdentityTitle"),
        _Resource("InvalidConsumerIdentityMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidConsumerIdentityMessage"),
        helpLinkUri: _HelpLinkBase + "hm001",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor DuplicateConsumerIdentity = new(
        "HM002",
        _Resource("DuplicateConsumerIdentityTitle"),
        _Resource("DuplicateConsumerIdentityMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("DuplicateConsumerIdentityMessage"),
        helpLinkUri: _HelpLinkBase + "hm002",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MissingConsumeInterface = new(
        "HM003",
        _Resource("MissingConsumeInterfaceTitle"),
        _Resource("MissingConsumeInterfaceMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MissingConsumeInterfaceMessage"),
        helpLinkUri: _HelpLinkBase + "hm003",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor DuplicateQueueConsumer = new(
        "HM004",
        _Resource("DuplicateQueueConsumerTitle"),
        _Resource("DuplicateQueueConsumerMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("DuplicateQueueConsumerMessage"),
        helpLinkUri: _HelpLinkBase + "hm004",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidFailurePolicy = new(
        "HM005",
        _Resource("InvalidFailurePolicyTitle"),
        _Resource("InvalidFailurePolicyMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidFailurePolicyMessage"),
        helpLinkUri: _HelpLinkBase + "hm005",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor SubscriptionHookWithoutEveryInstance = new(
        "HM006",
        _Resource("SubscriptionHookWithoutEveryInstanceTitle"),
        _Resource("SubscriptionHookWithoutEveryInstanceMessage"),
        _Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: _Resource("SubscriptionHookWithoutEveryInstanceMessage"),
        helpLinkUri: _HelpLinkBase + "hm006",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InaccessibleConsumer = new(
        "HM007",
        _Resource("InaccessibleConsumerTitle"),
        _Resource("InaccessibleConsumerMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InaccessibleConsumerMessage"),
        helpLinkUri: _HelpLinkBase + "hm007",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor AbstractOrGenericConsumer = new(
        "HM008",
        _Resource("AbstractOrGenericConsumerTitle"),
        _Resource("AbstractOrGenericConsumerMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("AbstractOrGenericConsumerMessage"),
        helpLinkUri: _HelpLinkBase + "hm008",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MultipleLaneAttributes = new(
        "HM009",
        _Resource("MultipleLaneAttributesTitle"),
        _Resource("MultipleLaneAttributesMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MultipleLaneAttributesMessage"),
        helpLinkUri: _HelpLinkBase + "hm009",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor FailurePolicyOnEveryInstance = new(
        "HM010",
        _Resource("FailurePolicyOnEveryInstanceTitle"),
        _Resource("FailurePolicyOnEveryInstanceMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("FailurePolicyOnEveryInstanceMessage"),
        helpLinkUri: _HelpLinkBase + "hm010",
        customTags: _CustomTags
    );

    private static LocalizableResourceString _Resource(string resourceName) =>
        new(resourceName, _Resources, typeof(DiagnosticDescriptors));
}
