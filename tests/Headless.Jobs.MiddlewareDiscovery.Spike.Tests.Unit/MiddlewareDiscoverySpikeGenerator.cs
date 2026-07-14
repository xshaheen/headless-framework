// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tests;

internal sealed class MiddlewareDiscoverySpikeGenerator : IIncrementalGenerator
{
    private const string _JobFunctionAttribute = "Spike.Contracts.JobFunctionAttribute";
    private const string _JobFunctionDescriptorAttribute = "Spike.Contracts.JobFunctionDescriptorAttribute";
    private const string _JobMiddlewareAttribute = "Spike.Contracts.JobMiddlewareAttribute";
    private const string _IncludeJobsMiddlewareAttribute = "Spike.Contracts.IncludeJobsMiddlewareAttribute";
    private const string _GeneratedHookTypeName = "GeneratedJobsMiddlewareRegistration";
    private const string _DiagnosticCategory = "Headless.Jobs.MiddlewareDiscovery.Spike";
    private const string _DiagnosticHelpLink = "https://github.com/xshaheen/headless-framework/issues/302";

    private static readonly string[] _DiagnosticTags = [WellKnownDiagnosticTags.Telemetry];

    private static readonly DiagnosticDescriptor _MissingTarget = _CreateDiagnostic(
        "JMD001",
        "Middleware target was not found",
        "Middleware '{0}' targets unknown job function descriptor '{1}'"
    );

    private static readonly DiagnosticDescriptor _DuplicateMiddleware = _CreateDiagnostic(
        "JMD002",
        "Middleware declaration is duplicated",
        "Middleware declaration '{0}' is duplicated"
    );

    private static readonly DiagnosticDescriptor _MissingGeneratedHook = _CreateDiagnostic(
        "JMD003",
        "Generated middleware hook was not found",
        "Assembly marker '{0}' does not expose the well-known generated middleware hook '{1}'"
    );

    private static DiagnosticDescriptor _CreateDiagnostic(string id, string title, string message)
    {
        return new(
            id,
            title,
            message,
            _DiagnosticCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: message,
            helpLinkUri: _DiagnosticHelpLink,
            customTags: _DiagnosticTags
        );
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var discovery = context
            .CompilationProvider.Select(
                static (compilation, cancellationToken) => _Discover(compilation, cancellationToken)
            )
            .WithTrackingName("MiddlewareDiscovery");

        context.RegisterSourceOutput(
            discovery,
            static (productionContext, model) =>
            {
                foreach (var diagnostic in model.Diagnostics)
                {
                    productionContext.ReportDiagnostic(diagnostic);
                }

                productionContext.AddSource(
                    "JobsMiddlewareCallChain.g.cs",
                    SourceText.From(_Generate(model), Encoding.UTF8)
                );
            }
        );
    }

    private static DiscoveryModel _Discover(Compilation compilation, CancellationToken cancellationToken)
    {
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols.Insert(0, compilation.Assembly);

        var functionIdentities = new HashSet<string>(StringComparer.Ordinal);
        var middleware = new List<MiddlewareDeclaration>();
        var hooks = new List<GeneratedHook>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var assembly in assemblies)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.ToDisplayString();

                if (string.Equals(attributeName, _JobFunctionDescriptorAttribute, StringComparison.Ordinal))
                {
                    if (_GetStringArgument(attribute, 0) is { } identity)
                    {
                        functionIdentities.Add(identity);
                    }

                    continue;
                }

                if (string.Equals(attributeName, _JobMiddlewareAttribute, StringComparison.Ordinal))
                {
                    if (_CreateMiddlewareDeclaration(assembly, attribute) is { } declaration)
                    {
                        middleware.Add(declaration);
                    }

                    continue;
                }

                if (
                    SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
                    && string.Equals(attributeName, _IncludeJobsMiddlewareAttribute, StringComparison.Ordinal)
                )
                {
                    _AddGeneratedHook(attribute, hooks, diagnostics);
                }
            }
        }

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            // Incremental generator transforms are synchronous; Roslyn provides the cancellation-aware sync API here.
#pragma warning disable MA0045
            var root = syntaxTree.GetRoot(cancellationToken);
