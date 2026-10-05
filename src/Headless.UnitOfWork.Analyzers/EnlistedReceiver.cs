// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Headless.UnitOfWork.Analyzers;

/// <summary>
/// Recognizes a call already made through a unit's enlisted receiver. The Jobs accessors (<c>unit.Jobs</c>,
/// <c>unit.TimeJobs&lt;T&gt;()</c>, <c>unit.CronJobs&lt;T&gt;()</c>) return the same interfaces the rule flags, so the
/// receiver expression, not its type, tells an enlisted call from an autonomous one.
/// </summary>
internal static class EnlistedReceiver
{
    /// <summary>
    /// Whether the call's receiver is the rule's unit accessor, directly or through a local initialized once from it and
    /// never reassigned. A receiver passed in from elsewhere cannot be traced and is treated as autonomous.
    /// </summary>
    public static bool IsCalledOn(
        IInvocationOperation invocation,
        ReceiverRule rule,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        var receiver = _Unwrap(_Receiver(invocation));

        if (_IsAccessor(receiver, rule, unitOfWorkType))
        {
            return true;
        }

        return receiver is ILocalReferenceOperation { Local: var local }
            && invocation.SemanticModel is { } model
            && _IsInitializedOnceFromAccessor(model, local, rule, unitOfWorkType, cancellationToken);
    }

    private static IOperation? _Receiver(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance;
        }

        // A classic extension method carries its receiver as the first argument.
        return invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0
            ? invocation.Arguments[0].Value
            : null;
    }

    private static IOperation? _Unwrap(IOperation? operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { IsImplicit: true } conversion:
                    operation = conversion.Operand;
                    break;
                case IConditionalAccessInstanceOperation instance:
                    // unit.Jobs?.EnqueueAsync(...): the receiver is the expression the conditional access tests.
                    var conditional = instance.Parent;

                    while (conditional is not null and not IConditionalAccessOperation)
                    {
                        conditional = conditional.Parent;
                    }

                    operation = (conditional as IConditionalAccessOperation)?.Operation;
                    break;
                default:
                    return operation;
            }
        }
    }

    private static bool _IsAccessor(IOperation? operation, ReceiverRule rule, INamedTypeSymbol unitOfWorkType)
    {
        ISymbol? member = operation switch
        {
            IPropertyReferenceOperation property => property.Property,
            IInvocationOperation call => call.TargetMethod,
            _ => null,
        };

        return member is not null
            && string.Equals(member.Name, rule.Accessor, StringComparison.Ordinal)
            && member.ContainingType is { IsExtension: true, ExtensionParameter.Type: var extended }
            && SymbolEqualityComparer.Default.Equals(
                extended.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                unitOfWorkType
            );
    }

    private static bool _IsInitializedOnceFromAccessor(
        SemanticModel model,
        ILocalSymbol local,
        ReceiverRule rule,
        INamedTypeSymbol unitOfWorkType,
        CancellationToken cancellationToken
    )
    {
        if (
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            is not VariableDeclaratorSyntax { Initializer.Value: var initializer } declarator
        )
        {
            return false;
        }

        if (!_IsAccessor(_Unwrap(model.GetOperation(initializer, cancellationToken)), rule, unitOfWorkType))
        {
            return false;
        }

        var scope =
            declarator.Ancestors().FirstOrDefault(node => node is MemberDeclarationSyntax)
            ?? declarator.SyntaxTree.GetRoot(cancellationToken);

        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!string.Equals(identifier.Identifier.ValueText, local.Name, StringComparison.Ordinal))
            {
                continue;
            }

            var written = identifier.Parent switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left == identifier,
                ArgumentSyntax argument => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                    || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
                _ => false,
            };

            if (
                written
                && SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}
