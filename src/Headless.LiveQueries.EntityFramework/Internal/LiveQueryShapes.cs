// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Keeps the shape of each query EF Core compiled, and marks the query's SQL with a comment that names the shape, so
/// every later execution of the SQL finds it.
/// </summary>
/// <remarks>
/// The shapes belong to the model. EF Core compiles a query once per model and reuses the SQL in every context of
/// that model, across hosts in one process too, so a store per host would hand one host SQL marked by another. A
/// shape's number is never used twice in the process: a mark made for one model finds nothing in another's store.
/// </remarks>
internal static class LiveQueryShapes
{
    // What one model keeps. A query is compiled again after EF Core evicts it, so the count can only grow; past the
    // limit a newly compiled query gets no shape and matches by table, and the shapes in use keep working.
    private const int _MaxShapesPerModel = 4096;

    private const string _Mark = "live-query-shape:";

    private static readonly MethodInfo _TagWith = typeof(EntityFrameworkQueryableExtensions)
        .GetMethods()
        .Single(method =>
            string.Equals(method.Name, nameof(EntityFrameworkQueryableExtensions.TagWith), StringComparison.Ordinal)
        );

    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<long, LiveQueryShape>> _Stores = [];
    private static long _lastId;

    /// <summary>
    /// Analyzes <paramref name="query" /> and returns it marked when it has a shape; returns it unchanged otherwise.
    /// </summary>
    public static Expression Register(IModel model, Expression query)
    {
        var shapes = _Stores.GetValue(model, static _ => new ConcurrentDictionary<long, LiveQueryShape>());

        if (shapes.Count >= _MaxShapesPerModel || LiveQueryShapeAnalyzer.Analyze(model, query) is not { } shape)
        {
            return query;
        }

        var id = Interlocked.Increment(ref _lastId);
        shapes[id] = shape;

        return new Marker(_Mark + id.ToString(CultureInfo.InvariantCulture)).Visit(query);
    }

    /// <summary>Returns the shape <paramref name="commandText" /> is marked with, if it is one of this model's.</summary>
    public static LiveQueryShape? Find(IModel model, string commandText)
    {
        var start = commandText.IndexOf(_Mark, StringComparison.Ordinal);

        if (start < 0 || !_Stores.TryGetValue(model, out var shapes))
        {
            return null;
        }

        start += _Mark.Length;
        var end = start;

        while (end < commandText.Length && char.IsAsciiDigit(commandText[end]))
        {
            end++;
        }

        return
            long.TryParse(commandText.AsSpan(start, end - start), CultureInfo.InvariantCulture, out var id)
            && shapes.TryGetValue(id, out var shape)
            ? shape
            : null;
    }

    /// <summary>Wraps the query's one root in <c>TagWith</c>, which EF Core writes as a comment above the SQL.</summary>
    private sealed class Marker(string mark) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            return node is EntityQueryRootExpression root
                ? Expression.Call(_TagWith.MakeGenericMethod(root.ElementType), root, Expression.Constant(mark))
                : base.VisitExtension(node);
        }
    }
}
