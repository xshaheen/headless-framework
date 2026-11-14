// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class TusUploadEndpointOptionsTests : TestBase
{
    private static TusDiskStore _DiskStore()
    {
        // Constructing the disk store touches no file: it only keeps the path.
        return new TusDiskStore(Path.GetTempPath());
    }

    private static IConfiguration _Configuration(long maxSize, TimeSpan expiration)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["MaxSize"] = maxSize.ToString(CultureInfo.InvariantCulture),
                    ["Expiration"] = expiration.ToString("c", CultureInfo.InvariantCulture),
                }
            )
            .Build();
    }

    public static TheoryData<string, bool> Declarations =>
        new()
        {
            { "configuration", false },
            { "action", false },
            { "action-with-provider", false },
            { "configuration", true },
            { "action", true },
            { "action-with-provider", true },
        };

    [Theory]
    [MemberData(nameof(Declarations))]
    public async Task should_apply_the_declared_options_to_the_mapped_endpoint(string overload, bool named)
    {
        // given - a 100-byte limit and a 10-minute window, declared through each overload
        var expiration = TimeSpan.FromMinutes(10);
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
            {
                tus.UseStore(store);

                _ = (overload, named) switch
                {
                    ("configuration", false) => tus.AddUploadEndpoint(_Configuration(100, expiration)),
                    ("configuration", true) => tus.AddUploadEndpoint("videos", _Configuration(100, expiration)),
                    ("action", false) => tus.AddUploadEndpoint(options =>
                    {
                        options.MaxSize = 100;
                        options.Expiration = expiration;
                    }),
                    ("action", true) => tus.AddUploadEndpoint(
                        "videos",
                        options =>
                        {
                            options.MaxSize = 100;
                            options.Expiration = expiration;
                        }
                    ),
                    (_, false) => tus.AddUploadEndpoint(
                        (options, provider) =>
                        {
                            options.MaxSize = provider.GetRequiredService<UploadLimit>().Bytes;
                            options.Expiration = expiration;
                        }
                    ),
                    (_, true) => tus.AddUploadEndpoint(
                        "videos",
                        (options, provider) =>
                        {
                            options.MaxSize = provider.GetRequiredService<UploadLimit>().Bytes;
                            options.Expiration = expiration;
                        }
                    ),
                };
            },
            ConfigureServices = static services => services.AddSingleton(new UploadLimit(100)),
            Map = endpoints =>
            {
                _ = named
                    ? endpoints.MapHeadlessTus(TusEndpointTestHost.Route, "videos")
                    : endpoints.MapHeadlessTus(TusEndpointTestHost.Route);
            },
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var atLimit = await host.CreateAsync(uploadLength: 100);
        using var aboveLimit = await host.CreateAsync(uploadLength: 101);

        // then
        aboveLimit.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        atLimit.StatusCode.Should().Be(HttpStatusCode.Created);
        var expires = atLimit.Headers.GetValues("Upload-Expires").Should().ContainSingle().Which;
        DateTimeOffset
            .ParseExact(expires, "R", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            .Should()
            .BeCloseTo(DateTimeOffset.UtcNow.Add(expiration), TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task should_fail_startup_when_the_maximum_size_is_not_positive(long? maxSize)
    {
        // given - null leaves the option as the options class creates it
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(options =>
                    {
                        if (maxSize is not null)
                        {
                            options.MaxSize = maxSize.Value;
                        }
                    }),
        };

        // when
        var act = () => TusEndpointTestHost.StartAsync(setup, AbortToken);

        // then
        var failure = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        failure.OptionsType.Should().Be<TusUploadEndpointOptions>();
        failure.Failures.Should().ContainSingle().Which.Should().Contain(nameof(TusUploadEndpointOptions.MaxSize));
    }

    [Fact]
    public async Task should_fail_startup_when_a_named_endpoint_is_invalid_beside_a_valid_one()
    {
        // given
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
            {
                tus.UseStore(store);
                tus.AddUploadEndpoint(static options => options.MaxSize = 100);
                tus.AddUploadEndpoint("videos", static _ => { });
            },
        };

        // when
        var act = () => TusEndpointTestHost.StartAsync(setup, AbortToken);

        // then
        var failure = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        failure.OptionsName.Should().Be("videos");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task should_fail_startup_when_the_expiration_is_not_positive(int seconds)
    {
        // given
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(options =>
                    {
                        options.MaxSize = 100;
                        options.Expiration = TimeSpan.FromSeconds(seconds);
                    }),
        };

        // when
        var act = () => TusEndpointTestHost.StartAsync(setup, AbortToken);

        // then
        var failure = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        failure.Failures.Should().ContainSingle().Which.Should().Contain(nameof(TusUploadEndpointOptions.Expiration));
    }

    [Fact]
    public void should_throw_when_the_default_endpoint_is_declared_twice()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessTus(tus =>
            {
                tus.UseStore(_DiskStore());
                tus.AddUploadEndpoint(options => options.MaxSize = 100);
                tus.AddUploadEndpoint(options => options.MaxSize = 200);
            });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("The default upload endpoint is already declared. Declare each upload endpoint once.");
        services.Should().BeEmpty();
    }

    [Fact]
    public void should_throw_when_a_named_endpoint_is_declared_twice()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessTus(tus =>
            {
                tus.UseStore(_DiskStore());
                tus.AddUploadEndpoint("videos", options => options.MaxSize = 100);
                tus.AddUploadEndpoint("videos", options => options.MaxSize = 200);
            });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("The upload endpoint \"videos\" is already declared. Declare each upload endpoint once.");
        services.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_a_blank_endpoint_name_when_declaring(string name)
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessTus(tus =>
                tus.UseStore(_DiskStore()).AddUploadEndpoint(name, options => options.MaxSize = 100)
            );

        // then - the default endpoint has its own overload, so a blank name is a mistake, not a way to declare it
        act.Should().Throw<ArgumentException>();
    }

    private sealed record UploadLimit(long Bytes);
}
