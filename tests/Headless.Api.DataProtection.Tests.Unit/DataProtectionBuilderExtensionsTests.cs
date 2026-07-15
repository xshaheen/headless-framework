// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Headless.Api.DataProtection;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DataProtectionBuilderExtensionsTests : TestBase
{
    #region PersistKeysToBlobStorage(storage, loggerFactory) Tests

    [Fact]
    public void should_configure_xml_repository_with_storage()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();

        // when
        builder.PersistKeysToBlobStorage(storage);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_pass_logger_factory_to_repository()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        var loggerFactory = Substitute.For<ILoggerFactory>();

        // when
        builder.PersistKeysToBlobStorage(storage, loggerFactory);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_work_without_logger_factory()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();

        // when
        builder.PersistKeysToBlobStorage(storage, loggerFactory: null);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_return_builder_for_chaining()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();

        // when
        var result = builder.PersistKeysToBlobStorage(storage);

        // then
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void should_throw_at_config_time_when_storage_requires_provisioning_and_no_manager()
    {
        // given: an Azure/S3/file-system-shaped storage — a missing container is an error the repository can never fix
        // because the storage-only overload has no manager to ensure it.
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);

        // when
        var act = () => builder.PersistKeysToBlobStorage(storage);

        // then: fail at configuration time, not at the first key write, with the actionable fixes named.
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*DataProtection*")
            .WithMessage("*IBlobContainerManager*")
            .WithMessage("*BlobContainerProvisioning.PreProvisioned*")
            .WithMessage("*Cloudflare R2*");
    }

    [Fact]
    public void should_not_throw_when_pre_provisioned_is_acknowledged()
    {
        // given: the container was provisioned out-of-band (the Cloudflare R2 story), which the caller acknowledges.
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);

        // when
        builder.PersistKeysToBlobStorage(storage, provisioning: BlobContainerProvisioning.PreProvisioned);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_not_require_acknowledgment_for_lazy_backend()
    {
        // given: a Redis-shaped storage — containers materialize on first write, so no provisioning step exists.
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(false);

        // when
        builder.PersistKeysToBlobStorage(storage);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    #endregion

    #region PersistKeysToBlobStorage(storage, containerManager) Tests

    [Fact]
    public async Task should_ensure_container_via_manager_before_first_store()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        var containerManager = Substitute.For<IBlobContainerManager>();

        // when
        builder.PersistKeysToBlobStorage(storage, containerManager);

        // then
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();

        // The first key write must ensure the DataProtection container through the supplied manager: the blob data
        // plane treats a missing container as an error, so a fresh deployment would otherwise fail right here.
        options.XmlRepository!.StoreElement(new XElement("key"), "friendly");

        await containerManager.Received(1).EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .UploadAsync(
                new BlobLocation("DataProtection", "friendly.xml"),
                Arg.Any<Stream>(),
                metadata: null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_require_acknowledgment_when_manager_is_supplied()
    {
        // given: a provisioning-requiring storage is fine without any acknowledgment when a manager covers the ensure.
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        var containerManager = Substitute.For<IBlobContainerManager>();

        // when
        builder.PersistKeysToBlobStorage(storage, containerManager);

        // then: configures without acknowledgment and the ensure still runs before the first write.
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository!.StoreElement(new XElement("key"), "friendly");

        await containerManager.Received(1).EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
    }

    #endregion

    #region PersistKeysToBlobStorage(storageFactory) Tests

    [Fact]
    public void should_resolve_storage_from_factory()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        var factoryInvoked = false;

        // when
        builder.PersistKeysToBlobStorage(sp =>
        {
            factoryInvoked = true;
            return storage;
        });

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        factoryInvoked.Should().BeTrue();
        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_resolve_logger_factory_from_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();

        // when
        builder.PersistKeysToBlobStorage(_ => storage);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_register_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        var invokeCount = 0;

        builder.PersistKeysToBlobStorage(_ =>
        {
            invokeCount++;
            return storage;
        });

        // when
        using var provider = services.BuildServiceProvider();
        var options1 = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        var options2 = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        // then - factory invoked once for singleton registration
        invokeCount.Should().Be(1);
        options1.XmlRepository.Should().BeSameAs(options2.XmlRepository);
    }

    #endregion

    #region PersistKeysToBlobStorage() (parameterless) Tests

    [Fact]
    public void should_resolve_storage_from_di()
    {
        // given
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        services.AddSingleton(storage);
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage();

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_throw_when_storage_not_registered()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage();

        // then
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_resolve_logger_factory_from_di()
    {
        // given
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        services.AddSingleton(storage);
        services.AddLogging();
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage();

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().NotBeNull();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    #endregion

    #region PersistKeysToBlobStorage(serviceKey) (keyed) Tests

    [Fact]
    public void should_resolve_keyed_storage_and_container_manager()
    {
        // given
        const string key = "dpkeys";
        var services = new ServiceCollection();
        services.AddKeyedSingleton(key, Substitute.For<IBlobStorage>());
        services.AddKeyedSingleton(key, Substitute.For<IBlobContainerManager>());
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage(key);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_resolve_keyed_storage_even_without_a_keyed_container_manager()
    {
        // A keyed store with no matching keyed manager still configures (ensure becomes a no-op); it must not throw.
        const string key = "dpkeys";
        var services = new ServiceCollection();
        services.AddKeyedSingleton(key, Substitute.For<IBlobStorage>());
        var builder = services.AddDataProtection();

        builder.PersistKeysToBlobStorage(key);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_throw_when_keyed_storage_not_registered()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage("missing-key");

        // then
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region PersistKeysToBlobStorage(storageFactory, containerManagerFactory) Tests

    [Fact]
    public void should_invoke_container_manager_factory_when_supplied()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var managerFactoryInvoked = false;

        // when
        builder.PersistKeysToBlobStorage(
            _ => Substitute.For<IBlobStorage>(),
            _ =>
            {
                managerFactoryInvoked = true;
                return Substitute.For<IBlobContainerManager>();
            }
        );

        // then
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        managerFactoryInvoked.Should().BeTrue();
    }

    [Fact]
    public void should_not_fall_back_to_unkeyed_manager_when_factory_returns_null()
    {
        // A supplied factory is authoritative: returning null must NOT silently resolve the unkeyed default manager
        // (which would ensure the wrong store's container). The unkeyed manager here must never be resolved.
        var services = new ServiceCollection();
        var unkeyedManager = Substitute.For<IBlobContainerManager>();
        services.AddSingleton(unkeyedManager);
        var builder = services.AddDataProtection();
        var factoryReturnedNull = false;

        // when
        builder.PersistKeysToBlobStorage(
            _ => Substitute.For<IBlobStorage>(),
            _ =>
            {
                factoryReturnedNull = true;
                return null;
            }
        );

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        factoryReturnedNull.Should().BeTrue();
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
        // The unkeyed manager was registered but must not have been touched, proving no silent fallback.
        unkeyedManager.ReceivedCalls().Should().BeEmpty();
    }

    #endregion

    #region Provisioning guardrail (DI paths)

    [Fact]
    public void should_throw_at_first_resolution_when_factory_path_manager_resolves_null_and_storage_requires_provisioning()
    {
        // given: the manager factory is authoritative and returns null (the CloudflareR2 shape — no manager exists),
        // while the resolved storage demands a provisioned container.
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);

        builder.PersistKeysToBlobStorage(_ => storage, _ => null);

        // when / then: the misconfiguration surfaces at the first options resolution, not at the first key write.
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*BlobContainerProvisioning.PreProvisioned*");
    }

    [Fact]
    public void should_resolve_when_factory_path_pre_provisioned_is_acknowledged()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddDataProtection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);

        builder.PersistKeysToBlobStorage(
            _ => storage,
            _ => null,
            provisioning: BlobContainerProvisioning.PreProvisioned
        );

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        // then
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_throw_at_first_resolution_when_keyed_path_no_keyed_manager_and_storage_requires_provisioning()
    {
        // given: a keyed store with no matching keyed manager cannot ensure its container.
        const string key = "dpkeys";
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        services.AddKeyedSingleton(key, storage);
        var builder = services.AddDataProtection();

        builder.PersistKeysToBlobStorage(key);

        // when / then
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*BlobContainerProvisioning.PreProvisioned*");
    }

    [Fact]
    public void should_resolve_when_keyed_path_pre_provisioned_is_acknowledged()
    {
        // given
        const string key = "dpkeys";
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        services.AddKeyedSingleton(key, storage);
        var builder = services.AddDataProtection();

        builder.PersistKeysToBlobStorage(key, provisioning: BlobContainerProvisioning.PreProvisioned);

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        // then
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void should_throw_at_first_resolution_when_parameterless_path_no_manager_registered_and_storage_requires_provisioning()
    {
        // given: an unkeyed store registered without any IBlobContainerManager.
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        services.AddSingleton(storage);
        var builder = services.AddDataProtection();

        builder.PersistKeysToBlobStorage();

        // when / then
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*BlobContainerProvisioning.PreProvisioned*");
    }

    [Fact]
    public void should_resolve_when_parameterless_path_pre_provisioned_is_acknowledged()
    {
        // given
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        services.AddSingleton(storage);
        var builder = services.AddDataProtection();

        builder.PersistKeysToBlobStorage(provisioning: BlobContainerProvisioning.PreProvisioned);

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        // then
        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public async Task should_still_use_manager_even_when_di_paths_pre_provisioned_is_acknowledged()
    {
        // PreProvisioned only suppresses the guardrail; a manager that IS resolvable must still be wired so the
        // repository keeps ensuring the container before writes.
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        var containerManager = Substitute.For<IBlobContainerManager>();
        services.AddSingleton(storage);
        services.AddSingleton(containerManager);
        var builder = services.AddDataProtection();

        builder.PersistKeysToBlobStorage(provisioning: BlobContainerProvisioning.PreProvisioned);

        // when
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        options.XmlRepository!.StoreElement(new XElement("key"), "friendly");

        // then
        await containerManager.Received(1).EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
    }

    #endregion

    #region Overload resolution pins

    [Fact]
    public void bool_argument_binds_the_keyed_service_overload()
    {
        // Overload-resolution pin: a bool literal is a legitimate keyed-service key. It must bind to
        // (object serviceKey) — never to a provisioning acknowledgment — or a bool-keyed consumer silently
        // routes to the wrong (unkeyed) store. This is why the acknowledgment is an enum, not a bool.
        var services = new ServiceCollection();
        var keyedStorage = Substitute.For<IBlobStorage>(); // Redis-shaped (RequiresContainerProvisioning=false)
        services.AddKeyedSingleton(true, keyedStorage);
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage(true);

        // then: only the keyed registration exists, so successful resolution proves the keyed lookup ran under
        // key `true` (an unkeyed lookup would throw for the missing plain IBlobStorage).
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    [Fact]
    public void enum_argument_binds_the_provisioning_overload_not_the_keyed_one()
    {
        // Overload-resolution pin: an exact enum match must beat boxing to (object serviceKey). Only the unkeyed
        // storage is registered, so successful resolution proves the enum overload ran (the keyed overload would
        // throw looking up a store keyed by the enum value).
        var services = new ServiceCollection();
        var storage = Substitute.For<IBlobStorage>();
        storage.RequiresContainerProvisioning.Returns(true);
        services.AddSingleton(storage);
        var builder = services.AddDataProtection();

        // when
        builder.PersistKeysToBlobStorage(BlobContainerProvisioning.PreProvisioned);

        // then
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlRepository.Should().BeOfType<BlobStorageDataProtectionXmlRepository>();
    }

    #endregion
}
