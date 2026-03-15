using System.Diagnostics.CodeAnalysis;

namespace Headless.Jobs.Models;

public class JobResult<TEntity>
    where TEntity : class
{
    internal JobResult(Exception exception)
        : this(false) => Exception = exception;

    internal JobResult(TEntity result)
        : this(true) => Result = result;

    internal JobResult(int affectedRows)
        : this(true) => AffectedRows = affectedRows;

    private JobResult(bool isSucceeded) => IsSucceeded = isSucceeded;

    internal JobResult(TEntity result, int affectedRows)
        : this(true)
    {
        Result = result;
        AffectedRows = affectedRows;
    }

    [MemberNotNullWhen(true, nameof(Result))]
    [MemberNotNullWhen(false, nameof(Exception))]
    public bool IsSucceeded { get; }
    public int AffectedRows { get; }
    public TEntity? Result { get; }
    public Exception? Exception { get; }
}
