// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// A store that remembers what it read about an upload for the rest of the current HTTP request. A caller that has
/// just taken the upload's file lock asks it to forget that, so the next read comes from storage.
/// </summary>
/// <remarks>
/// What a request read before it held the lock says nothing about what the lock's last holder wrote. A decision
/// that is only right under the lock, such as whether an upload is finished, must not be answered from it.
/// </remarks>
internal interface ITusRequestSnapshotStore
{
    /// <summary>Drops what the current request remembers about the upload. Does nothing outside a request.</summary>
    void ForgetRequestSnapshot(string fileId);
}
