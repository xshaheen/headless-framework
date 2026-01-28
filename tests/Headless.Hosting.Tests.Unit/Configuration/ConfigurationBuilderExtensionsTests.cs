using Microsoft.Extensions.Configuration;

namespace Tests.Configuration;

public sealed class ConfigurationBuilderExtensionsTests
{
    [Fact]
    public void configuration_builder_add_if_should_add_configuration_if_condition_is_true()
    {
        // given
        var builder = new ConfigurationBuilder();
        const bool shouldAdd = true;
        var isActionExecuted = false;

        // when
        builder.AddIf(
            shouldAdd,
            b =>
            {
                isActionExecuted = true;

                return b.AddInMemoryCollection([new KeyValuePair<string, string?>("Key", "Secret")]);
            }
        );

        var configuration = builder.Build();

        // then
        isActionExecuted.Should().BeTrue();
        configuration.GetSection("Key").Value.Should().NotBeNull().And.Be("Secret");
    }

    [Fact]
    public void configuration_builder_add_if_should_not_add_configuration_if_condition_is_false()
    {
        // given
        var builder = new ConfigurationBuilder();
        const bool shouldAdd = false;
        var isActionExecuted = false;

        // when
        builder.AddIf(
            shouldAdd,
            b =>
            {
                isActionExecuted = true;

                return b.AddInMemoryCollection([new KeyValuePair<string, string?>("Key", "Secret")]);
            }
        );

        var configuration = builder.Build();

        // then
        isActionExecuted.Should().BeFalse();
        configuration.GetSection("Key").Value.Should().BeNull();
    }

    [Fact]
    public void configuration_builder_add_if_else_should_execute_if_action_when_condition_is_true()
    {
        // given
        var builder = new ConfigurationBuilder();
        const bool condition = true;
        var isIfActionExecuted = false;
        var isElseActionExecuted = false;

        // when
        builder.AddIfElse(
            condition,
            b =>
            {
                isIfActionExecuted = true;
                return b.AddInMemoryCollection([new KeyValuePair<string, string?>("Key", "Secret")]);
            },
            b =>
            {
                isElseActionExecuted = true;
                return b;
            }
        );

        var configuration = builder.Build();

        // then
        isIfActionExecuted.Should().BeTrue();
        isElseActionExecuted.Should().BeFalse();
        configuration.GetSection("Key").Value.Should().Be("Secret");
    }

    [Fact]
    public void configuration_builder_add_if_else_should_execute_else_action_when_condition_is_false()
    {
        // given
        var builder = new ConfigurationBuilder();
        const bool condition = false;
        var isIfActionExecuted = false;
        var isElseActionExecuted = false;

        // when
        builder.AddIfElse(
            condition,
            b =>
            {
                isIfActionExecuted = true;
                return b.AddInMemoryCollection([new KeyValuePair<string, string?>("Key", "Secret")]);
            },
            b =>
            {
                isElseActionExecuted = true;
                return b.AddInMemoryCollection([new KeyValuePair<string, string?>("Key", "ElseSecret")]);
            }
        );

        var configuration = builder.Build();

        // then
        isIfActionExecuted.Should().BeFalse();
        isElseActionExecuted.Should().BeTrue();
        configuration.GetSection("Key").Value.Should().Be("ElseSecret");
    }
}
