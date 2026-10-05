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
    public const string UnitProperty = "Unit";

    /// <summary>
    /// Diagnostic property holding the enlisted receiver to write after <c>unit.</c>, such as <c>Outbox</c>. Present only
    /// when the rewritten call binds, so the code fix offers itself exactly when the swap is one-to-one.
    /// </summary>
    public const string ReceiverProperty = "Receiver";

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

        var rules = ImmutableArray.CreateBuilder<(ReceiverRule Rule, INamedTypeSymbol Receiver)>();

        foreach (var rule in ReceiverRule.All)
        {
            if (context.Compilation.GetTypeByMetadataName(rule.ReceiverMetadataName) is { } receiver)
            {
                rules.Add((rule, receiver));
            }
        }

        if (rules.Count == 0)
        {
            return;
        }

        var resolved = rules.ToImmutable();

        context.RegisterOperationAction(
            operationContext => _AnalyzeInvocation(operationContext, resolved, unitOfWorkType),
            OperationKind.Invocation
        );
    }

    private static void _AnalyzeInvocation(
        OperationAnalysisContext context,
        ImmutableArray<(ReceiverRule Rule, INamedTypeSymbol Receiver)> rules,
        INamedTypeSymbol unitOfWorkType
    )
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (
            invocation.Syntax is not InvocationExpressionSyntax syntax
            || _ExtendedType(invocation.TargetMethod) is not INamedTypeSymbol extendedType
        )
        {
            return;
        }

        var rule = _Match(rules, invocation.TargetMethod.Name, extendedType);

        if (rule is null || invocation.SemanticModel is not { } model)
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

        if (_CanRewrite(rule, extendedType, model, syntax))
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

    private static ReceiverRule? _Match(
        ImmutableArray<(ReceiverRule Rule, INamedTypeSymbol Receiver)> rules,
        string memberName,
        INamedTypeSymbol extendedType
    )
    {
        foreach (var (rule, receiver) in rules)
        {
            if (
                rule.Members.Contains(memberName)
                && SymbolEqualityComparer.Default.Equals(extendedType.OriginalDefinition, receiver)
            )
            {
                return rule;
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
        INamedTypeSymbol extendedType,
        SemanticModel model,
        InvocationExpressionSyntax call
    )
    {
        if (rule.EnlistedMetadataName is null || call.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var enlistedType = rule.EnlistedIsReceiver
            ? extendedType
            : model.Compilation.GetTypeByMetadataName(rule.EnlistedMetadataName);

        if (enlistedType is null)
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
}
