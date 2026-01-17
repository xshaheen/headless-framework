using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Tests.Helpers;

public static class ObservableCollectionExtensions
{
    public static async Task WaitOneMessage<T>(
        this ObservableCollection<T> collection,
        CancellationToken cancellationToken
    )
    {
        await collection.WaitForMessages(x => x.Count() == 1, cancellationToken);
    }

    public static async Task WaitForMessages<T>(
        this ObservableCollection<T> collection,
        Func<IEnumerable<T>, bool> comparison,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cts = new CancellationTokenSource();
        cancellationToken.Register(() => cts.Cancel());

        await Task.Run(
            async () =>
            {
                void onCollectionChanged(object? sender, NotifyCollectionChangedEventArgs? args)
                {
                    if (comparison(collection))
                    {
                        cts.Cancel();
                    }
                }

                collection.CollectionChanged += onCollectionChanged;

                if (collection.Count > 0)
                {
                    onCollectionChanged(collection, null);
                }

                try
                {
                    await Task.Delay(-1, cts.Token);
                }
                catch (TaskCanceledException) { }
                finally
                {
                    collection.CollectionChanged -= onCollectionChanged;
                }

                cancellationToken.ThrowIfCancellationRequested();
            },
            cancellationToken
        );
    }
}
