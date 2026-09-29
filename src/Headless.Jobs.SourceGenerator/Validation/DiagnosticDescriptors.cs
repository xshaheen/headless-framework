// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Resources;
using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Contains all diagnostic descriptors used by the Jobs source generator.
/// </summary>
/// <remarks>
/// Every descriptor is declared with a literal ID, category, and severity so the release-tracking analyzer can check it
/// against <c>AnalyzerReleases.Shipped.md</c> and <c>AnalyzerReleases.Unshipped.md</c>. Text comes from
/// <c>DiagnosticMessages.resx</c>, and each help link targets the rule's row in the Jobs guide.
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string _Category = "Headless.Jobs.SourceGenerator";
    private const string _HelpLinkBase = "https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#";

    private static readonly ResourceManager _Resources = new(
        "Headless.Jobs.SourceGenerator.Resources.DiagnosticMessages",
        typeof(DiagnosticDescriptors).Assembly
    );

    private static readonly string[] _CustomTags = [WellKnownDiagnosticTags.Telemetry];

    public static readonly DiagnosticDescriptor ClassAccessibility = new(
        "HF001",
        _Resource("ClassAccessibilityTitle"),
        _Resource("ClassAccessibilityMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("ClassAccessibilityMessage"),
        helpLinkUri: _HelpLinkBase + "hf001",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MethodAccessibility = new(
        "HF002",
        _Resource("MethodAccessibilityTitle"),
        _Resource("MethodAccessibilityMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MethodAccessibilityMessage"),
        helpLinkUri: _HelpLinkBase + "hf002",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidCronExpression = new(
        "HF003",
        _Resource("InvalidCronExpressionTitle"),
        _Resource("InvalidCronExpressionMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidCronExpressionMessage"),
        helpLinkUri: _HelpLinkBase + "hf003",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MissingFunctionName = new(
        "HF004",
        _Resource("MissingFunctionNameTitle"),
        _Resource("MissingFunctionNameMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MissingFunctionNameMessage"),
        helpLinkUri: _HelpLinkBase + "hf004",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor DuplicateFunctionName = new(
        "HF005",
        _Resource("DuplicateFunctionNameTitle"),
        _Resource("DuplicateFunctionNameMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("DuplicateFunctionNameMessage"),
        helpLinkUri: _HelpLinkBase + "hf005",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MultipleConstructors = new(
        "HF006",
        _Resource("MultipleConstructorsTitle"),
        _Resource("MultipleConstructorsMessage"),
        _Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: _Resource("MultipleConstructorsMessage"),
        helpLinkUri: _HelpLinkBase + "hf006",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor AbstractClass = new(
        "HF007",
        _Resource("AbstractClassTitle"),
        _Resource("AbstractClassMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("AbstractClassMessage"),
        helpLinkUri: _HelpLinkBase + "hf007",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor NestedClass = new(
        "HF008",
        _Resource("NestedClassTitle"),
        _Resource("NestedClassMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("NestedClassMessage"),
        helpLinkUri: _HelpLinkBase + "hf008",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidMethodParameter = new(
        "HF009",
        _Resource("InvalidMethodParameterTitle"),
        _Resource("InvalidMethodParameterMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidMethodParameterMessage"),
        helpLinkUri: _HelpLinkBase + "hf009",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MultipleJobsConstructorAttributes = new(
        "HF010",
        _Resource("MultipleJobsConstructorAttributesTitle"),
        _Resource("MultipleJobsConstructorAttributesMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MultipleJobsConstructorAttributesMessage"),
        helpLinkUri: _HelpLinkBase + "hf010",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor DuplicateRequestType = new(
        "HF011",
        _Resource("DuplicateRequestTypeTitle"),
        _Resource("DuplicateRequestTypeMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("DuplicateRequestTypeMessage"),
        helpLinkUri: _HelpLinkBase + "hf011",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidJobPriority = new(
        "HF012",
        _Resource("InvalidJobPriorityTitle"),
        _Resource("InvalidJobPriorityMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidJobPriorityMessage"),
        helpLinkUri: _HelpLinkBase + "hf012",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidMaxConcurrency = new(
        "HF013",
        _Resource("InvalidMaxConcurrencyTitle"),
        _Resource("InvalidMaxConcurrencyMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidMaxConcurrencyMessage"),
        helpLinkUri: _HelpLinkBase + "hf013",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor UnknownMiddlewareTarget = new(
        "HF014",
        _Resource("UnknownMiddlewareTargetTitle"),
        _Resource("UnknownMiddlewareTargetMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("UnknownMiddlewareTargetMessage"),
        helpLinkUri: _HelpLinkBase + "hf014",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor DuplicateMiddleware = new(
        "HF015",
        _Resource("DuplicateMiddlewareTitle"),
        _Resource("DuplicateMiddlewareMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("DuplicateMiddlewareMessage"),
        helpLinkUri: _HelpLinkBase + "hf015",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MethodMiddlewareRequiresJobFunction = new(
        "HF016",
        _Resource("MethodMiddlewareRequiresJobFunctionTitle"),
        _Resource("MethodMiddlewareRequiresJobFunctionMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MethodMiddlewareRequiresJobFunctionMessage"),
        helpLinkUri: _HelpLinkBase + "hf016",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor MethodMiddlewareFunctionTarget = new(
        "HF017",
        _Resource("MethodMiddlewareFunctionTargetTitle"),
        _Resource("MethodMiddlewareFunctionTargetMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("MethodMiddlewareFunctionTargetMessage"),
        helpLinkUri: _HelpLinkBase + "hf017",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor LocalAssemblyMiddlewareTarget = new(
        "HF018",
        _Resource("LocalAssemblyMiddlewareTargetTitle"),
        _Resource("LocalAssemblyMiddlewareTargetMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("LocalAssemblyMiddlewareTargetMessage"),
        helpLinkUri: _HelpLinkBase + "hf018",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InaccessibleMiddlewareType = new(
        "HF019",
        _Resource("InaccessibleMiddlewareTypeTitle"),
        _Resource("InaccessibleMiddlewareTypeMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InaccessibleMiddlewareTypeMessage"),
        helpLinkUri: _HelpLinkBase + "hf019",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidMissedRunPolicy = new(
        "HF020",
        _Resource("InvalidMissedRunPolicyTitle"),
        _Resource("InvalidMissedRunPolicyMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidMissedRunPolicyMessage"),
        helpLinkUri: _HelpLinkBase + "hf020",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidMissedRunGrace = new(
        "HF021",
        _Resource("InvalidMissedRunGraceTitle"),
        _Resource("InvalidMissedRunGraceMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidMissedRunGraceMessage"),
        helpLinkUri: _HelpLinkBase + "hf021",
        customTags: _CustomTags
    );

    public static readonly DiagnosticDescriptor InvalidOverlapPolicy = new(
        "HF022",
        _Resource("InvalidOverlapPolicyTitle"),
        _Resource("InvalidOverlapPolicyMessage"),
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _Resource("InvalidOverlapPolicyMessage"),
        helpLinkUri: _HelpLinkBase + "hf022",
        customTags: _CustomTags
    );

    private static LocalizableResourceString _Resource(string resourceName) =>
        new(resourceName, _Resources, typeof(DiagnosticDescriptors));
}
