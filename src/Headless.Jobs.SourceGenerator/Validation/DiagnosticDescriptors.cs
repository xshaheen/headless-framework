// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Resources;
using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Contains all diagnostic descriptors used by the Jobs source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string _Category = "Headless.Jobs.SourceGenerator";
    private const string _HelpLinkUri =
        "https://github.com/xshaheen/headless-framework/blob/main/src/Headless.Jobs.SourceGenerator/AnalyzerReleases.Shipped.md";

    private static readonly ResourceManager _Resources = new(
        "Headless.Jobs.SourceGenerator.Resources.DiagnosticMessages",
        typeof(DiagnosticDescriptors).Assembly
    );

    private static readonly string[] _CustomTags = [WellKnownDiagnosticTags.Telemetry];

    public static readonly DiagnosticDescriptor ClassAccessibility = _Create(
        "HF001",
        "ClassAccessibilityTitle",
        "ClassAccessibilityMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MethodAccessibility = _Create(
        "HF002",
        "MethodAccessibilityTitle",
        "MethodAccessibilityMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor InvalidCronExpression = _Create(
        "HF003",
        "InvalidCronExpressionTitle",
        "InvalidCronExpressionMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MissingFunctionName = _Create(
        "HF004",
        "MissingFunctionNameTitle",
        "MissingFunctionNameMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor DuplicateFunctionName = _Create(
        "HF005",
        "DuplicateFunctionNameTitle",
        "DuplicateFunctionNameMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MultipleConstructors = _Create(
        "HF006",
        "MultipleConstructorsTitle",
        "MultipleConstructorsMessage",
        DiagnosticSeverity.Warning
    );

    public static readonly DiagnosticDescriptor AbstractClass = _Create(
        "HF007",
        "AbstractClassTitle",
        "AbstractClassMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor NestedClass = _Create(
        "HF008",
        "NestedClassTitle",
        "NestedClassMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor InvalidMethodParameter = _Create(
        "HF009",
        "InvalidMethodParameterTitle",
        "InvalidMethodParameterMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MultipleJobsConstructorAttributes = _Create(
        "HF010",
        "MultipleJobsConstructorAttributesTitle",
        "MultipleJobsConstructorAttributesMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor DuplicateRequestType = _Create(
        "HF011",
        "DuplicateRequestTypeTitle",
        "DuplicateRequestTypeMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor InvalidJobPriority = _Create(
        "HF012",
        "InvalidJobPriorityTitle",
        "InvalidJobPriorityMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor InvalidMaxConcurrency = _Create(
        "HF013",
        "InvalidMaxConcurrencyTitle",
        "InvalidMaxConcurrencyMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor UnknownMiddlewareTarget = new(
        "HF014",
        "Unknown Jobs middleware target",
        "The Jobs middleware target '{0}' does not match a generated job-function descriptor",
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateMiddleware = new(
        "HF015",
        "Duplicate Jobs middleware declaration",
        "The Jobs middleware declaration for '{0}' is duplicated",
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MethodMiddlewareRequiresJobFunction = new(
        "HF016",
        "Method Jobs middleware requires JobFunction",
        "Method-level Jobs middleware must be declared beside a [JobFunction] attribute",
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MethodMiddlewareFunctionTarget = new(
        "HF017",
        "Method Jobs middleware cannot specify Function",
        "Method-level Jobs middleware derives its target from [JobFunction]; remove the Function property",
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor LocalAssemblyMiddlewareTarget = new(
        "HF018",
        "Assembly Jobs middleware cannot target a local function",
        "The Jobs middleware target '{0}' is declared in this assembly; place the middleware attribute beside that [JobFunction] method",
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InaccessibleMiddlewareType = new(
        "HF019",
        "Jobs middleware type is inaccessible to generated code",
        "The Jobs middleware type '{0}' must be accessible from generated code in the declaring assembly",
        _Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static DiagnosticDescriptor _Create(
        string id,
        string titleResourceName,
        string messageResourceName,
        DiagnosticSeverity severity
    )
    {
        var message = _Resource(messageResourceName);

        return new DiagnosticDescriptor(
            id,
            _Resource(titleResourceName),
            message,
            _Category,
            severity,
            isEnabledByDefault: true,
            description: message,
            helpLinkUri: _HelpLinkUri,
            customTags: _CustomTags
        );
    }

    private static LocalizableResourceString _Resource(string resourceName)
    {
        return new(resourceName, _Resources, typeof(DiagnosticDescriptors));
    }
}
