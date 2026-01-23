using Microsoft.Extensions.DependencyInjection;

namespace Tests.DependencyInjection;

public sealed class DependencyInjectionExtensionsTests
{
    [Fact]
    public void add_if_should_execute_the_action_if_the_condition_to_add_is_true()
    {
        // given
        var services = new ServiceCollection();
        var wasActionCalled = false;

        // when
        services.AddIf(
            true,
            s =>
            {
                wasActionCalled = true;

                return s;
            }
        );

        // then
        wasActionCalled.Should().BeTrue();
    }

    [Fact]
    public void add_if_should_not_execute_the_action_if_the_condition_to_add_is_false()
    {
        // given
        var services = new ServiceCollection();
        var wasActionCalled = false;

        // when
        services.AddIf(
            false,
            s =>
            {
                wasActionCalled = true;

                return s;
            }
        );

        // then
        wasActionCalled.Should().BeFalse();
    }

    [Fact]
    public void add_if_should_add_the_service_to_the_service_collection_if_the_add_condition_is_true()
    {
        // given
        var services = new ServiceCollection();
        var serviceDescriptor = new ServiceDescriptor(typeof(string), "Test Service");

        // when
        services.AddIf(
            true,
            s =>
            {
                s.Add(serviceDescriptor);

                return s;
            }
        );

        // then
        services.Should().Contain(serviceDescriptor);
    }

    [Fact]
    public void add_if_should_not_add_the_service_to_the_service_collection_if_the_add_condition_is_false()
    {
        // given
        var services = new ServiceCollection();
        var serviceDescriptor = new ServiceDescriptor(typeof(string), "Test Service");

        // when
        services.AddIf(
            false,
            s =>
            {
                s.Add(serviceDescriptor);

                return s;
            }
        );

        // then
        services.Should().NotContain(serviceDescriptor);
    }

    [Fact]
    public void add_if_else_should_invoke_if_action_when_condition_is_true()
    {
        // given
        var services = new ServiceCollection();

        var ifActionCalled = false;
        var elseActionCalled = false;

        // when
        services.AddIfElse(
            true,
            sc =>
            {
                ifActionCalled = true;

                return sc;
            },
            sc =>
            {
                elseActionCalled = true;

                return sc;
            }
        );

        // then
        ifActionCalled.Should().BeTrue();
        elseActionCalled.Should().BeFalse();
    }

    [Fact]
    public void add_if_else_should_invoke_else_action_when_condition_is_false()
    {
        // given
        var services = new ServiceCollection();

        var ifActionInvoked = false;
        var elseActionInvoked = false;

        // when
        services.AddIfElse(
            false,
            sc =>
            {
                ifActionInvoked = true;

                return sc;
            },
            sc =>
            {
                elseActionInvoked = true;

                return sc;
            }
        );

        // then
        ifActionInvoked.Should().BeFalse();
        elseActionInvoked.Should().BeTrue();
    }

    [Fact]
    public void add_if_else_should_add_if_action_when_condition_is_false()
    {
        // given
        var services = new ServiceCollection();

        var ifServiceDescriptor = new ServiceDescriptor(typeof(string), "if service");
        var elseServiceDescriptor = new ServiceDescriptor(typeof(string), "else service");

        // when
        services.AddIfElse(
            true,
            sc =>
            {
                sc.Add(ifServiceDescriptor);

                return sc;
            },
            sc =>
            {
                sc.Add(elseServiceDescriptor);

                return sc;
            }
        );

        // then
        services.Should().NotContain(elseServiceDescriptor);
        services.Should().Contain(ifServiceDescriptor);
    }

    [Fact]
    public void add_if_else_should_add_else_action_when_condition_is_false()
    {
        // given
        var services = new ServiceCollection();

        var ifServiceDescriptor = new ServiceDescriptor(typeof(string), "if service");
        var elseServiceDescriptor = new ServiceDescriptor(typeof(string), "else service");

        // when
        services.AddIfElse(
            false,
            sc =>
            {
                sc.Add(ifServiceDescriptor);

                return sc;
            },
            sc =>
            {
                sc.Add(elseServiceDescriptor);

                return sc;
            }
        );

        // then
        services.Should().Contain(elseServiceDescriptor);
        services.Should().NotContain(ifServiceDescriptor);
    }

