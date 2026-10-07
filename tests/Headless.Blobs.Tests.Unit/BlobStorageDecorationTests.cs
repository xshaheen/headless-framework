// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for <c>DecorateHeadlessBlobs</c>: which stores it wraps, and when.</summary>
public sealed class BlobStorageDecorationTests
{
    private const string _Named = "docs";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_wrap_default_and_named_stores_whichever_registration_runs_first(bool decorationFirst)
    {
        // given
        var defaultStore = Substitute.For<IBlobStorage>();
        var namedStore = Substitute.For<IBlobStorage>();
        var seen = new List<(IBlobStorage Inner, string? Name)>();
        var decoration = new BlobStorageDecoration(
            (inner, name, _) =>
            {
                seen.Add((inner, name));
                return new Wrapper(inner);
            }
        );
        var services = new ServiceCollection();

        void addBlobs() =>
            services.AddHeadlessBlobs(blobs =>
            {
                blobs.RegisterDefaultProvider(s => s.AddSingleton(defaultStore));
                blobs.AddNamed(_Named, i => i.RegisterProvider(s => s.AddKeyedSingleton(_Named, namedStore)));
            });

        if (decorationFirst)
        {
            services.DecorateHeadlessBlobs(decoration);
            addBlobs();
        }
        else
        {
            addBlobs();
            services.DecorateHeadlessBlobs(decoration);
        }

        await using var provider = services.BuildServiceProvider();

        // when
        var resolvedDefault = provider.GetRequiredService<IBlobStorage>();
        var resolvedNamed = provider.GetRequiredService<IBlobStorageProvider>().GetStorage(_Named);

        // then
        decoration.DecoratedRegistrations.Should().Be(2);
        resolvedDefault.Should().BeOfType<Wrapper>().Which.Inner.Should().BeSameAs(defaultStore);
        resolvedNamed.Should().BeOfType<Wrapper>().Which.Inner.Should().BeSameAs(namedStore);
        seen.Should().BeEquivalentTo([(defaultStore, (string?)null), (namedStore, _Named)]);
    }

    [Fact]
    public async Task should_wrap_outside_the_cross_cutting_extensions_when_registered_first()
    {
        // given
        var store = Substitute.For<IBlobStorage>();
        var services = new ServiceCollection();
        services.DecorateHeadlessBlobs(new BlobStorageDecoration((inner, _, _) => new Wrapper(inner)));

        // when
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.RegisterDefaultProvider(s => s.AddSingleton(store));
            blobs.RegisterCrossCuttingExtension(s => s.Decorate<IBlobStorage>((inner, _) => new CrossCutting(inner)));
        });

        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IBlobStorage>()
            .Should()
            .BeOfType<Wrapper>()
            .Which.Inner.Should()
            .BeOfType<CrossCutting>();
    }

    [Fact]
    public async Task should_leave_a_store_unwrapped_when_the_decoration_returns_it()
    {
        // given
        var store = Substitute.For<IBlobStorage>();
        var services = new ServiceCollection();
        services.AddHeadlessBlobs(blobs => blobs.RegisterDefaultProvider(s => s.AddSingleton(store)));
        services.DecorateHeadlessBlobs(new BlobStorageDecoration((inner, _, _) => inner));

        await using var provider = services.BuildServiceProvider();

        // when
        var resolved = provider.GetRequiredService<IBlobStorage>();

        // then
        resolved.Should().BeSameAs(store);
    }

    [Fact]
    public void should_report_no_decorated_registration_when_blobs_are_never_added()
    {
        // given
        var decoration = new BlobStorageDecoration((inner, _, _) => new Wrapper(inner));
        var services = new ServiceCollection();

        // when
        services.DecorateHeadlessBlobs(decoration);

        // then
        decoration.DecoratedRegistrations.Should().Be(0);
    }

    [Fact]
    public async Task should_apply_the_same_decoration_once_when_registered_twice()
    {
        // given
        var store = Substitute.For<IBlobStorage>();
        var decoration = new BlobStorageDecoration((inner, _, _) => new Wrapper(inner));
        var services = new ServiceCollection();
        services.DecorateHeadlessBlobs(decoration);
        services.DecorateHeadlessBlobs(decoration);

        // when
        services.AddHeadlessBlobs(blobs => blobs.RegisterDefaultProvider(s => s.AddSingleton(store)));

        await using var provider = services.BuildServiceProvider();

        // then
        decoration.DecoratedRegistrations.Should().Be(1);
        provider.GetRequiredService<IBlobStorage>().Should().BeOfType<Wrapper>().Which.Inner.Should().BeSameAs(store);
    }

    private sealed class Wrapper(IBlobStorage inner) : ForwardingBlobStorage(inner);

    private sealed class CrossCutting(IBlobStorage inner) : ForwardingBlobStorage(inner);

    /// <summary>A store that only records what it wraps; no test calls through it.</summary>
    private abstract class ForwardingBlobStorage(IBlobStorage inner) : IBlobStorage
    {
        public IBlobStorage Inner { get; } = inner;

        public bool RequiresContainerProvisioning => Inner.RequiresContainerProvisioning;

        public ValueTask UploadAsync(
            BlobLocation location,
            Stream content,
            string? contentType = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default
        ) => Inner.UploadAsync(location, content, contentType, metadata, cancellationToken);

        public ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
            string container,
            IReadOnlyCollection<BlobUploadRequest> blobs,
            CancellationToken cancellationToken = default
        ) => Inner.BulkUploadAsync(container, blobs, cancellationToken);

        public ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default) =>
            Inner.DeleteAsync(location, cancellationToken);

        public ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
            string container,
            IReadOnlyCollection<string> paths,
            CancellationToken cancellationToken = default
        ) => Inner.BulkDeleteAsync(container, paths, cancellationToken);

        public ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default) =>
            Inner.DeleteAllAsync(query, cancellationToken);

        public ValueTask<bool> MoveAsync(
            BlobLocation source,
            BlobLocation destination,
            CancellationToken cancellationToken = default
        ) => Inner.MoveAsync(source, destination, cancellationToken);

        public ValueTask<bool> CopyAsync(
            BlobLocation source,
            BlobLocation destination,
            CancellationToken cancellationToken = default
        ) => Inner.CopyAsync(source, destination, cancellationToken);

        public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default) =>
            Inner.ExistsAsync(location, cancellationToken);

        public ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
            BlobLocation location,
            CancellationToken cancellationToken = default
        ) => Inner.OpenReadStreamAsync(location, cancellationToken);

        public ValueTask<BlobInfo?> GetBlobInfoAsync(
            BlobLocation location,
            CancellationToken cancellationToken = default
        ) => Inner.GetBlobInfoAsync(location, cancellationToken);

        public ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default) =>
            Inner.ListAsync(query, cancellationToken);

        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
