// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Initialization.Schema;

/// <summary>Thrown when the schema runner cannot open a connection, take its lock, apply or record a step, or when startup finds a fatal history mismatch.</summary>
[PublicAPI]
public sealed class SchemaRunnerException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    /// <summary>The mismatches that failed startup, when that is the cause; otherwise empty.</summary>
    public IReadOnlyList<SchemaMismatch> Mismatches { get; init; } = [];
}
