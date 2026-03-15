using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.Validation;

/// <summary>
/// Contains all diagnostic descriptors used by the Jobs source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor ClassAccessibility = new(
        "TQ001",
        "Class accessibility issue",
        "The class '{0}' should be public or internal to be used with [JobFunction]",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor MethodAccessibility = new(
        "TQ002",
        "Method accessibility issue",
        "The method '{0}' should be public or internal to be used with [JobFunction]",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor InvalidCronExpression = new(
        "TQ003",
        "Invalid cron expression",
        "The cron expression '{0}' in function '{1}' is invalid",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor MissingFunctionName = new(
        "TQ004",
        "Missing function name",
        "The [JobFunction] attribute on method '{0}' in class '{1}' must specify a function name",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor DuplicateFunctionName = new(
        "TQ005",
        "Duplicate function name",
        "The function name '{0}' is already used by another [JobFunction] method",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor MultipleConstructors = new(
        "TQ006",
        "Multiple constructors detected",
        "The class '{0}' has multiple constructors. Only the first constructor will be used for dependency injection. Consider using [JobsConstructor] attribute to explicitly mark the preferred constructor.",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Warning,
        true
    );

    public static readonly DiagnosticDescriptor AbstractClass = new(
        "TQ007",
        "Abstract class with JobFunction",
        "The abstract class '{0}' contains [JobFunction] methods",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor NestedClass = new(
        "TQ008",
        "Nested class with JobFunction",
        "The nested class '{0}' contains [JobFunction] methods. JobFunction methods are only allowed in top-level classes.",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor InvalidMethodParameter = new(
        "TQ009",
        "Invalid JobFunction parameter",
        "The method '{0}' has invalid parameter '{1}' of type '{2}'. JobFunction methods can only have JobFunctionContext, JobFunctionContext<T>, CancellationToken parameters, or no parameters.",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );

    public static readonly DiagnosticDescriptor MultipleJobsConstructorAttributes = new(
        "TQ010",
        "Multiple JobsConstructor attributes",
        "The class '{0}' has multiple constructors with [JobsConstructor] attribute. Only one constructor can be marked with [JobsConstructor].",
        "Headless.Jobs.SourceGenerator",
        DiagnosticSeverity.Error,
        true
    );
}