    [Fact]
    public void replace_scoped_should_replace_service_when_it_exists()
    {
        // given
        var services = new ServiceCollection();
        var originalServiceMock = Substitute.For<IMyService>();
        originalServiceMock.Greet().Returns("original");

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        services.AddScoped<IMyService>(_ => originalServiceMock);

        // when
        var result = services.AddOrReplaceScoped<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeTrue();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void replace_scoped_with_implementation_params_should_replace_service()
    {
        // given
        var services = new ServiceCollection();

        services.AddScoped<IMyService, MyService>();

        // when
        services.AddOrReplaceScoped<IMyService, ReplacementService>();
        var provider = services.BuildServiceProvider();
        var myService = provider.GetService<IMyService>();

        // then
        myService.Should().NotBeNull();
        myService.Greet().Should().Be("replacement");
    }

    [Fact]
    public void add_or_replace_scoped_should_replace_service_when_it_doesnt_exist()
    {
        // given
        var services = new ServiceCollection();

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        // when
        var result = services.AddOrReplaceScoped<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeFalse();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void add_or_replace_transient_should_replace_service_when_it_exists()
    {
        // given
        var services = new ServiceCollection();
        var originalServiceMock = Substitute.For<IMyService>();
        originalServiceMock.Greet().Returns("original");

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        services.AddTransient<IMyService>(_ => originalServiceMock);

        // when
        var result = services.AddOrReplaceTransient<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeTrue();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void add_or_replace_transient_with_implementation_params_should_replace_service()
    {
        // given
        var services = new ServiceCollection();

        services.AddTransient<IMyService, MyService>();

        // when
        services.AddOrReplaceTransient<IMyService, ReplacementService>();
        var provider = services.BuildServiceProvider();
        var myService = provider.GetService<IMyService>();

        // then
        myService.Should().NotBeNull();
        myService.Greet().Should().Be("replacement");
    }

    [Fact]
    public void add_or_replace_transient_should_replace_service_when_it_exists_as_another_lifetime()
    {
        // given
        var services = new ServiceCollection();
        var originalServiceMock = Substitute.For<IMyService>();
        originalServiceMock.Greet().Returns("original");

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        services.AddScoped<IMyService>(_ => originalServiceMock);

        // when
        var result = services.AddOrReplaceTransient<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeTrue();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void add_or_replace_transient_should_replace_service_when_it_doesnt_exist()
    {
        // given
        var services = new ServiceCollection();

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        // when
        var isReplace = services.AddOrReplaceTransient<IMyService>(_ => newServiceMock);

        // then
        isReplace.Should().BeFalse();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void add_or_replace_singleton_should_replace_service_when_it_exists()
    {
        // given
        var services = new ServiceCollection();
        var originalServiceMock = Substitute.For<IMyService>();
        originalServiceMock.Greet().Returns("original");

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        services.AddSingleton<IMyService>(_ => originalServiceMock);

        // when
        var result = services.AddOrReplaceSingleton<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeTrue();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void add_or_replace_singleton_with_implementation_params_should_replace_service()
    {
        // given
        var services = new ServiceCollection();

        services.AddSingleton<IMyService, MyService>();

        // when
        services.AddOrReplaceSingleton<IMyService, ReplacementService>();
        var provider = services.BuildServiceProvider();
        var myService = provider.GetService<IMyService>();

        // then
        myService.Should().NotBeNull();
        myService.Greet().Should().Be("replacement");
    }

    [Fact]
    public void add_or_replace_singleton_should_replace_service_when_it_doesnt_exist()
    {
        // given
        var services = new ServiceCollection();

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        // when
        var isReplace = services.AddOrReplaceSingleton<IMyService>(_ => newServiceMock);

        // then
        isReplace.Should().BeFalse();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService.Greet().Should().Be("replace");
    }

    [Fact]
    public void replace_should_replace_service_with_new_factory()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IMyService, MyService>();

        // when
        services.Replace<IMyService>(_ => new ReplacementService());

        var provider = services.BuildServiceProvider();
        var replacementService = provider.GetService<IMyService>();

        // then
        var descriptor = services.Single(d => d.ServiceType == typeof(IMyService));
        descriptor.ImplementationFactory.Should().NotBeNull();
        replacementService.Should().NotBeNull();
        replacementService.Greet().Should().Be("replacement");
    }

    [Fact]
    public void is_added_should_return_true_when_service_is_added()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IMyService, MyService>();

        // when
        var result = services.IsAdded<IMyService>();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void is_added_should_return_false_when_service_is_not_added()
    {
        // given
        var services = new ServiceCollection();

        // when
        var result = services.IsAdded<IMyService>();

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void add_keyed_scoped_should_register_service_with_key()
    {
        // given
        var services = new ServiceCollection();
        const string serviceKey = "myServiceKey";

        // when
        services.AddKeyedScoped<IMyService>(serviceKey, _ => new MyService());
        var provider = services.BuildServiceProvider();
        var myService = provider.GetKeyedService<IMyService>(serviceKey);

        // then
        myService.Should().NotBeNull();
        myService.Greet().Should().Be("original");
    }

    [Fact]
    public void service_provider_should_return_null_when_retrieving_invalid_scoped_keyed_service()
    {
        // given
        var services = new ServiceCollection();
        const string serviceKey = "myServiceKey";

        // when
        services.AddKeyedScoped<IMyService>(serviceKey, _ => new MyService());
        var provider = services.BuildServiceProvider();
        var myServiceWithoutKey = provider.GetKeyedService<IMyService>("invalidKey");

        // then
        myServiceWithoutKey.Should().BeNull();
    }

    [Fact]
    public void add_keyed_transient_should_register_service_with_key()
    {
        // given
        var services = new ServiceCollection();
        const string serviceKey = "myServiceKey";

        // when
        services.AddKeyedTransient<IMyService>(serviceKey, _ => new MyService());
        var provider = services.BuildServiceProvider();
        var myService = provider.GetKeyedService<IMyService>(serviceKey);

        // then
        myService.Should().NotBeNull();
        myService.Greet().Should().Be("original");
    }

    [Fact]
    public void service_provider_should_return_null_when_retrieving_invalid_transient_keyed_service()
    {
        // given
        var services = new ServiceCollection();
        const string serviceKey = "myServiceKey";

        // when
        services.AddKeyedTransient<IMyService>(serviceKey, _ => new MyService());
        var provider = services.BuildServiceProvider();
        var myServiceWithoutKey = provider.GetKeyedService<IMyService>("invalidKey");

        // then
        myServiceWithoutKey.Should().BeNull();
    }
}

public interface IMyService
{
    string Greet();
}

public sealed class MyService : IMyService
{
    public string Greet() => "original";
}

public sealed class ReplacementService : IMyService
{
    public string Greet() => "replacement";
}
