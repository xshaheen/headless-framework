// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Messaging.SourceGenerator.Models;
using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Validation;

/// <summary>
/// Checks each <c>RequestAsync&lt;TRequest, TResponse&gt;</c> call against the responders the project can see: those it
/// declares and those its references publish in generated metadata.
/// </summary>
/// <remarks>
/// A call is reported only when a visible responder answers <c>TRequest</c> and none answers it with
/// <c>TResponse</c>. A request with no visible responder is the normal case for a caller in another service, so absence
/// is never reported.
/// </remarks>
internal static class RequestCallValidator
{
    private const string _GlobalPrefix = "global::";

    public static EquatableArray<DiagnosticInfo> Validate(
        ImmutableArray<RequestCallModel> calls,
        EquatableArray<ResponderModel> localResponders,
        EquatableArray<ResponderModel> referencedResponders
    )
    {
        if (calls.IsEmpty)
        {
            return EquatableArray<DiagnosticInfo>.Empty;
        }

        var answers = localResponders
            .Concat(referencedResponders)
            .GroupBy(responder => responder.RequestTypeName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                    group.Select(responder => responder.ResponseTypeName).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal
            );

        var diagnostics = new List<DiagnosticInfo>();
        foreach (
            var call in calls
                .OrderBy(call => call.Location?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(call => call.Location?.TextSpan.Start ?? 0)
        )
        {
            if (
                !answers.TryGetValue(call.RequestTypeName, out var responseTypes)
                || responseTypes.Contains(call.ResponseTypeName, StringComparer.Ordinal)
            )
            {
                continue;
            }

            diagnostics.Add(
                new DiagnosticInfo(
                    DiagnosticDescriptors.ResponseTypeMismatch,
                    call.Location,
                    new[]
                    {
                        _Display(call.RequestTypeName),
                        string.Join(
                            ", ",
                            responseTypes
                                .OrderBy(name => name, StringComparer.Ordinal)
                                .Select(name => $"'{_Display(name)}'")
                        ),
                        _Display(call.ResponseTypeName),
                    }.ToEquatableArray()
                )
            );
        }

        return diagnostics.ToEquatableArray();
    }

    private static string _Display(string fullyQualifiedName) =>
        fullyQualifiedName.Replace(_GlobalPrefix, string.Empty);
}
