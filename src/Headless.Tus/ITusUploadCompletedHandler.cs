// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

/// <summary>
/// An application's handler for a finished upload: it takes the upload over, for example by recording it or by
/// queueing its processing. Name the type on an upload endpoint with <c>OnCompleted&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// The handler runs once the upload's last byte is stored, inside the request that stored it. An upload counts as
/// handed off only after the handler returns <see cref="TusUploadHandoff.Keep"/> or
/// <see cref="TusUploadHandoff.Delete"/>. On a store that records that (an <see cref="ITusHandoffStore"/>, such as
/// the S3 and Azure stores), an upload whose handler threw, or never ran because the request died first, gets the
/// handler again on the client's next status request, until it returns. So the handler runs at least once and may
/// run more than once for one upload: make it safe to repeat. On any other store it runs once.
/// </para>
/// <para>
/// The handler runs under the upload's file lock, so two requests never run it for one upload at the same time,
/// provided the host's file lock provider spans every node that serves the endpoint. Keep it short, since the
/// client waits on it: record or queue the work and return.
/// </para>
/// <para>
/// An exception that leaves <see cref="HandleAsync"/> is a transient failure, not a refusal: the request fails,
/// and the upload stays for the next attempt. Return <see cref="TusUploadHandoff.Reject"/> to refuse an upload for
/// good. One case has no next attempt: when the upload finished inside the request that created it, the client
/// never learned its URL, so the upload is deleted and the request fails with a server error.
/// </para>
/// <para>The instance is resolved from the request's services, so it may depend on scoped services.</para>
/// </remarks>
[PublicAPI]
public interface ITusUploadCompletedHandler
{
    /// <summary>Takes over a finished upload, or refuses it.</summary>
    /// <param name="upload">The finished upload.</param>
    /// <param name="cancellationToken">Cancelled when the client abandons the request.</param>
    /// <returns>
    /// <see cref="TusUploadHandoff.Keep"/> to leave the upload in the store, <see cref="TusUploadHandoff.Delete"/>
    /// to have it removed once the response is sent, or <see cref="TusUploadHandoff.Reject"/> to remove it now and
    /// refuse it to the client.
    /// </returns>
    ValueTask<TusUploadHandoff> HandleAsync(TusCompletedUpload upload, CancellationToken cancellationToken);
}
