// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for the <c>UseBclCache</c> registration guards.</summary>
public sealed class SetupBclCacheTests
{
    [Fact]
    public void should_reject_a_serializer_configured_on_the_bcl_cache_instance()
    {
        // given
        var services = new ServiceCollection();

        // when — the caller wrongly configures a serializer on the adapter's named cache; it would silently
        // compete with the raw-bytes codec the adapter owns (last keyed registration would win)
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.UseBclCache(
                    options =>
                    {
                        options.CacheName = "bcl";
                        options.DefaultAbsoluteExpiration = TimeSpan.FromHours(1);
                    },
                    instance =>
                    {
                        instance.RegisterProvider(static _ => { });
                        instance.SetSerializerFactory(static _ => new RawBytesSerializer());
                    }
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*manages its own raw-bytes serializer*");
    }
}
