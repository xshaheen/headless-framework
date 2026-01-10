// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Base class for all result errors. Extend this to create domain-specific errors.
/// </summary>
[PublicAPI]
public abstract record ResultError
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
    /// Additional structured data about the error.
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?>? Metadata => null;

    /// <summary>
    /// Creates a simple error without defining a new type.
    /// </summary>
    public static ResultError Custom(string code, string message) => new SimpleError(code, message);

    private sealed record SimpleError(string Code, string Message) : ResultError
    {
        public override string Code { get; } = Code;
        public override string Message { get; } = Message;
    }
}
