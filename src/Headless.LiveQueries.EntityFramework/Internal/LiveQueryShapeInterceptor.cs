// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.LiveQueries.Internal;

/// <summary>Hands each query to the shape analysis once, when EF Core compiles it.</summary>
/// <remarks>
/// One instance serves every context, for the reason <see cref="LiveMaterializationInterceptor" /> gives.
/// </remarks>
internal sealed class LiveQueryShapeInterceptor : IQueryExpressionInterceptor
{
    public static readonly LiveQueryShapeInterceptor Instance = new();

    private LiveQueryShapeInterceptor() { }

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        try
        {
            return eventData.Context is { } context
                ? LiveQueryShapes.Register(context.Model, queryExpression)
                : queryExpression;
        }
#pragma warning disable CA1031, ERP022 // The fix would make it worse: the analysis must not fail the application's query. An unmarked query matches by table, which re-runs more and misses nothing.
        catch (Exception)
        {
            return queryExpression;
        }
#pragma warning restore CA1031, ERP022
    }
}
