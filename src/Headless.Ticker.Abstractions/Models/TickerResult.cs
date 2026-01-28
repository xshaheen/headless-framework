using System.Diagnostics.CodeAnalysis;

namespace Headless.Ticker.Models;

public class TickerResult<TEntity>
    where TEntity : class
{
    internal TickerResult(Exception exception)
        : this(false) => Exception = exception;

    internal TickerResult(TEntity result)
        : this(true) => Result = result;

    internal TickerResult(int affectedRows)
        : this(true) => AffectedRows = affectedRows;

    private TickerResult(bool isSucceeded) => IsSucceeded = isSucceeded;

    internal TickerResult(TEntity result, int affectedRows)
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
