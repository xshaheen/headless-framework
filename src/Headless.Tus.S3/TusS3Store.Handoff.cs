// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store : ITusHandoffStore
{
    /// <summary>
    /// Reads where the upload stands between its last byte and its handoff to the application, from storage.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// <see cref="TusHandoffState.NotComplete"/> for an unknown id and for an upload that is incomplete or still
    /// completing, <see cref="TusHandoffState.Pending"/> for a completed upload that was not handed off, and
    /// <see cref="TusHandoffState.HandedOff"/> once <see cref="MarkHandedOffAsync"/> recorded it
    /// </returns>
    /// <remarks>
    /// The answer never comes from the state the store keeps for the current HTTP request, so a caller that asks
    /// under the upload's lock sees what the lock's last holder did. It costs up to three requests.
    /// </remarks>
    /// <exception cref="TusStoreException">The id is invalid.</exception>
    public async Task<TusHandoffState> GetHandoffStateAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);
        var keys = _Keys();

        return await _stateStore.ReadHandoffStateAsync(keys, fileId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records that the completed upload was handed off to the application, as an object of its own beside the
    /// upload's completed record.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <remarks>
    /// The mark is written only if it does not exist yet, so marking an upload again does nothing. Marking an
    /// upload that no longer exists does nothing either. Deleting the upload removes its mark.
    /// </remarks>
    /// <exception cref="TusStoreException">The id is invalid.</exception>
    /// <exception cref="InvalidOperationException">The upload is not completed yet.</exception>
    /// <exception cref="AmazonS3Exception">Storage refused the write.</exception>
    public async Task MarkHandedOffAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);
        var keys = _Keys();

        var upload = await _stateStore.LoadAsync(keys, fileId, cancellationToken).ConfigureAwait(false);

        if (upload is null)
        {
            // Whoever took the upload over deleted it, and a mark without an upload would only be left behind.
            return;
        }

        if (upload.Completed is null)
        {
            // A mark written early would make the upload read as handed off the moment it completes.
            throw new InvalidOperationException(
                $"File {fileId} is not complete yet, so it cannot be recorded as handed off."
            );
        }

        await _stateStore.MarkHandedOffAsync(keys, fileId, cancellationToken).ConfigureAwait(false);
        _logger.LogS3UploadHandedOff(fileId);
    }
}
