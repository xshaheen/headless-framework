// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// What the analysis of one compiled query proved: the query reads one table, and every row it can return holds
/// the listed values in the listed columns. A value is a constant of the query or one of its parameters, which
/// differs from execution to execution.
/// </summary>
/// <param name="layout">The layout of the entity type the query reads.</param>
/// <param name="conditions">The columns and the values they must hold; at least one.</param>
internal sealed class LiveQueryShape(LiveRowLayout layout, LiveQueryShape.Condition[] conditions)
{
    /// <summary>The dependency key of the one table the query reads.</summary>
    public string Key => layout.Key!;

    /// <summary>
    /// Returns the filter for one execution, or <see langword="null" /> when a parameter's value cannot be read
    /// from <paramref name="command" /> for certain. The caller then records the whole table.
    /// </summary>
    public LiveRowFilter? Bind(DbCommand command, ISqlGenerationHelper sql)
    {
        var bound = new (int Index, object Value)[conditions.Length];

        for (var index = 0; index < bound.Length; index++)
        {
            var condition = conditions[index];
            var value = condition.Value;

            if (
                condition.Parameter is { } parameter
                && (
                    _FindParameter(command, sql, parameter) is not { } sent
                    || !LiveRowValue.TryConvert(sent, condition.ColumnType, out value)
                )
            )
            {
                return null;
            }

            bound[index] = (condition.Index, value!);
        }

        return new LiveRowFilter(layout.EntityType, bound);
    }

    // The parameter is looked up by the exact name EF Core gives it, bare or with the provider's prefix. EF Core
    // leaves a parameter out when it wrote its value into the SQL (a null becomes IS NULL), and anything but exactly
    // one match with a value leaves the comparison unknown.
    private static object? _FindParameter(DbCommand command, ISqlGenerationHelper sql, string name)
    {
        var prefixed = sql.GenerateParameterName(name);
        DbParameter? match = null;

        foreach (DbParameter parameter in command.Parameters)
        {
            if (
                !string.Equals(parameter.ParameterName, name, StringComparison.Ordinal)
                && !string.Equals(parameter.ParameterName, prefixed, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = parameter;
        }

        return match?.Value is { } value and not DBNull ? value : null;
    }

    /// <summary>One column and the value it must hold.</summary>
    /// <param name="Index">The column's position in the layout.</param>
    /// <param name="ColumnType">The column's CLR type.</param>
    /// <param name="Value">The value, already of the column's type, when the query states it as a constant.</param>
    /// <param name="Parameter">The name of the query parameter that carries the value otherwise.</param>
    internal readonly record struct Condition(int Index, Type ColumnType, object? Value, string? Parameter);
}
