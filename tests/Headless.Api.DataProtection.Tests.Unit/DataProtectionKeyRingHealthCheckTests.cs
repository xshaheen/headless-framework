// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DataProtectionKeyRingHealthCheckTests : TestBase
{
    [Fact]
    public async Task should_write_probe_by_default_even_when_manager_wired()
    {
        // given: default style with a manager wired — the wiring must NOT weaken the guarantee: managers can
        // succeed on read/stat permission while writes are revoked, so the default always write-probes.
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        var (check, context) = _CreateCheck(storage, manager);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then: healthy via the definitive write probe — the sentinel was uploaded AND deleted, and the
        // existence API was never consulted.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.WriteProbeHealthyDescription);
        await _AssertSentinelUploadedAndDeletedAsync(storage);
        await manager.DidNotReceive().ContainerExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_healthy_when_existence_style_and_manager_reports_container_exists()
    {
        // given: the explicit opt-down to the cheap existence probe, and the container exists.
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync(BlobStorageDataProtectionXmlRepository.ContainerName, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        var (check, context) = _CreateCheck(storage, manager, KeyRingProbeStyle.ContainerExistence);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then: healthy via the existence probe — the description names which probe ran, and the cheap
        // manager path never fell through to the sentinel write probe.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.ExistenceProbeHealthyDescription);
        storage.ReceivedCalls().Should().BeEmpty("the existence probe must not touch the storage");
    }

    [Fact]
    public async Task should_report_healthy_with_existence_style_even_when_write_access_revoked()
    {
        // given: the container exists but writes are revoked — this pins the DOCUMENTED weaker guarantee of the
        // opt-down: ContainerExistence does not verify write access, so this still reports Healthy.
        var storage = Substitute.For<IBlobStorage>();
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated lost write access"));
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync(BlobStorageDataProtectionXmlRepository.ContainerName, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        var (check, context) = _CreateCheck(storage, manager, KeyRingProbeStyle.ContainerExistence);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.ExistenceProbeHealthyDescription);
    }

    [Fact]
    public async Task should_report_unhealthy_when_existence_style_and_manager_reports_container_missing()
    {
        // given: the container manager answers, but the DataProtection container is gone (deleted after boot —
        // exactly the drift the one-shot startup gate cannot see).
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync(BlobStorageDataProtectionXmlRepository.ContainerName, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var (check, context) = _CreateCheck(storage, manager, KeyRingProbeStyle.ContainerExistence);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.ContainerMissingDescription);
        result.Description.Should().Contain("key rotation will fail");
    }

    [Fact]
    public async Task should_report_unhealthy_with_exception_when_existence_probe_throws()
    {
        // given: the existence probe itself fails (e.g. credentials revoked after boot).
        var backendFailure = new HttpRequestException("Simulated lost credentials");
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync(BlobStorageDataProtectionXmlRepository.ContainerName, Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<bool>(backendFailure));
        var (check, context) = _CreateCheck(storage, manager, KeyRingProbeStyle.ContainerExistence);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then: the backend exception is surfaced on the result, not thrown out of the check.
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.ExistenceProbeFailedDescription);
        result.Exception.Should().BeSameAs(backendFailure);
    }

    [Fact]
    public async Task should_fall_back_to_write_probe_when_existence_style_requested_without_manager()
    {
        // given: the opt-down was requested but no manager is wired (pre-provisioned mode) — there is no
        // existence API to ask, so the stronger write probe is the only probe possible. That is a legitimate
        // wiring, not misuse, so it must NOT report Degraded.
        var storage = Substitute.For<IBlobStorage>();
        var (check, context) = _CreateCheck(storage, manager: null, KeyRingProbeStyle.ContainerExistence);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then: healthy via the write probe, with a description noting the fallback.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.ExistenceFallbackHealthyDescription);
        await _AssertSentinelUploadedAndDeletedAsync(storage);
    }

    [Fact]
    public async Task should_report_healthy_via_write_probe_when_no_manager_wired()
    {
        // given: default style, pre-provisioned wiring (no manager). The bare substitute accepts uploads and
        // deletes.
        var storage = Substitute.For<IBlobStorage>();
        var (check, context) = _CreateCheck(storage, manager: null);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then: healthy via the write probe (distinct description from the existence probe), and the sentinel
        // was uploaded AND deleted so it does not accumulate.
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.WriteProbeHealthyDescription);
        await _AssertSentinelUploadedAndDeletedAsync(storage);
    }

    [Fact]
    public async Task should_report_unhealthy_with_exception_when_write_probe_fails()
    {
        // given: no manager and lost write access — the sentinel upload fails terminally.
        var storage = Substitute.For<IBlobStorage>();
        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated lost write access"));
        var (check, context) = _CreateCheck(storage, manager: null);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then: the probe's contextual wrap (naming the container and remediation) is surfaced on the result.
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.WriteProbeFailedDescription);
        result
            .Exception.Should()
            .BeOfType<InvalidOperationException>()
            .Which.InnerException.Should()
            .BeOfType<NotSupportedException>();
    }

    [Fact]
    public async Task should_report_degraded_when_xml_repository_is_not_blob_backed()
    {
        // given: the key ring persists somewhere else entirely — registration misuse, not an outage.
        var check = new DataProtectionKeyRingHealthCheck(
            Options.Create(new KeyManagementOptions { XmlRepository = Substitute.For<IXmlRepository>() }),
            KeyRingProbeStyle.WriteProbe
        );
        var context = _CreateContext(check);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not persisting to blob storage");
    }

    [Fact]
    public async Task should_report_degraded_when_no_xml_repository_configured()
    {
        // given: nothing configured the key ring at all (XmlRepository is null).
        var check = new DataProtectionKeyRingHealthCheck(
            Options.Create(new KeyManagementOptions()),
            KeyRingProbeStyle.WriteProbe
        );
        var context = _CreateContext(check);

        // when
        var result = await check.CheckHealthAsync(context, AbortToken);

        // then
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not persisting to blob storage").And.Contain("<none>");
    }

    [Fact]
    public void should_register_check_with_default_name_and_empty_tags()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHealthChecks().AddDataProtectionKeyRing();

        // then: the registration carries the defaults and its factory DI-activates our check (a missing
        // data-protection setup must not break resolution — IOptions falls back to an empty instance).
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Should().ContainSingle().Which;
        registration.Name.Should().Be(DataProtectionHealthChecksExtensions.DefaultName);
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        registration.Tags.Should().BeEmpty();
        registration.Factory(provider).Should().BeOfType<DataProtectionKeyRingHealthCheck>();
    }

    [Fact]
    public void should_register_check_with_custom_name_failure_status_tags_and_probe_style()
    {
        // given
        var services = new ServiceCollection();

        // when
        services
            .AddHealthChecks()
            .AddDataProtectionKeyRing(
                name: "keys",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"],
                probeStyle: KeyRingProbeStyle.ContainerExistence
            );

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Should().ContainSingle().Which;
        registration.Name.Should().Be("keys");
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
        registration.Tags.Should().BeEquivalentTo(["ready"]);
    }

    [Fact]
    public async Task should_flow_probe_style_through_registration_factory()
    {
        // given: a ContainerExistence registration resolved through DI — the style must reach the activated
        // check, not just the registration metadata.
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync(BlobStorageDataProtectionXmlRepository.ContainerName, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        services.AddDataProtection().PersistKeysToBlobStorage(storage, manager);
        services.AddHealthChecks().AddDataProtectionKeyRing(probeStyle: KeyRingProbeStyle.ContainerExistence);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Should().ContainSingle().Which;
        var check = registration.Factory(provider);

        // when
        var result = await check.CheckHealthAsync(new HealthCheckContext { Registration = registration }, AbortToken);

        // then: the existence probe ran (not the default write probe).
        result.Description.Should().Be(DataProtectionKeyRingHealthCheck.ExistenceProbeHealthyDescription);
        storage.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_resolve_from_di_and_report_through_health_check_service()
    {
        // given: a full DI composition — data protection persisting to a managed blob store. The default style
        // write-probes even though a manager is wired.
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(LoggerProvider));
        var storage = Substitute.For<IBlobStorage>();
        var manager = Substitute.For<IBlobContainerManager>();
        services.AddDataProtection().PersistKeysToBlobStorage(storage, manager);
        services.AddHealthChecks().AddDataProtectionKeyRing();

        await using var provider = services.BuildServiceProvider();

        // when: the real HealthCheckService runs the registered checks.
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries.Should().ContainKey(DataProtectionHealthChecksExtensions.DefaultName).WhoseValue;
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Description.Should().Be(DataProtectionKeyRingHealthCheck.WriteProbeHealthyDescription);
    }

    #region Helper Methods

    /// <summary>Builds the check against a repository wired directly with the given storage/manager pair.</summary>
    private static (DataProtectionKeyRingHealthCheck Check, HealthCheckContext Context) _CreateCheck(
        IBlobStorage storage,
        IBlobContainerManager? manager,
        KeyRingProbeStyle probeStyle = KeyRingProbeStyle.WriteProbe
    )
    {
        var repository = new BlobStorageDataProtectionXmlRepository(storage, manager);
        var check = new DataProtectionKeyRingHealthCheck(
            Options.Create(new KeyManagementOptions { XmlRepository = repository }),
            probeStyle
        );

        return (check, _CreateContext(check));
    }

    private static HealthCheckContext _CreateContext(IHealthCheck check)
    {
        return new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                DataProtectionHealthChecksExtensions.DefaultName,
                check,
                failureStatus: null,
                tags: null
            ),
        };
    }

    private static async Task _AssertSentinelUploadedAndDeletedAsync(IBlobStorage storage)
    {
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .DeleteAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion
}
