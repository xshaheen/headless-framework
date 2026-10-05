// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.UnitOfWork.Analyzers;

/// <summary>
/// Finds the unit of work in scope at a call: an <c>IUnitOfWork</c> local or parameter the call can see and still
/// write through. Scope is lexical because there is no ambient current unit; the explicit handle is the unit's only
/// identity.
/// </summary>
internal static class UnitScope
{
    private static readonly HashSet<string> _CompletionMembers = new(StringComparer.Ordinal)
    {
        "CompleteAsync",
        "RollbackAsync",
        "Dispose",
        "DisposeAsync",
    };

    private static readonly HashSet<string> _CallbackMembers = new(StringComparer.Ordinal)
    {
        "OnCompleted",
        "OnFailed",
    };

    /// <summary>Returns the innermost eligible unit at <paramref name="call"/>, or <see langword="null"/> when none is.</summary>
    public static ISymbol? Find(
        SemanticModel model,
        InvocationExpressionSyntax call,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        var position = call.SpanStart;
        ISymbol? chosen = null;
        var chosenPosition = -1;

        foreach (var symbol in model.LookupSymbols(position))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_IsUnitOfWork(symbol, unitOfWorkType, out var declaration) || declaration.SpanStart <= chosenPosition)
            {
                continue;
            }

