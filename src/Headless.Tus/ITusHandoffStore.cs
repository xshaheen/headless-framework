// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// An optional capability of an <see cref="ITusStore"/>: it records that a finished upload was handed off to the
/// application. An upload endpoint needs the record to run a completion handler again for an upload whose handler
/// failed or never ran, and to tell an upload a client may still delete from one the application has taken over.
/// </summary>
/// <remarks>
/// On a store without it, an endpoint's <see cref="ITusUploadCompletedHandler"/> runs once per upload, and a
/// failure is not repeated. The record belongs to the upload: deleting the upload removes it.
/// </remarks>
[PublicAPI]
public interface ITusHandoffStore
{
    /// <summary>Reads where the upload stands, from storage.</summary>
    /// <param name="fileId">The upload's file id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// <see cref="TusHandoffState.NotComplete"/> for an upload that lacks bytes or does not exist,
    /// <see cref="TusHandoffState.Pending"/> for a complete upload that was not handed off, and
    /// <see cref="TusHandoffState.HandedOff"/> once <see cref="MarkHandedOffAsync"/> recorded it.
    /// </returns>
    /// <remarks>
    /// The answer must come from storage on every call, never from state the store keeps for the current request:
    /// the endpoint asks again after it takes the upload's file lock, to see what the lock's last holder did.
    /// </remarks>
    Task<TusHandoffState> GetHandoffStateAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>Records that the complete upload was handed off.</summary>
    /// <param name="fileId">The upload's file id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the record is durable.</returns>
    /// <remarks>
    /// Recording an upload that is already recorded does nothing, and so does recording one that no longer exists:
    /// a completion handler may have deleted it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The upload does not hold all of its bytes yet.</exception>
    Task MarkHandedOffAsync(string fileId, CancellationToken cancellationToken);
}
