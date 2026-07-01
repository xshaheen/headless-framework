// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>
/// Discriminated result returned by <c>ITimeJobManager</c> and <c>ICronJobManager</c> update and delete
/// operations. Carries either the persisted entity and affected-row count on success, or the exception
/// on failure.
/// </summary>
/// <remarks>
/// Add operations (single and batch) throw on failure instead of returning a result so that failures
/// propagate through the caller's ambient transaction on the coordinated path.
/// </remarks>
/// <typeparam name="TEntity">The job entity type.</typeparam>
public sealed class JobResult<TEntity>
    where TEntity : class
{
    internal JobResult(Exception exception)
        : this(isSucceeded: false) => Exception = exception;

    internal JobResult(TEntity result)
        : this(isSucceeded: true) => Result = result;

    internal JobResult(int affectedRows)
        : this(isSucceeded: true) => AffectedRows = affectedRows;

    private JobResult(bool isSucceeded) => IsSucceeded = isSucceeded;

    internal JobResult(TEntity result, int affectedRows)
        : this(isSucceeded: true)
    {
        Result = result;
        AffectedRows = affectedRows;
    }

    /// <summary>
    /// <see langword="true"/> when the operation succeeded. When <see langword="true"/>,
    /// <see cref="Result"/> is non-null; when <see langword="false"/>, <see cref="Exception"/>
    /// is non-null.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Result))]
    [MemberNotNullWhen(false, nameof(Exception))]
    public bool IsSucceeded { get; }

    /// <summary>Number of database rows affected by the operation, when applicable.</summary>
    public int AffectedRows { get; }

    /// <summary>The persisted or returned entity on success; <see langword="null"/> on failure.</summary>
    public TEntity? Result { get; }

    /// <summary>The exception that caused the operation to fail; <see langword="null"/> on success.</summary>
    public Exception? Exception { get; }
}
