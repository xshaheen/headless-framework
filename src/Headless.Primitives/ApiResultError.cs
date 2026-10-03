// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Base class for all result errors. Extend this to create domain-specific errors.
/// </summary>
[PublicAPI]
public abstract record ApiResultError
{
    /// <summary>
    /// Machine-readable error code for logging and client handling.
    /// Convention: "namespace:error_name" (e.g., "user:duplicate_email")
    /// </summary>
    public abstract string Code { get; }

    /// <summary>
    /// Human-readable description. Should be localized for end-user display.
    /// </summary>
    public abstract string Message { get; }

    /// <summary>
    /// Additional structured data about the error, or <see langword="null"/> when none is provided.
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?>? Metadata => null;

    /// <summary>
    /// Creates a simple error without defining a new type.
    /// </summary>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <returns>A <see cref="ApiResultError"/> carrying the supplied code and message.</returns>
    public static ApiResultError Custom(string code, string message)
    {
        return new SimpleError(code, message);
    }

    /// <summary>
    /// Projects this error to an <see cref="ErrorDescriptor"/> carrying <see cref="Code"/>, <see cref="Message"/> and
    /// every <see cref="Metadata"/> entry as a descriptor parameter, so custom errors keep their structured data on the
    /// ProblemDetails wire.
    /// </summary>
    /// <returns>A new descriptor for this error.</returns>
    public ErrorDescriptor ToErrorDescriptor()
    {
        var descriptor = new ErrorDescriptor(Code, Message);

        if (Metadata is { Count: > 0 } metadata)
        {
            foreach (var (key, value) in metadata)
            {
                descriptor.WithParam(key, value);
            }
        }

        return descriptor;
    }

    private sealed record SimpleError(string Code, string Message) : ApiResultError
    {
        public override string Code { get; } = Code;
        public override string Message { get; } = Message;
    }
}

internal static class ApiResultErrorEquality
{
    public static bool SequenceEquals(IReadOnlyList<ErrorDescriptor> left, IReadOnlyList<ErrorDescriptor> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => _DescriptorEquals(pair.First, pair.Second));
    }

    public static int GetSequenceHashCode(IReadOnlyList<ErrorDescriptor> errors)
    {
        var hash = new HashCode();

        foreach (var error in errors)
        {
            hash.Add(error.Code, StringComparer.Ordinal);
            hash.Add(error.Description, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static bool DictionaryEquals(
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> left,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> right
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (field, descriptors) in left)
        {
            if (!right.TryGetValue(field, out var otherDescriptors) || !SequenceEquals(descriptors, otherDescriptors))
            {
                return false;
            }
        }

        return true;
    }

    public static int GetDictionaryHashCode(IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors)
    {
        var hash = new HashCode();

        foreach (var (field, descriptors) in errors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(field, StringComparer.Ordinal);
            hash.Add(GetSequenceHashCode(descriptors));
        }

        return hash.ToHashCode();
    }

    private static bool _DescriptorEquals(ErrorDescriptor left, ErrorDescriptor right)
    {
        return string.Equals(left.Code, right.Code, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal);
    }
}
