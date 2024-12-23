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
}
