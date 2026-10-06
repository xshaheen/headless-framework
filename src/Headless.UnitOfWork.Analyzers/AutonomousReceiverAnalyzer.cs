// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Headless.UnitOfWork.Analyzers;

/// <summary>
/// Reports a call to an autonomous receiver, such as <c>IBus.PublishAsync</c>, made while an <c>IUnitOfWork</c> is in
/// scope, and names the enlisted receiver on that unit that writes inside its transaction instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutonomousReceiverAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic property holding the name of the unit of work in scope.</summary>
    internal const string UnitProperty = "Unit";

    /// <summary>
    /// Diagnostic property holding the enlisted receiver to write after <c>unit.</c>, such as <c>Outbox</c>. Present only
    /// when the rewritten call binds, so the code fix offers itself exactly when the swap is one-to-one.
    /// </summary>
    internal const string ReceiverProperty = "Receiver";

    private const string _UnitOfWorkMetadataName = "Headless.UnitOfWork.IUnitOfWork";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [.. ReceiverRule.All.Select(rule => rule.Descriptor).Distinct()];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(_OnCompilationStart);
    }

    private static void _OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var unitOfWorkType = context.Compilation.GetTypeByMetadataName(_UnitOfWorkMetadataName);

        if (unitOfWorkType is null)
        {
            return;
        }

        var rules = ImmutableArray.CreateBuilder<ResolvedRule>();

        foreach (var rule in ReceiverRule.All)
        {
            if (context.Compilation.GetTypeByMetadataName(rule.ReceiverMetadataName) is not { } receiver)
            {
                continue;
            }

            // The Jobs accessors return the receiver interface itself, constructed per call, so it is resolved there.
            var enlisted =
                rule.EnlistedMetadataName is null || rule.EnlistedIsReceiver
                    ? null
                    : context.Compilation.GetTypeByMetadataName(rule.EnlistedMetadataName);

            rules.Add(new ResolvedRule(rule, receiver, enlisted));
        }

        if (rules.Count == 0)
        {
            return;
        }

        var resolved = rules.ToImmutable();
        var memberNames = resolved.SelectMany(entry => entry.Rule.Members).ToImmutableHashSet(StringComparer.Ordinal);

        context.RegisterOperationAction(
            operationContext => _AnalyzeInvocation(operationContext, resolved, memberNames, unitOfWorkType),
            OperationKind.Invocation
        );
    }

    private static void _AnalyzeInvocation(
        OperationAnalysisContext context,
        ImmutableArray<ResolvedRule> rules,
        ImmutableHashSet<string> memberNames,
        INamedTypeSymbol unitOfWorkType
    )
    {
        var invocation = (IInvocationOperation)context.Operation;

        // Every invocation in the compilation reaches this point, so reject on the member name before any symbol work.
        if (
            !memberNames.Contains(invocation.TargetMethod.Name)
            || invocation.Syntax is not InvocationExpressionSyntax syntax
            || _ExtendedType(invocation.TargetMethod) is not INamedTypeSymbol extendedType
        )
        {
            return;
        }

        var match = _Match(rules, invocation.TargetMethod.Name, extendedType);

        if (match is null || invocation.SemanticModel is not { } model)
        {
            return;
        }

        var rule = match.Rule;

        if (
            rule.EnlistedIsReceiver
            && EnlistedReceiver.IsCalledOn(invocation, rule, unitOfWorkType, context.CancellationToken)
        )
        {
            return;
        }

        var unit = UnitScope.Find(model, syntax, unitOfWorkType, context.CancellationToken);

        if (unit is null)
        {
            return;
        }

        var receiverDisplay = _ReceiverText(rule, extendedType, model, syntax.SpanStart);
        var properties = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        properties[UnitProperty] = unit.Name;

        var enlistedType = rule.EnlistedIsReceiver ? extendedType : match.Enlisted;

        if (enlistedType is not null && _CanRewrite(rule, enlistedType, extendedType, model, syntax))
        {
            properties[ReceiverProperty] = receiverDisplay;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                rule.Descriptor,
                syntax.GetLocation(),
                properties.ToImmutable(),
                unit.Name,
                unit.Name + "." + receiverDisplay
            )
        );
    }

    private static ResolvedRule? _Match(
        ImmutableArray<ResolvedRule> rules,
        string memberName,
        INamedTypeSymbol extendedType
    )
    {
        foreach (var entry in rules)
        {
            if (
                entry.Rule.Members.Contains(memberName)
                && SymbolEqualityComparer.Default.Equals(extendedType.OriginalDefinition, entry.Receiver)
            )
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// The type a member is called on: the receiver parameter of a classic or C# 14 extension member, otherwise the
    /// declaring type. Matching on it, not on the call's instance, keeps a call through a concrete implementation, such as
    /// the enlisted <c>UnitOfWorkOutbox</c>, out of the rules.
    /// </summary>
    private static ITypeSymbol? _ExtendedType(IMethodSymbol method)
    {
        if (method.IsExtensionMethod)
        {
            // The operation tree carries the unreduced method, whose first parameter is the receiver.
            return method.ReducedFrom is null ? method.Parameters.FirstOrDefault()?.Type : method.ReceiverType;
        }

        if (method.ContainingType is { IsExtension: true } extension)
        {
            return extension.ExtensionParameter?.Type;
        }

        return method.ContainingType;
    }

    private static string _ReceiverText(
        ReceiverRule rule,
        INamedTypeSymbol extendedType,
        SemanticModel model,
        int position
    )
    {
        if (rule.AccessorShape == AccessorShape.Property || extendedType.TypeArguments.Length == 0)
        {
            return rule.Accessor;
        }

        return rule.Accessor + "<" + extendedType.TypeArguments[0].ToMinimalDisplayString(model, position) + ">()";
    }

    /// <summary>
    /// Whether the same call binds on the enlisted receiver: the call's own arguments, with its receiver replaced by an
    /// expression of the enlisted type. An overload the enlisted receiver lacks, such as the <c>PublishOptions</c>
    /// overload, fails to bind and gets the diagnostic without a fix.
    /// </summary>
    private static bool _CanRewrite(
        ReceiverRule rule,
        ITypeSymbol enlistedType,
        INamedTypeSymbol extendedType,
        SemanticModel model,
        InvocationExpressionSyntax call
    )
    {
        if (rule.EnlistedMetadataName is null || call.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (
            rule.AccessorShape == AccessorShape.GenericMethod
            && (extendedType.TypeArguments.Length == 0 || !_SatisfiesNewConstraint(extendedType.TypeArguments[0]))
        )
        {
            return false;
        }

        var typedReceiver = SyntaxFactory.ParseExpression(
            "((" + enlistedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")null!)"
        );
        var rewritten = call.ReplaceNode(memberAccess.Expression, typedReceiver);

        return model
                .GetSpeculativeSymbolInfo(call.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression)
                .Symbol is IMethodSymbol;
    }

    /// <summary>
    /// Whether a type argument satisfies the <c>new()</c> constraint the generic Jobs accessors add over the manager
    /// interfaces: a concrete type with a public parameterless constructor and no <see langword="required"/> member left unset.
    /// </summary>
    private static bool _SatisfiesNewConstraint(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            return parameter.HasConstructorConstraint || parameter.HasValueTypeConstraint;
        }

        if (type is not INamedTypeSymbol { IsAbstract: false } named)
        {
            return false;
        }

        var constructor = named.InstanceConstructors.FirstOrDefault(candidate =>
            candidate.Parameters.Length == 0 && candidate.DeclaredAccessibility == Accessibility.Public
        );

        if (constructor is null)
        {
            return false;
        }

        if (
            constructor
                .GetAttributes()
                .Any(attribute =>
                    string.Equals(
                        attribute.AttributeClass?.Name,
                        "SetsRequiredMembersAttribute",
                        StringComparison.Ordinal
                    )
                )
        )
        {
            return true;
        }

        for (
            ITypeSymbol? current = named;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            if (
                current
                    .GetMembers()
                    .Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A rule with its receiver type, and its enlisted type when that differs from the receiver.</summary>
    private sealed record ResolvedRule(ReceiverRule Rule, INamedTypeSymbol Receiver, INamedTypeSymbol? Enlisted);
}
