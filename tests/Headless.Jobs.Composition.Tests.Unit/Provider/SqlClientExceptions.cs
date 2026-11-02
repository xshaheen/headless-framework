// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Tests.Provider;

/// <summary>
/// Builds a genuine <see cref="SqlException" /> carrying several errors. SqlClient exposes no public constructor, so
/// the errors, their collection, and the exception are assembled through the internal members the driver itself
/// uses; the exception's <c>Number</c> is the first error's, as on the wire.
/// </summary>
internal static class SqlClientExceptions
{
    private const BindingFlags _NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static SqlException Create(params int[] numbers)
    {
        var errorConstructor =
            typeof(SqlError)
                .GetConstructors(_NonPublicInstance)
                .FirstOrDefault(candidate =>
                {
                    var parameters = candidate.GetParameters();

                    return parameters.Length == 9
                        && parameters[7].ParameterType == typeof(int)
                        && parameters[8].ParameterType == typeof(Exception);
                })
            ?? throw new InvalidOperationException("Microsoft.Data.SqlClient no longer exposes the SqlError shape.");

        var errors =
            (SqlErrorCollection?)
                Activator.CreateInstance(
                    typeof(SqlErrorCollection),
                    _NonPublicInstance,
                    binder: null,
                    args: null,
                    culture: null
                )
            ?? throw new InvalidOperationException("Microsoft.Data.SqlClient no longer exposes SqlErrorCollection.");
        var add =
            typeof(SqlErrorCollection).GetMethod("Add", _NonPublicInstance)
            ?? throw new InvalidOperationException(
                "Microsoft.Data.SqlClient no longer exposes SqlErrorCollection.Add."
            );

        foreach (var number in numbers)
        {
            var error = errorConstructor.Invoke([
                number,
                (byte)1,
                (byte)16,
                "headless-tests",
                $"fake SQL Server error {number}",
                string.Empty,
                1,
                0,
                null,
            ]);

            add.Invoke(errors, [error]);
        }

        var create =
            typeof(SqlException).GetMethod(
                "CreateException",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                [typeof(SqlErrorCollection), typeof(string)],
                modifiers: null
            )
            ?? throw new InvalidOperationException("Microsoft.Data.SqlClient no longer exposes SqlException factory.");

        return (SqlException)create.Invoke(null, [errors, "17.00.0000"])!;
    }
}
