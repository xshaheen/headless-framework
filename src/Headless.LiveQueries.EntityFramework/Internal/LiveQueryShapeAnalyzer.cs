// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Decides whether a query's filter bounds the rows its result depends on, and extracts that filter. It accepts
/// only a shape it recognizes from end to end: one chain of known operators over one table, with nothing that
/// reads a second row source. Every other query gets no shape and is matched by table.
/// </summary>
/// <remarks>
/// The analysis runs before EF Core expands the query, so it cannot see what EF Core adds afterwards. That is why
/// an entity type with a navigation loaded by default has no layout, why the type's query filters are checked here,
/// and why the caller still compares the tables the final SQL names with the shape's one table.
/// </remarks>
internal static class LiveQueryShapeAnalyzer
{
    // Operators that keep or drop whole rows of the source without looking at any other row. A filter found under
    // them still bounds the rows the result depends on.
    private static readonly FrozenSet<string> _RowPreserving = FrozenSet.ToFrozenSet(
        [
            nameof(Queryable.OrderBy),
            nameof(Queryable.OrderByDescending),
            nameof(Queryable.ThenBy),
            nameof(Queryable.ThenByDescending),
            nameof(EntityFrameworkQueryableExtensions.AsNoTracking),
            nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution),
            nameof(EntityFrameworkQueryableExtensions.AsTracking),
            nameof(EntityFrameworkQueryableExtensions.TagWith),
            nameof(EntityFrameworkQueryableExtensions.TagWithCallSite),
            nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes),
            nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters),
            nameof(RelationalQueryableExtensions.AsSplitQuery),
            nameof(RelationalQueryableExtensions.AsSingleQuery),
        ],
        StringComparer.Ordinal
    );

    // Operators whose one-argument lambda keeps the rows it holds for and drops the rest.
    private static readonly FrozenSet<string> _Filtering = FrozenSet.ToFrozenSet(
        [
            nameof(Queryable.Where),
            nameof(Queryable.Count),
            nameof(Queryable.LongCount),
            nameof(Queryable.Any),
            nameof(Queryable.First),
            nameof(Queryable.FirstOrDefault),
            nameof(Queryable.Single),
            nameof(Queryable.SingleOrDefault),
            nameof(Queryable.Last),
            nameof(Queryable.LastOrDefault),
        ],
        StringComparer.Ordinal
    );

    // Operators that read only the rows they are given, and whose output a later filter no longer bounds: the
    // first row of a page depends on rows a filter applied after paging would drop.
    private static readonly FrozenSet<string> _Reshaping = FrozenSet.ToFrozenSet(
        [
            nameof(Queryable.Select),
            nameof(Queryable.Skip),
            nameof(Queryable.Take),
            nameof(Queryable.Distinct),
            nameof(Queryable.Sum),
            nameof(Queryable.Min),
            nameof(Queryable.Max),
            nameof(Queryable.Average),
        ],
        StringComparer.Ordinal
    );

    /// <summary>Returns the shape of <paramref name="query" />, or <see langword="null" /> when it has none.</summary>
    public static LiveQueryShape? Analyze(IModel model, Expression query)
    {
        List<MethodCallExpression> operators = [];
        var source = query;

        while (source is MethodCallExpression call)
        {
            if (!_IsKnownOperator(call))
            {
                return null;
            }

            operators.Add(call);
            source = call.Arguments[0];
        }

        // The plain root only: a root over raw SQL or a function reads whatever that says.
        if (source is not EntityQueryRootExpression root || root.GetType() != typeof(EntityQueryRootExpression))
        {
            return null;
        }

        var entityType = root.EntityType;
        var layout = LiveRowLayout.For(entityType);

        if (!layout.IsMatchable)
        {
            return null;
        }

        var reads = new ReadFinder(model, entityType);

        // EF Core adds these to the query later. One that reads only the row itself narrows the result further,
        // which a filter may ignore; one that reads anything else makes the result depend on rows no filter names.
        if (entityType.GetDeclaredQueryFilters().Any(filter => filter.Expression is { } own && reads.Finds(own)))
        {
            return null;
        }

        List<LiveQueryShape.Condition> conditions = [];
        var bounded = true;

        // From the root outwards, which is the order the operators apply in.
        for (var index = operators.Count - 1; index >= 0; index--)
        {
            var call = operators[index];

            if (call.Arguments.Skip(1).Any(reads.Finds))
            {
                return null;
            }

            var name = call.Method.Name;

            if (_RowPreserving.Contains(name))
            {
                continue;
            }

            if (_Filtering.Contains(name) && call.Arguments.Count == 2)
            {
                // A predicate that also takes the row's position depends on every row before it.
                if (_Unquote(call.Arguments[1]) is not { Parameters.Count: 1 } predicate)
                {
                    return null;
                }

                if (bounded)
                {
                    _AddConditions(predicate, entityType, layout, conditions);
                }
            }

            // Anything but Where ends the chain or reshapes it.
            bounded &= name is nameof(Queryable.Where);
        }

        return conditions.Count == 0 ? null : new LiveQueryShape(layout, [.. conditions]);
    }

    private static bool _IsKnownOperator(MethodCallExpression call)
    {
        var owner = call.Method.DeclaringType;
        var name = call.Method.Name;

        return call.Arguments.Count >= 1
            && (
                owner == typeof(Queryable)
                || owner == typeof(EntityFrameworkQueryableExtensions)
                || owner == typeof(RelationalQueryableExtensions)
            )
            && (_RowPreserving.Contains(name) || _Filtering.Contains(name) || _Reshaping.Contains(name));
    }

    private static LambdaExpression? _Unquote(Expression argument)
    {
        return argument is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted }
            ? quoted
            : argument as LambdaExpression;
    }

    // A filter is the conjunction of its parts, and so is a row of filters. A part that is not understood is left
    // out, which only widens the set of rows the filter admits.
    private static void _AddConditions(
        LambdaExpression predicate,
        IEntityType entityType,
        LiveRowLayout layout,
        List<LiveQueryShape.Condition> conditions
    )
    {
        var row = predicate.Parameters[0];
        Stack<Expression> parts = new([predicate.Body]);

        while (parts.TryPop(out var part))
        {
            if (part is BinaryExpression { NodeType: ExpressionType.AndAlso } both)
            {
                parts.Push(both.Left);
                parts.Push(both.Right);
            }
            else if (_TryReadCondition(part, row, entityType, layout) is { } condition)
            {
                conditions.Add(condition);
            }
        }
    }

    private static LiveQueryShape.Condition? _TryReadCondition(
        Expression part,
        ParameterExpression row,
        IEntityType entityType,
        LiveRowLayout layout
    )
    {
        return part switch
        {
            BinaryExpression { NodeType: ExpressionType.Equal } equal => _TryReadEquality(
                equal.Left,
                equal.Right,
                row,
                entityType,
                layout
            ) ?? _TryReadEquality(equal.Right, equal.Left, row, entityType, layout),
            UnaryExpression { NodeType: ExpressionType.Not, Method: null } not => _TryReadFlag(
                not.Operand,
                held: false,
                row,
                entityType,
                layout
            ),
            _ => _TryReadFlag(part, held: true, row, entityType, layout),
        };
    }

    // A bool column used as the condition itself.
    private static LiveQueryShape.Condition? _TryReadFlag(
        Expression part,
        bool held,
        ParameterExpression row,
        IEntityType entityType,
        LiveRowLayout layout
    )
    {
        return
            part.Type == typeof(bool)
            && _FindColumn(part, row, entityType) is { } column
            && layout.IndexOf(column) is >= 0 and var index
            ? new LiveQueryShape.Condition(index, column.ClrType, held, Parameter: null)
            : null;
    }

    private static LiveQueryShape.Condition? _TryReadEquality(
        Expression columnSide,
        Expression valueSide,
        ParameterExpression row,
        IEntityType entityType,
        LiveRowLayout layout
    )
    {
        if (
            _FindColumn(columnSide, row, entityType) is not { } column
            || layout.IndexOf(column) is not (>= 0 and var index)
        )
        {
            return null;
        }

        // The compiler lifts a value to the column's nullable type and compares enums as numbers. The value is
        // brought back to the column's own type when it is bound, so a conversion that loses nothing may go.
        while (
            valueSide is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } convert
            && LiveRowValue.IsLossless(convert.Operand.Type, convert.Type)
        )
        {
            valueSide = convert.Operand;
        }

        return valueSide switch
        {
            ConstantExpression { Value: { } value }
                when LiveRowValue.TryConvert(value, column.ClrType, out var converted) => new LiveQueryShape.Condition(
                index,
                column.ClrType,
                converted,
                Parameter: null
            ),
            QueryParameterExpression parameter => new LiveQueryShape.Condition(
                index,
                column.ClrType,
                Value: null,
                parameter.Name
            ),
            _ => null,
        };
    }

    // The column an expression reads from the row: a property of the row, by member or by name, under conversions
    // that lose nothing. A narrowing conversion compares another value than the column holds.
    private static IProperty? _FindColumn(Expression expression, ParameterExpression row, IEntityType entityType)
    {
        while (
            expression is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } convert
            && LiveRowValue.IsLossless(convert.Operand.Type, convert.Type)
        )
        {
            expression = convert.Operand;
        }

        return expression switch
        {
            MemberExpression member when member.Expression == row => entityType.FindProperty(member.Member),
            MethodCallExpression call
                when _IsPropertyByName(call)
                    && call.Arguments[0] == row
                    && call.Arguments[1] is ConstantExpression { Value: string name } => entityType.FindProperty(name),
            _ => null,
        };
    }

    private static bool _IsPropertyByName(MethodCallExpression call)
    {
        return call.Method.DeclaringType == typeof(EF) && call.Method.Name is nameof(EF.Property);
    }

    /// <summary>
    /// Finds anything in a lambda or an argument that reads beyond the row it is given, or that the analysis does
    /// not know: a second query, a navigation, a property read by name that may be one.
    /// </summary>
    private sealed class ReadFinder(IModel model, IEntityType entityType) : ExpressionVisitor
    {
        private bool _found;

        public bool Finds(Expression expression)
        {
            _found = false;
            Visit(expression);

            return _found;
        }

        public override Expression? Visit(Expression? node)
        {
            if (node is null || _found)
            {
                return node;
            }

            // A query inside the query, whatever builds it.
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                _found = true;

                return node;
            }

            return base.Visit(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            // A captured value is the only extension node that is known to read nothing.
            _found |= node is not QueryParameterExpression;

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _found |= _IsEntity(node.Type);

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            _found |= _IsPropertyByName(node)
                ? node.Arguments[1] is not ConstantExpression { Value: string name }
                    || entityType.FindProperty(name) is null
                    || node.Arguments[0].Type != entityType.ClrType
                // A function the model maps to the database runs there, over whatever rows it likes.
                : _IsEntity(node.Type) || model.FindDbFunction(node.Method) is not null;

            return base.VisitMethodCall(node);
        }

        private bool _IsEntity(Type type)
        {
            if (model.FindEntityTypes(type).Any())
            {
                return true;
            }

            if (type == typeof(string))
            {
                return false;
            }

            if (type.IsArray)
            {
                return model.FindEntityTypes(type.GetElementType()!).Any();
            }

            return type.GetInterfaces()
                .Append(type)
                .Any(candidate =>
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    && model.FindEntityTypes(candidate.GetGenericArguments()[0]).Any()
                );
        }
    }
}
