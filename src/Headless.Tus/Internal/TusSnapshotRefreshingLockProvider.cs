// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Interfaces;

namespace Headless.Tus.Internal;

/// <summary>
/// The file lock an upload endpoint gives tusdotnet. It takes the host's lock and, once it holds it, tells the
/// store to forget what the request read about the upload before.
/// </summary>
/// <remarks>
/// tusdotnet authorizes a request before it takes the lock, and the endpoint reads the upload to authorize it. A
/// store that remembers that read for the rest of the request would then answer tusdotnet's checks under the lock
/// from it, although the lock's last holder may have written since. A write that waited for the lock could pass
/// the offset check against an offset that is gone, and a refusal could remove bytes another request just stored.
/// </remarks>
internal sealed class TusSnapshotRefreshingLockProvider(ITusFileLockProvider inner, ITusRequestSnapshotStore store)
    : ITusFileLockProvider
{
    public async Task<ITusFileLock> AquireLock(string fileId)
    {
        return new Lock(await inner.AquireLock(fileId).ConfigureAwait(false), store, fileId);
    }

    private sealed class Lock(ITusFileLock inner, ITusRequestSnapshotStore store, string fileId) : ITusFileLock
    {
        async Task<bool> ITusFileLock.Lock()
        {
            if (!await inner.Lock().ConfigureAwait(false))
            {
                return false;
            }

            store.ForgetRequestSnapshot(fileId);

            return true;
        }

        public Task ReleaseIfHeld()
        {
            return inner.ReleaseIfHeld();
        }
    }
}
