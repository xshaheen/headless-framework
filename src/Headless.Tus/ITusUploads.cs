// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Headless.Tus;

/// <summary>
/// Gives an application the finished uploads of its upload endpoints. A client finishes an upload, then names the
/// upload's file id in a request of the application's own, and the application asks here for that upload.
/// </summary>
/// <remarks>
/// <para>
/// An upload is finished once every byte of its declared length is stored. Neither method returns an upload that
/// still lacks bytes, one whose length was never declared, or one another request holds at that moment: a write,
/// a status request, or another lookup of the same upload. So the result may be <see langword="null"/> for an
/// upload that is found a moment later.
/// </para>
/// <para>
/// Finding an upload reserves nothing: every call that names a finished upload gets it, so an application that
/// records or copies an upload makes that step safe to repeat. The application decides what happens to the
/// upload afterwards. It stays in the store, and never expires, until the application deletes it.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITusUploads
{
    /// <summary>
    /// Finds a finished upload of the default upload endpoint that belongs to the caller of the current HTTP
    /// request.
    /// </summary>
    /// <param name="fileId">
    /// The upload's file id, the last segment of the upload's URL, as the client sent it.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The upload, or <see langword="null"/> when the caller may not use it: the id is unknown, the upload was
    /// created on another endpoint, for another owner, or in another tenant, or it is not finished.
    /// </returns>
    /// <remarks>
    /// The caller is resolved exactly as the upload endpoint resolves it: the owner from the endpoint's
    /// <c>OwnedBy</c> function, or from <c>ICurrentUser</c> when it has none, and the tenant from
    /// <c>ICurrentTenant</c>. A request with no owner is given no upload.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileId"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no default upload endpoint is declared, or when the call is made outside an HTTP request, where
    /// there is no caller to compare with: use <see cref="FindUncheckedAsync"/> there.
    /// </exception>
    Task<TusUpload?> FindAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a finished upload of the upload endpoint declared as <paramref name="endpointName"/> that belongs to
    /// the caller of the current HTTP request.
    /// </summary>
    /// <param name="fileId">
    /// The upload's file id, the last segment of the upload's URL, as the client sent it.
    /// </param>
    /// <param name="endpointName">The name the upload endpoint was declared with.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns><inheritdoc cref="FindAsync(string, CancellationToken)" path="/returns"/></returns>
    /// <remarks><inheritdoc cref="FindAsync(string, CancellationToken)" path="/remarks"/></remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fileId"/> or <paramref name="endpointName"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="endpointName"/> is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no upload endpoint named <paramref name="endpointName"/> is declared, or when the call is made
    /// outside an HTTP request, where there is no caller to compare with: use <see cref="FindUncheckedAsync"/>
    /// there.
    /// </exception>
    Task<TusUpload?> FindAsync(string fileId, string endpointName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a finished upload by its file id alone, comparing nothing with a caller. It is for trusted server
    /// code that runs outside the request that named the upload, such as a background job.
    /// </summary>
    /// <param name="fileId">The upload's file id.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The upload, with the owner, tenant, and endpoint it was created for, or <see langword="null"/> when the id
    /// is unknown, the upload was not created through an upload endpoint, or it is not finished.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Whoever holds a file id reads that upload through this method, whoever created it. Never pass an id a
    /// client sent unless the application has already checked it, for example with
    /// <see cref="FindAsync(string, CancellationToken)"/> in the request that received it, or compares
    /// <see cref="TusUpload.OwnerKey"/> itself.
    /// </para>
    /// <para>
    /// With uploads scoped by tenant the lookup stays inside the ambient tenant: wrap the call in
    /// <c>ICurrentTenant.Change(tenantId)</c> with the tenant the upload belongs to, and use the returned upload
    /// inside that same scope.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileId"/> is <see langword="null"/>.</exception>
    /// <exception cref="MissingTenantContextException">
    /// Thrown when uploads are scoped by tenant and no tenant is ambient.
    /// </exception>
    Task<TusUpload?> FindUncheckedAsync(string fileId, CancellationToken cancellationToken = default);
}
