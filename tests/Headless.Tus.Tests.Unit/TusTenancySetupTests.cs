// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.Endpoint;
using Tests.S3;
using tusdotnet.Stores;

namespace Tests;

/// <summary>
/// The TUS tenancy seam: what it records, what it makes an upload endpoint refuse, and where it makes the S3 store
/// keep a tenant's uploads.
/// </summary>
public sealed class TusTenancySetupTests : TestBase
{
    private const string _Prefix = "uploads/";

    private static TusEndpointTestHostSetup _Scoped(bool tenancyBeforeTus = false)
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTenancy = static tenancy => tenancy.Tus(static tus => tus.ScopeByTenant()),
            TenancyBeforeTus = tenancyBeforeTus,
        };
    }

    /// <summary>A scoped host whose endpoint stores uploads in the S3 store, over an in-memory bucket.</summary>
    private static TusEndpointTestHostSetup _ScopedOverS3(InMemoryS3 s3)
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTenancy = static tenancy => tenancy.Tus(static tus => tus.ScopeByTenant()),
            ConfigureServices = services => services.AddSingleton(s3.Client),
            ConfigureTus = static (tus, _) =>
                tus.UseS3(static options =>
                    {
                        options.Bucket = "bucket";
                        options.Prefix = _Prefix;
                    })
                    .AddUploadEndpoint(static options => options.MaxSize = TusEndpointTestHost.DefaultMaxSize),
        };
    }

    [Fact]
    public void should_record_the_tus_seam_as_guarded()
    {
        // given
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Tus(tus => tus.ScopeByTenant()));

        // then
        var manifest = (TenantPostureManifest)
            builder.Services.Single(d => d.ServiceType == typeof(TenantPostureManifest)).ImplementationInstance!;
        var seam = manifest.GetSeam(HeadlessTusTenancyBuilder.Seam);
        seam.Should().NotBeNull();
        seam!.Seam.Should().Be("Tus");
        seam.Status.Should().Be(TenantPostureStatus.Guarded);
        seam.Capabilities.Should().Equal("scope-by-tenant");
    }

    [Fact]
    public async Task should_fail_startup_when_uploads_are_scoped_and_no_store_is_registered()
    {
        // given - the seam without AddHeadlessTus
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddHeadlessTenancy(tenancy => tenancy.Tus(tus => tus.ScopeByTenant()));
        using var host = builder.Build();

        // when
        var start = () => host.StartAsync(AbortToken);

        // then
        (await start.Should().ThrowAsync<HeadlessTenancyValidationException>())
            .Which.Diagnostics.Should()
            .ContainSingle()
            .Which.Should()
            .Match<HeadlessTenancyDiagnostic>(d =>
                d.Code == "HEADLESS_TENANCY_TUS_NO_STORE"
                && d.Seam == "Tus"
                && d.Severity == HeadlessTenancyDiagnosticSeverity.Error
            );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_require_a_tenant_whichever_registration_runs_first(bool tenancyBeforeTus)
    {
        // given - the host starts, so the seam found the store AddHeadlessTus registered
        await using var host = await TusEndpointTestHost.StartAsync(_Scoped(tenancyBeforeTus), AbortToken);

        // when - a creating request outside any tenant
        using var refused = await host.CreateAsync(uploadLength: 10);

        // then
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync(AbortToken)).Should().Be("The request has no tenant.");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();

        // and the same request inside a tenant creates the upload
        host.Tenant = "acme";
        using var created = await host.CreateAsync(uploadLength: 10);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    public async Task should_refuse_a_create_whose_tenant_id_cannot_be_a_path_segment(string tenantId)
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Scoped(), AbortToken);
        host.Tenant = tenantId;

        // when
        using var response = await host.CreateAsync(uploadLength: 10);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be("The request's tenant id cannot be used to store uploads.");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_accept_any_tenant_id_while_uploads_are_not_scoped()
    {
        // given - tenancy without the seam: the tenant is only recorded on the upload, never used as a path
        await using var host = await TusEndpointTestHost.StartAsync(
            new TusEndpointTestHostSetup { RegisterCurrentTenant = true },
            AbortToken
        );
        host.Tenant = "Acme";

        // when
        using var response = await host.CreateAsync(uploadLength: 10);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("HEAD", null)]
    [InlineData("PATCH", null)]
    [InlineData("DELETE", null)]
    [InlineData("HEAD", "Acme")]
    [InlineData("HEAD", "globex")]
    public async Task should_answer_404_for_an_existing_upload_to_a_caller_outside_its_tenant(
        string method,
        string? callerTenant
    )
    {
        // given - an upload of tenant acme in a store that refuses to look anywhere outside a tenant
        var s3 = new InMemoryS3();
        await using var host = await TusEndpointTestHost.StartAsync(_ScopedOverS3(s3), AbortToken);
        host.Tenant = "acme";
        var location = await host.CreateUploadAsync(uploadLength: 10);
        var unknown = $"{TusEndpointTestHost.Route}/{Guid.NewGuid():n}";

        // when - the same user with no tenant, with a tenant no upload can be stored under, or in another tenant
        host.Tenant = callerTenant;
        using var response = await _SendAsync(host, method, location);
        using var reference = await _SendAsync(host, method, unknown);

        // then - the answer an id that was never created gets
        reference.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TusEndpointTestHost.DescribeAsync(response, AbortToken))
            .Should()
            .Be(await TusEndpointTestHost.DescribeAsync(reference, AbortToken));

        // and the upload is still its tenant's
        host.Tenant = "acme";
        using var own = await host.HeadAsync(location);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_keep_an_upload_created_through_the_endpoint_under_its_tenants_folder()
    {
        // given
        var s3 = new InMemoryS3();
        await using var host = await TusEndpointTestHost.StartAsync(_ScopedOverS3(s3), AbortToken);
        host.Tenant = "acme";

        // when
        var fileId = TusEndpointTestHost.FileIdOf(await host.CreateUploadAsync(uploadLength: 10));

        // then
        s3.Exists($"{_Prefix}.tus/.tenants/acme/{fileId}/info").Should().BeTrue();
        s3.Exists($"{_Prefix}.tus/{fileId}/info").Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_make_the_s3_store_keep_each_tenants_uploads_apart(bool tenancyBeforeTus)
    {
        // given
        var s3 = new InMemoryS3();
        await using var provider = _BuildS3Host(s3, scoped: true, tenancyBeforeTus);
        var store = provider.GetRequiredService<TusS3Store>();
        var tenant = provider.GetRequiredService<ICurrentTenant>();
        string acmeId;

        // when
        using (tenant.Change("acme"))
        {
            acmeId = await store.CreateFileAsync(10, metadata: null, AbortToken);
        }

        // then - stored under acme, found by acme, and missing for every other tenant
        s3.Exists($"{_Prefix}.tus/.tenants/acme/{acmeId}/info").Should().BeTrue();

        using (tenant.Change("acme"))
        {
            (await store.FileExistAsync(acmeId, AbortToken)).Should().BeTrue();
        }

        using (tenant.Change("globex"))
        {
            (await store.FileExistAsync(acmeId, AbortToken)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task should_refuse_a_scoped_store_call_with_no_ambient_tenant()
    {
        // given
        var s3 = new InMemoryS3();
        await using var provider = _BuildS3Host(s3, scoped: true);
        var store = provider.GetRequiredService<TusS3Store>();

        // when
        var create = () => store.CreateFileAsync(10, metadata: null, AbortToken);
        var read = () => store.FileExistAsync(Guid.NewGuid().ToString("n"), AbortToken);

        // then - nothing is written or read outside a tenant
        await create.Should().ThrowAsync<MissingTenantContextException>();
        await read.Should().ThrowAsync<MissingTenantContextException>();
        s3.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    public async Task should_refuse_a_scoped_store_call_whose_tenant_id_cannot_be_a_path_segment(string tenantId)
    {
        // given
        var s3 = new InMemoryS3();
        await using var provider = _BuildS3Host(s3, scoped: true);
        var store = provider.GetRequiredService<TusS3Store>();

        // when
        var create = async () =>
        {
            using (provider.GetRequiredService<ICurrentTenant>().Change(tenantId))
            {
                await store.CreateFileAsync(10, metadata: null, AbortToken);
            }
        };

        // then
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*path segment*");
        s3.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_the_s3_keys_as_they_were_while_uploads_are_not_scoped()
    {
        // given - a host with tenancy but without the seam
        var s3 = new InMemoryS3();
        await using var provider = _BuildS3Host(s3, scoped: false);
        var store = provider.GetRequiredService<TusS3Store>();
        string inTenant;

        // when - one upload inside a tenant and one outside
        using (provider.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
            inTenant = await store.CreateFileAsync(10, metadata: null, AbortToken);
        }

        var outside = await store.CreateFileAsync(10, metadata: null, AbortToken);

        // then
        s3.Exists($"{_Prefix}.tus/{inTenant}/info").Should().BeTrue();
        s3.Exists($"{_Prefix}.tus/{outside}/info").Should().BeTrue();
    }

    [Fact]
    public async Task should_leave_a_store_passed_to_use_store_with_its_own_layout()
    {
        // given - a store the application constructed itself
        await using var host = await TusEndpointTestHost.StartAsync(_Scoped(), AbortToken);
        host.Tenant = "acme";

        // when
        var fileId = TusEndpointTestHost.FileIdOf(await host.CreateUploadAsync(uploadLength: 10));

        // then - the disk store keeps the file where it always does
        host.Store.Should().BeOfType<TusDiskStore>();
        File.Exists(Path.Combine(host.Directory, fileId)).Should().BeTrue();
    }

    private static ServiceProvider _BuildS3Host(InMemoryS3 s3, bool scoped, bool tenancyBeforeTus = false)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddSingleton<IAmazonS3>(s3.Client);

        void addTus() =>
            builder.Services.AddHeadlessTus(tus =>
                tus.UseS3(options =>
                {
                    options.Bucket = "bucket";
                    options.Prefix = _Prefix;
                })
            );

        void addTenancy() =>
            builder.AddHeadlessTenancy(tenancy =>
            {
                if (scoped)
                {
                    tenancy.Tus(tus => tus.ScopeByTenant());
                }
            });

        if (tenancyBeforeTus)
        {
            addTenancy();
            addTus();
        }
        else
        {
            addTus();
            addTenancy();
        }

        if (!scoped)
        {
            builder.Services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        }

        return builder.Services.BuildServiceProvider();
    }

    private static Task<HttpResponseMessage> _SendAsync(TusEndpointTestHost host, string method, string location)
    {
        return method switch
        {
            "HEAD" => host.HeadAsync(location),
            "PATCH" => host.PatchAsync(location, new byte[10], offset: 0),
            "DELETE" => host.DeleteAsync(location),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Not a method this scenario sends."),
        };
    }
}
