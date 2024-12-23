using Microsoft.Extensions.DependencyInjection;

namespace Tests.DependencyInjection;

public class DependencyInjectionExtensionsTests
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
        var result = services.ReplaceScoped<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeTrue();
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService!.Greet().Should().Be("replace");
    }

#warning Ask Shaheen about this. If the service doesn't exist, the ReplaceScoped method will return false but it will add the new service which might confuse the user and cause unintentional bug
    [Fact]
    public void replace_scoped_should_replace_service_when_it_doesnt_exist()
    {
        // given
        var services = new ServiceCollection();

        var newServiceMock = Substitute.For<IMyService>();
        newServiceMock.Greet().Returns("replace");

        // when
        var result = services.ReplaceScoped<IMyService>(_ => newServiceMock);

        // then
        result.Should().BeFalse(); // TODO: - MINA - Ask Shaheen about this.
        var provider = services.BuildServiceProvider();
        var resolvedService = provider.GetService<IMyService>();

        resolvedService.Should().NotBeNull();
        resolvedService.Should().Be(newServiceMock);
        resolvedService!.Greet().Should().Be("replace");
    }
}

public interface IMyService
{
    string Greet();
}
