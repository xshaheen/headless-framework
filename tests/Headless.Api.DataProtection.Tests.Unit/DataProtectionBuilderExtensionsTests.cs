// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
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
}
