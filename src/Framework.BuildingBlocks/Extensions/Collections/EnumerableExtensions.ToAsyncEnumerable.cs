#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

public static partial class EnumerableExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AsyncEnumerableWrapper<T>(source);
    }

    public static IAsyncEnumerator<T> ToAsyncEnumerator<T>(this IEnumerator<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AsyncEnumeratorWrapper<T>(source);
    }

    private sealed class AsyncEnumerableWrapper<T>(IEnumerable<T> enumerable) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new AsyncEnumeratorWrapper<T>(enumerable.GetEnumerator());
        }
    }

    private sealed class AsyncEnumeratorWrapper<T>(IEnumerator<T> enumerator) : IAsyncEnumerator<T>
    {
        public T Current => enumerator.Current;

        public ValueTask DisposeAsync()
        {
            enumerator.Dispose();

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(enumerator.MoveNext());
    }
}
