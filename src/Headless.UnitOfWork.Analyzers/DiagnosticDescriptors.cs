// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Resources;
using Microsoft.CodeAnalysis;

namespace Headless.UnitOfWork.Analyzers;

/// <summary>Contains every diagnostic descriptor the unit-of-work analyzers report.</summary>
/// <remarks>
/// Every descriptor is declared with a literal ID, category, and severity so the release-tracking analyzer can check it
/// against <c>AnalyzerReleases.Shipped.md</c> and <c>AnalyzerReleases.Unshipped.md</c>. Text comes from
/// <c>DiagnosticMessages.resx</c>, and each help link targets the rule's row in the unit-of-work guide. Every rule is
/// <see cref="DiagnosticSeverity.Info"/> by default because some autonomous calls inside a unit are correct; a host
/// raises a rule per ID in <c>.editorconfig</c>.
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string _Category = "Headless.UnitOfWork.Analyzers";

    private const string _HelpLinkBase =
        "https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#";

    private static readonly ResourceManager _Resources = new(
        "Headless.UnitOfWork.Analyzers.Resources.DiagnosticMessages",
        typeof(DiagnosticDescriptors).Assembly
    );

    private static readonly string[] _CustomTags = [WellKnownDiagnosticTags.Telemetry];

    public static readonly DiagnosticDescriptor OutboxReceiver = new(
        "HF2001",
        _Resource("OutboxReceiverTitle"),
        _Resource("OutboxReceiverMessage"),
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: _Resource("OutboxReceiverDescription"),
        helpLinkUri: _HelpLinkBase + "hf2001",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor JobsReceiver = new(
        "HF2002",
        _Resource("JobsReceiverTitle"),
        _Resource("JobsReceiverMessage"),
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: _Resource("JobsReceiverDescription"),
        helpLinkUri: _HelpLinkBase + "hf2002",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor TransactionLocksReceiver = new(
        "HF2003",
        _Resource("TransactionLocksReceiverTitle"),
        _Resource("TransactionLocksReceiverMessage"),
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: _Resource("TransactionLocksReceiverDescription"),
        helpLinkUri: _HelpLinkBase + "hf2003",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor LeasesReceiver = new(
        "HF2004",
        _Resource("LeasesReceiverTitle"),
        _Resource("LeasesReceiverMessage"),
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: _Resource("LeasesReceiverDescription"),
        helpLinkUri: _HelpLinkBase + "hf2004",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor IdempotencyReceiver = new(
        "HF2005",
        _Resource("IdempotencyReceiverTitle"),
        _Resource("IdempotencyReceiverMessage"),
        _Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: _Resource("IdempotencyReceiverDescription"),
        helpLinkUri: _HelpLinkBase + "hf2005",
        customTags: _CustomTags
    );

    private static LocalizableResourceString _Resource(string name) =>
        new(name, _Resources, typeof(DiagnosticDescriptors));
}
