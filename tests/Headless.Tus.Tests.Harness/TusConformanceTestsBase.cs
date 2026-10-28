// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using tusdotnet.Interfaces;

namespace Tests;

/// <summary>
/// Shared lifetime for the tus conformance suites: creates the store under test through the backend before each
/// scenario. The scenario bases carry no test attributes; leaf <c>*.Tests.Integration</c> classes attach
/// <c>[Fact]</c> to <c>public override</c> delegations of their methods.
/// </summary>
public abstract class TusConformanceTestsBase<TStore>(ITusConformanceBackend<TStore> backend) : TestBase
    where TStore : class,
        ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusConcatenationStore
{
    /// <summary>The store under test, created by the backend before each scenario.</summary>
    protected TStore Store { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Store = await backend.CreateStoreAsync(LoggerFactory, AbortToken);
    }

    /// <summary>
    /// Reads a finished upload's content through <see cref="ITusReadableStore" />, the one read path every store
    /// shares, so content assertions hold for any backend.
    /// </summary>
    protected async Task<byte[]> ReadContentAsync(string fileId)
    {
        var file = await Store.GetFileAsync(fileId, AbortToken);
        file.Should().NotBeNull();

        await using var content = await file!.GetContentAsync(AbortToken);
        await using var downloadStream = new MemoryStream();
        await content.CopyToAsync(downloadStream, AbortToken);

        return downloadStream.ToArray();
    }
}
