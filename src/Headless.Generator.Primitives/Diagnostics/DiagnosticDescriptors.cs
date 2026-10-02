// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Resources;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.Primitives.Diagnostics;

/// <summary>Contains all diagnostic descriptors used by the primitive generator.</summary>
/// <remarks>
/// Every descriptor is declared with a literal ID, category, and severity so the release-tracking analyzer can check it
/// against <c>AnalyzerReleases.Shipped.md</c> and <c>AnalyzerReleases.Unshipped.md</c>. Text comes from
/// <c>DiagnosticMessages.resx</c>, and each help link targets the rule's row in the utilities guide.
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string _Category = "Headless.Generator.Primitives";

    private const string _HelpLinkBase =
        "https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#";

    private static readonly ResourceManager _Resources = new(
        "Headless.Generator.Primitives.Resources.DiagnosticMessages",
        typeof(DiagnosticDescriptors).Assembly
    );

    private static readonly string[] _CustomTags = [WellKnownDiagnosticTags.Telemetry];

    public static readonly DiagnosticDescriptor GeneratorFailure = new(
        "HF1000",
        _Resource("GeneratorFailureTitle"),
        _Resource("GeneratorFailureMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("GeneratorFailureMessage"),
        helpLinkUri: _HelpLinkBase + "hf1000",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor UnsupportedUnderlyingType = new(
        "HF1001",
        _Resource("UnsupportedUnderlyingTypeTitle"),
        _Resource("UnsupportedUnderlyingTypeMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("UnsupportedUnderlyingTypeMessage"),
        helpLinkUri: _HelpLinkBase + "hf1001",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor PrimitiveMustBePartial = new(
        "HF1002",
        _Resource("PrimitiveMustBePartialTitle"),
        _Resource("PrimitiveMustBePartialMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("PrimitiveMustBePartialMessage"),
        helpLinkUri: _HelpLinkBase + "hf1002",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor SerializationFormatRequiresDateOrTime = new(
        "HF1012",
        _Resource("SerializationFormatRequiresDateOrTimeTitle"),
        _Resource("SerializationFormatRequiresDateOrTimeMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("SerializationFormatRequiresDateOrTimeMessage"),
        helpLinkUri: _HelpLinkBase + "hf1012",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor SupportedOperationsRequiresNumeric = new(
        "HF1013",
        _Resource("SupportedOperationsRequiresNumericTitle"),
        _Resource("SupportedOperationsRequiresNumericMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("SupportedOperationsRequiresNumericMessage"),
        helpLinkUri: _HelpLinkBase + "hf1013",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor TypeShouldBeValueType = new(
        "HF1015",
        _Resource("TypeShouldBeValueTypeTitle"),
        _Resource("TypeShouldBeValueTypeMessage"),
        _Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: _Resource("TypeShouldBeValueTypeMessage"),
        helpLinkUri: _HelpLinkBase + "hf1015",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor TypeShouldBeReferenceType = new(
        "HF1016",
        _Resource("TypeShouldBeReferenceTypeTitle"),
        _Resource("TypeShouldBeReferenceTypeMessage"),
        _Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: _Resource("TypeShouldBeReferenceTypeMessage"),
        helpLinkUri: _HelpLinkBase + "hf1016",
        customTags: _CustomTags
    );

    private static LocalizableResourceString _Resource(string resourceName) =>
        new(resourceName, _Resources, typeof(DiagnosticDescriptors));
}
