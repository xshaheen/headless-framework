// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TypedCurrentTenantInfoTests : TestBase
{
    [Fact]
    public async Task should_downcast_without_calling_projection_when_base_accessor_already_returns_the_subtype()
    {
        // given — AE13 downcast path: the base accessor's call happened to hit the store directly
        // (a cache miss) and the store returned the subclass instance already.
        var subclassInstance = new AcmeTenantInfo("ten_1", "acme", "Acme", isEnabled: true, region: "eu-west-1");
        var baseAccessor = Substitute.For<ICurrentTenantInfo>();
        baseAccessor.GetAsync(AbortToken).Returns(Task.FromResult<TenantInfo?>(subclassInstance));
        var projectionCalled = false;

        var sut = new TypedCurrentTenantInfo<AcmeTenantInfo>(
            baseAccessor,
            (_, _) =>
            {
                projectionCalled = true;

                throw new InvalidOperationException("projection must not run on the downcast fast path");
            }
        );

        // when
        var result = await sut.GetAsync(AbortToken);

        // then
        result.Should().BeSameAs(subclassInstance);
        result!.Region.Should().Be("eu-west-1");
        projectionCalled.Should().BeFalse();
    }

    [Fact]
    public async Task should_call_projection_and_rehydrate_subclass_fields_when_cache_served_the_base_shape()
    {
        // given — AE13 projection path: the base accessor returned a plain base-shape TenantInfo (as a
        // cache hit always does per R13), so the typed accessor's registered projection delegate is
        // responsible for re-hydrating the subclass instance — in this test, from a fake store.
        var baseShape = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        var baseAccessor = Substitute.For<ICurrentTenantInfo>();
        baseAccessor.GetAsync(AbortToken).Returns(Task.FromResult<TenantInfo?>(baseShape));

        var fakeStore = new Dictionary<string, AcmeTenantInfo>(StringComparer.Ordinal)
        {
            ["ten_1"] = new("ten_1", "acme", "Acme", isEnabled: true, region: "eu-west-1"),
        };

        var sut = new TypedCurrentTenantInfo<AcmeTenantInfo>(
            baseAccessor,
            (info, _) => Task.FromResult(fakeStore[info.Id])
        );

        // when
        var result = await sut.GetAsync(AbortToken);

        // then
        result.Should().NotBeNull();
        result.Should().NotBeSameAs(baseShape);
        result!.Region.Should().Be("eu-west-1");
    }

    [Fact]
    public async Task should_return_null_when_base_accessor_returns_null()
    {
        // given
        var baseAccessor = Substitute.For<ICurrentTenantInfo>();
        baseAccessor.GetAsync(AbortToken).Returns(Task.FromResult<TenantInfo?>(null));
        var sut = new TypedCurrentTenantInfo<AcmeTenantInfo>(
            baseAccessor,
            (_, _) => throw new InvalidOperationException("projection must not run when there is no ambient tenant")
        );

        // when
        var result = await sut.GetAsync(AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_register_and_resolve_the_typed_accessor_via_di()
    {
        // given
        var baseInfo = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantInfo>(_ => new _StubCurrentTenantInfo(baseInfo));
        services.AddTypedCurrentTenantInfo<AcmeTenantInfo>(
            (info, _) =>
                Task.FromResult(new AcmeTenantInfo(info.Id, info.Identifier, info.Name, info.IsEnabled, region: "eu"))
        );

        await using var provider = services.BuildServiceProvider();

        // when
        var typedAccessor = provider.GetRequiredService<ICurrentTenantInfo<AcmeTenantInfo>>();
        var result = await typedAccessor.GetAsync(AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Region.Should().Be("eu");
    }

    private sealed class AcmeTenantInfo(string id, string identifier, string? name, bool isEnabled, string region)
        : TenantInfo(id, identifier, name, isEnabled)
    {
        public string Region { get; } = region;
    }

    private sealed class _StubCurrentTenantInfo(TenantInfo info) : ICurrentTenantInfo
    {
        public Task<TenantInfo?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantInfo?>(info);
    }
}
