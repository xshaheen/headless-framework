// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

/// <summary>
/// An application's own check on an upload before it starts, for example a quota. Name the type on an upload
/// endpoint with <c>ValidateWith&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// The validator runs once per creating request, after the endpoint's own rules (required metadata, allowed
/// content types and extensions, empty uploads) have passed and before the store creates anything. An endpoint's
/// validators run in the order they were declared, and the first failure refuses the upload: the validators after
/// it do not run.
/// </para>
/// <para>
/// The instance is resolved from the request's services, so it may depend on scoped services. An exception that
/// leaves <see cref="ValidateAsync"/> fails the request with a server error; return a failure to refuse an upload.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITusUploadValidator
{
    /// <summary>Accepts the upload a request is about to create, or refuses it.</summary>
    /// <param name="request">The upload the client asks to create.</param>
    /// <param name="cancellationToken">Cancelled when the client abandons the request.</param>
    /// <returns>
    /// <see cref="TusUploadValidationResult.Success"/> to let the upload start, or a failure whose status and
    /// message answer the request.
    /// </returns>
    ValueTask<TusUploadValidationResult> ValidateAsync(TusUploadRequest request, CancellationToken cancellationToken);
}
