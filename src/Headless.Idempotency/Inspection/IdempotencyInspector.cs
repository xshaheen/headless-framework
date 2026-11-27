// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>Serves operator reads straight from the record store's lock-free read and the current options.</summary>
internal sealed class IdempotencyInspector(IIdempotencyRecordStore store, IdempotencyRequestResolver resolver)
    : IIdempotencyInspector
{
    public ValueTask<IdempotencyRecordSnapshot?> InspectAsync(string key, CancellationToken cancellationToken = default)
    {
        var recordKey = resolver.ResolvePeek(key);

        // The same lock-free read as PeekAsync: no owned unit, no record lock, no write.
        return store.InspectAsync(recordKey, cancellationToken);
    }

    public IdempotencyStoreDescription Describe()
    {
        var options = resolver.CurrentOptions;
        var info = store.Info;

        return new IdempotencyStoreDescription
        {
            Provider = info.Provider,
            Location = info.Location,
            DefaultRetention = options.DefaultRetention,
            DefaultLeaseDuration = options.DefaultLeaseDuration,
            MinimumLeaseDuration = options.MinimumLeaseDuration,
            MaximumLeaseDuration = options.MaximumLeaseDuration,
            PurgeInterval = info.PurgeDeletesRecords ? options.PurgeInterval : null,
            PurgeBatchSize = options.PurgeBatchSize,
        };
    }
}