            if (_IsEligible(model, call, symbol, declaration, unitOfWorkType, cancellationToken))
            {
                chosen = symbol;
                chosenPosition = declaration.SpanStart;
            }
        }

        return chosen;
    }

    private static bool _IsUnitOfWork(ISymbol symbol, INamedTypeSymbol unitOfWorkType, out SyntaxNode declaration)
    {
        declaration = null!;

        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            // An out parameter is unassigned on entry, and a primary-constructor parameter is type state, like a field.
            IParameterSymbol { RefKind: not RefKind.Out } parameter when !_IsPrimaryConstructorParameter(parameter) =>
                parameter.Type,
            _ => null,
        };

        if (
            type is null
            || !SymbolEqualityComparer.Default.Equals(
                type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                unitOfWorkType
            )
        )
        {
            return false;
        }

        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
        {
            return false;
        }

        declaration = reference.GetSyntax();
        return true;
    }

    private static bool _IsEligible(
        SemanticModel model,
        InvocationExpressionSyntax call,
        ISymbol unit,
        SyntaxNode declaration,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        // LookupSymbols returns every local of the enclosing block, including one declared after the call.
        if (unit is ILocalSymbol && declaration.Span.End > call.SpanStart)
        {
            return false;
        }

        if (_CrossesBoundary(model, call, declaration, unitOfWorkType, cancellationToken))
        {
            return false;
        }

        if (unit is ILocalSymbol && !_IsDefinitelyAssigned(model, call, declaration, unit))
        {
            return false;
        }

        if (_CompletedBefore(model, call, unit, cancellationToken))
        {
            return false;
        }

        var flowState = model
            .GetSpeculativeTypeInfo(
                call.SpanStart,
                SyntaxFactory.IdentifierName(unit.Name),
                SpeculativeBindingOption.BindAsExpression
            )
            .Nullability.FlowState;

        // None means nullable analysis is off at the call and cannot tell, so the unit counts.
        return flowState is NullableFlowState.NotNull or NullableFlowState.None;
    }

    /// <summary>
    /// Whether a function between the call and the unit's declaration hides the unit: a static lambda or local function
    /// cannot capture it, and a callback passed to <c>OnCompleted</c> or <c>OnFailed</c> runs after the transaction has
    /// ended, where the enlisted receiver refuses the write.
    /// </summary>
    private static bool _CrossesBoundary(
        SemanticModel model,
        InvocationExpressionSyntax call,
        SyntaxNode declaration,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        foreach (var ancestor in call.Ancestors())
        {
            if (ancestor is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            {
                continue;
            }

            if (ancestor.Span.Contains(declaration.Span))
            {
                return false;
            }

            if (_IsStatic(ancestor) || _IsCompletionCallback(model, ancestor, unitOfWorkType, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool _IsStatic(SyntaxNode function) =>
        function switch
        {
            AnonymousFunctionExpressionSyntax lambda => lambda.Modifiers.Any(SyntaxKind.StaticKeyword),
            LocalFunctionStatementSyntax local => local.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };

    private static bool _IsCompletionCallback(
        SemanticModel model,
        SyntaxNode function,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        if (function is AnonymousFunctionExpressionSyntax)
        {
            return function.Parent is ArgumentSyntax argument
                && _IsCallbackRegistration(model, argument, unitOfWorkType, cancellationToken);
        }

        if (function is not LocalFunctionStatementSyntax localFunction)
        {
            return false;
        }

        var localSymbol = model.GetDeclaredSymbol(localFunction, cancellationToken);
        var member = localFunction.Ancestors().FirstOrDefault(node => node is MemberDeclarationSyntax);

        if (localSymbol is null || member is null)
        {
            return false;
        }

        // A local function registered as a method group, such as unit.OnCompleted(NotifyAsync), is a callback too.
        foreach (var identifier in member.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (
                identifier.Parent is ArgumentSyntax argument
                && string.Equals(
                    identifier.Identifier.ValueText,
                    localFunction.Identifier.ValueText,
                    StringComparison.Ordinal
                )
                && SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    localSymbol
                )
                && _IsCallbackRegistration(model, argument, unitOfWorkType, cancellationToken)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool _IsCallbackRegistration(
        SemanticModel model,
        ArgumentSyntax argument,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        if (argument.Parent?.Parent is not InvocationExpressionSyntax registration)
        {
            return false;
        }

        return model.GetSymbolInfo(registration, cancellationToken).Symbol is IMethodSymbol method
            && _CallbackMembers.Contains(method.Name)
            && (
                SymbolEqualityComparer.Default.Equals(method.ContainingType, unitOfWorkType)
                || method.ContainingType.AllInterfaces.Contains(unitOfWorkType, SymbolEqualityComparer.Default)
            );
    }

    private static bool _IsDefinitelyAssigned(
        SemanticModel model,
        InvocationExpressionSyntax call,
        SyntaxNode declaration,
        ISymbol local
    )
    {
        // Inside a lambda or local function the analysis does not treat a captured local as assigned on entry. The local
        // must instead be assigned where the outermost function between the call and the declaration is created.
        SyntaxNode region = call;

        foreach (var ancestor in call.Ancestors())
        {
            if (ancestor.Span.Contains(declaration.Span))
            {
                break;
            }

            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                region = ancestor;
            }
        }

        var dataFlow = region switch
        {
            StatementSyntax statement => model.AnalyzeDataFlow(statement),
            ExpressionSyntax expression => model.AnalyzeDataFlow(expression),
            _ => null,
        };

        // When the region cannot be analyzed, do not drop the unit on a guess.
        return dataFlow is not { Succeeded: true }
            || dataFlow.DefinitelyAssignedOnEntry.Contains(local, SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// Whether a statement that runs on every path to the call has already completed, rolled back, or disposed the unit.
    /// Only statements that sit directly in a block enclosing the call count; a rollback inside an early-exit branch
    /// leaves the unit live on the path that reaches the call.
    /// </summary>
    private static bool _CompletedBefore(
        SemanticModel model,
        InvocationExpressionSyntax call,
        ISymbol unit,
        CancellationToken cancellationToken
    )
    {
        SyntaxNode current = call;

        foreach (var ancestor in call.Ancestors())
        {
            if (
                ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax
            )
            {
                return false;
            }

            var siblings = ancestor switch
            {
                BlockSyntax block => block.Statements,
                SwitchSectionSyntax section => section.Statements,
                _ => default(SyntaxList<StatementSyntax>?),
            };

            if (siblings is { } statements)
            {
                foreach (var statement in statements)
                {
                    if (statement.SpanStart >= current.SpanStart)
                    {
                        break;
                    }

                    if (_IsCompletionStatement(model, statement, unit, cancellationToken))
                    {
                        return true;
                    }
                }
            }

            current = ancestor;
        }

        return false;
    }

    private static bool _IsCompletionStatement(
        SemanticModel model,
        StatementSyntax statement,
        ISymbol unit,
        CancellationToken cancellationToken
    )
    {
        if (statement is not ExpressionStatementSyntax { Expression: var expression })
        {
            return false;
        }

        if (expression is AwaitExpressionSyntax awaited)
        {
            expression = awaited.Expression;
        }

        if (
            expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configureAwait,
            }
        )
        {
            expression = configureAwait.Expression;
        }

        return expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            && _CompletionMembers.Contains(memberAccess.Name.Identifier.ValueText)
            && SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol,
                unit
            );
    }

    private static bool _IsPrimaryConstructorParameter(IParameterSymbol parameter) =>
        parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
        && constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax);
}