#pragma warning restore MA0045

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var attribute = semanticModel
                    .GetDeclaredSymbol(method, cancellationToken)
                    ?.GetAttributes()
                    .FirstOrDefault(static candidate =>
                        string.Equals(
                            candidate.AttributeClass?.ToDisplayString(),
                            _JobFunctionAttribute,
                            StringComparison.Ordinal
                        )
                    );

                if (attribute is not null && _GetStringArgument(attribute, 0) is { } identity)
                {
                    functionIdentities.Add(identity);
                }
            }
        }

        var orderedMiddleware = middleware
            .OrderBy(static declaration => declaration.Priority)
            .ThenBy(static declaration => declaration.StableIdentity, StringComparer.Ordinal)
            .ToImmutableArray();

        foreach (var duplicate in orderedMiddleware.GroupBy(static declaration => declaration.DuplicateKey))
        {
            if (duplicate.Skip(1).Any())
            {
                diagnostics.Add(Diagnostic.Create(_DuplicateMiddleware, Location.None, duplicate.Key.ToString()));
            }
        }

        var validMiddleware = ImmutableArray.CreateBuilder<MiddlewareDeclaration>();
        var seenDeclarations = new HashSet<MiddlewareKey>();

        foreach (var declaration in orderedMiddleware)
        {
            if (!seenDeclarations.Add(declaration.DuplicateKey))
            {
                continue;
            }

            if (declaration.TargetFunction is { } targetFunction && !functionIdentities.Contains(targetFunction))
            {
                diagnostics.Add(
                    Diagnostic.Create(_MissingTarget, Location.None, declaration.StableIdentity, targetFunction)
                );
                continue;
            }

            validMiddleware.Add(declaration);
        }

        return new(
            validMiddleware.ToImmutable(),
            [.. hooks.OrderBy(static hook => hook.AssemblyName, StringComparer.Ordinal)],
            diagnostics.ToImmutable()
        );
    }

    private static MiddlewareDeclaration? _CreateMiddlewareDeclaration(
        IAssemblySymbol declaringAssembly,
        AttributeData attribute
    )
    {
        if (
            attribute.ConstructorArguments.Length < 2
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol middlewareType
            || attribute.ConstructorArguments[1].Value is not int priority
        )
        {
            return null;
        }

        var targetFunction = _GetStringArgument(attribute, 2);
        var middlewareMetadataName = _GetMetadataName(middlewareType);
        var stableIdentity = declaringAssembly.Name + ":" + middlewareMetadataName;

        return new(
            middlewareType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            stableIdentity,
            priority,
            targetFunction
        );
    }

    private static void _AddGeneratedHook(
        AttributeData attribute,
        ICollection<GeneratedHook> hooks,
        ICollection<Diagnostic> diagnostics
    )
    {
        if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol markerType)
        {
            return;
        }

        var containingNamespace = markerType.ContainingNamespace.ToDisplayString();
        var hookMetadataName = string.IsNullOrEmpty(containingNamespace)
            ? _GeneratedHookTypeName
            : containingNamespace + "." + _GeneratedHookTypeName;
        var hookType = markerType.ContainingAssembly.GetTypeByMetadataName(hookMetadataName);

        if (hookType is null)
        {
            diagnostics.Add(
                Diagnostic.Create(_MissingGeneratedHook, Location.None, markerType.ToDisplayString(), hookMetadataName)
            );
            return;
        }

        hooks.Add(
            new(markerType.ContainingAssembly.Name, hookType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
        );
    }

    private static string? _GetStringArgument(AttributeData attribute, int index)
    {
        return attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;
    }

    private static string _GetMetadataName(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();

        for (var current = type; current is not null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }

        var namespaceName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString() + ".";

        return namespaceName + string.Join("+", typeNames);
    }

    private static string _Generate(DiscoveryModel model)
    {
        var builder = new StringBuilder(
            """
            // <auto-generated />
            #nullable enable
            namespace Spike.Generated;

            internal static class JobsMiddlewareCallChain
            {
                public static void InvokeGlobal(global::System.IServiceProvider services)
                {

            """
        );

        foreach (var declaration in model.Middleware.Where(static declaration => declaration.TargetFunction is null))
        {
            builder.Append("        ").Append(declaration.TypeName).AppendLine(".Invoke(services);");
        }

        builder.AppendLine(
            """
                }

                public static void InvokeFor(
                    string functionIdentity,
                    global::System.IServiceProvider services
                )
                {
                    switch (functionIdentity)
                    {
            """
        );

        foreach (
            var targetIdentity in model
                .Middleware.Where(static declaration => declaration.TargetFunction is not null)
                .Select(static declaration => declaration.TargetFunction!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        )
        {
            builder
                .Append("            case ")
                .Append(SymbolDisplay.FormatLiteral(targetIdentity, true))
                .AppendLine(":");

            foreach (
                var declaration in model.Middleware.Where(declaration =>
                    declaration.TargetFunction is null
                    || string.Equals(declaration.TargetFunction, targetIdentity, StringComparison.Ordinal)
                )
            )
            {
                builder.Append("                ").Append(declaration.TypeName).AppendLine(".Invoke(services);");
            }

            builder.AppendLine("                break;");
        }

        builder.AppendLine(
            """
                    }
                }

                public static void InvokeGeneratedHooks(global::System.IServiceProvider services)
                {
            """
        );

        foreach (var hook in model.Hooks)
        {
            builder.Append("        ").Append(hook.TypeName).AppendLine(".Register(services);");
        }

        builder.AppendLine(
            """
                }
            }
            """
        );

        return builder.ToString();
    }

    private sealed record DiscoveryModel(
        ImmutableArray<MiddlewareDeclaration> Middleware,
        ImmutableArray<GeneratedHook> Hooks,
        ImmutableArray<Diagnostic> Diagnostics
    );

    private sealed record GeneratedHook(string AssemblyName, string TypeName);

    private sealed record MiddlewareDeclaration(
        string TypeName,
        string StableIdentity,
        int Priority,
        string? TargetFunction
    )
    {
        public MiddlewareKey DuplicateKey => new(TargetFunction, Priority, StableIdentity);
    }

    private readonly record struct MiddlewareKey(string? TargetFunction, int Priority, string StableIdentity)
    {
        public override string ToString() =>
            (TargetFunction is null ? "global" : "target:" + TargetFunction)
            + "|"
            + Priority.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "|"
            + StableIdentity;
    }
}
