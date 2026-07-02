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
        "TQ001",
        "ClassAccessibilityTitle",
        "ClassAccessibilityMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MethodAccessibility = _Create(
        "TQ002",
        "MethodAccessibilityTitle",
        "MethodAccessibilityMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor InvalidCronExpression = _Create(
        "TQ003",
        "InvalidCronExpressionTitle",
        "InvalidCronExpressionMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MissingFunctionName = _Create(
        "TQ004",
        "MissingFunctionNameTitle",
        "MissingFunctionNameMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor DuplicateFunctionName = _Create(
        "TQ005",
        "DuplicateFunctionNameTitle",
        "DuplicateFunctionNameMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MultipleConstructors = _Create(
        "TQ006",
        "MultipleConstructorsTitle",
        "MultipleConstructorsMessage",
        DiagnosticSeverity.Warning
    );

    public static readonly DiagnosticDescriptor AbstractClass = _Create(
        "TQ007",
        "AbstractClassTitle",
        "AbstractClassMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor NestedClass = _Create(
        "TQ008",
        "NestedClassTitle",
        "NestedClassMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor InvalidMethodParameter = _Create(
        "TQ009",
        "InvalidMethodParameterTitle",
        "InvalidMethodParameterMessage",
        DiagnosticSeverity.Error
    );

    public static readonly DiagnosticDescriptor MultipleJobsConstructorAttributes = _Create(
        "TQ010",
        "MultipleJobsConstructorAttributesTitle",
        "MultipleJobsConstructorAttributesMessage",
        DiagnosticSeverity.Error
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
