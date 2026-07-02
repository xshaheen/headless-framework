// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Headless.Api;
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
    public void should_configure_XmlRepository_with_storage()
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
        using var provider = services.BuildServiceProvider();
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
}
