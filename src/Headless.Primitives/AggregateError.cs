// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Multiple errors occurred. Useful for batch operations.
/// </summary>
[PublicAPI]
public sealed record AggregateError : ApiResultError
{
    /// <summary>The individual errors that were aggregated.</summary>
    public required IReadOnlyList<ApiResultError> Errors
    {
        get;
        init => field = _CopyErrors(value);
    }

    /// <inheritdoc/>
    public override string Code => "aggregate:multiple_errors";

    /// <inheritdoc/>
    public override string Message => $"{Errors.Count} errors occurred.";

    /// <summary>Determines equality from the ordered contained errors.</summary>
    /// <param name="other">The aggregate error to compare with.</param>
    /// <returns><see langword="true"/> when both aggregates contain equal errors in the same order.</returns>
    public bool Equals(AggregateError? other) => other is not null && Errors.SequenceEqual(other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var error in Errors)
        {
            hash.Add(error);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Tries to merge this aggregate when every contained error is a validation error.
    /// </summary>
    /// <param name="errors">The merged field-keyed validation descriptors when successful.</param>
    /// <returns><see langword="true"/> when every contained error is a validation error.</returns>
    public bool TryGetValidationErrors(
        [NotNullWhen(true)] out IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>>? errors
    )
    {
        var merged = new Dictionary<string, List<ErrorDescriptor>>(StringComparer.Ordinal);

        foreach (var error in Errors)
        {
            if (!_TryAppendValidationErrors(error, merged))
            {
                errors = null;
                return false;
            }
        }

        errors = merged.ToDictionary(
            pair => pair.Key,
            IReadOnlyList<ErrorDescriptor> (pair) => pair.Value,
            StringComparer.Ordinal
        );
        return true;
    }

    /// <summary>Flattens the aggregate into structured descriptors for conflict responses.</summary>
    /// <returns>The descriptors carried by all nested errors.</returns>
    public IReadOnlyList<ErrorDescriptor> ToErrorDescriptors()
    {
        var descriptors = new List<ErrorDescriptor>();

        foreach (var error in Errors)
        {
            _AppendErrorDescriptors(error, descriptors);
        }

        return descriptors;
    }

    private static IReadOnlyList<ApiResultError> _CopyErrors(IReadOnlyList<ApiResultError> errors)
    {
        var checkedErrors = Argument.IsNotNullOrEmpty(errors);
        Argument.HasNoNulls(checkedErrors);
        return [.. checkedErrors];
    }

    private static bool _TryAppendValidationErrors(
        ApiResultError error,
        Dictionary<string, List<ErrorDescriptor>> merged
    )
    {
        if (error is AggregateError aggregate)
        {
            return aggregate.Errors.All(item => _TryAppendValidationErrors(item, merged));
        }

        if (error is not ValidationError validation)
        {
            return false;
        }

        foreach (var (field, descriptors) in validation.Errors)
        {
            if (!merged.TryGetValue(field, out var fieldErrors))
            {
                fieldErrors = [];
                merged[field] = fieldErrors;
            }

            fieldErrors.AddRange(descriptors);
        }

        return true;
    }

    private static void _AppendErrorDescriptors(ApiResultError error, List<ErrorDescriptor> descriptors)
    {
        switch (error)
        {
            case AggregateError aggregate:
                foreach (var nested in aggregate.Errors)
                {
                    _AppendErrorDescriptors(nested, descriptors);
                }

                break;
            case ConflictError conflict:
                descriptors.AddRange(conflict.Errors);
                break;
            case ForbiddenError forbidden:
                descriptors.Add(forbidden.Error);
                break;
            case UnauthorizedError { Error: not null } unauthorized:
                descriptors.Add(unauthorized.Error);
                break;
            case ValidationError validation:
                foreach (var validationErrors in validation.Errors.Values)
                {
                    descriptors.AddRange(validationErrors);
                }

                break;
            default:
                descriptors.Add(error.ToErrorDescriptor());
                break;
        }
    }
}
