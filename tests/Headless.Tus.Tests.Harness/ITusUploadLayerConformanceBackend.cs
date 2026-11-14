// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// What a tus store backend supplies to run the upload endpoint scenarios: the store registration an application
/// writes, over a storage root that only this backend instance writes to. A scenario creates one backend, and
/// <see cref="TusUploadLayerConformanceTestsBase" /> disposes it when the scenario ends.
/// </summary>
/// <remarks>
/// The store is selected through the provider's own <c>Use*</c> member and never constructed by hand, because only
/// a store the provider registers keeps each tenant's uploads under a folder of their own. The suites that call a
/// store directly keep using <see cref="ITusConformanceBackend{TStore}" />.
/// </remarks>
public interface ITusUploadLayerConformanceBackend : IAsyncDisposable
{
    /// <summary>
    /// The longest <c>Upload-Metadata</c> string the store keeps with an upload, in characters, counting what the
    /// endpoint adds to the client's own. A store that sets no limit names a length a client can still send in
    /// the headers of one request.
    /// </summary>
    int MaxMetadataLength { get; }

    /// <summary>Registers what the provider needs from an application, such as its storage client.</summary>
    /// <param name="services">The host's services.</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Selects the store through the provider's <c>Use*</c> member, over this backend's own storage root. The
    /// scenario provisions the storage through the provisioner the provider registers before the host starts.
    /// </summary>
    /// <param name="tus">The builder of the host's <c>AddHeadlessTus</c> call.</param>
    void UseStore(HeadlessTusSetupBuilder tus);

    /// <summary>
    /// Lists the name of every object the store keeps under this backend's storage root, whatever upload or
    /// tenant it belongs to.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    Task<IReadOnlyList<string>> ListStoredNamesAsync(CancellationToken cancellationToken);
}
