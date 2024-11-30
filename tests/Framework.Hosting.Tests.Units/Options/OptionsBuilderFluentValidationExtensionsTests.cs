// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.Hosting.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Options;

public sealed class OptionsBuilderFluentValidationExtensionsTests
{
    [Fact]
    public void should_register_valid_options_when_validate_fluent_validation()
    {
        // given
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSingleton<IValidator<MyOptions>, MyOptionsValidator>();

        serviceCollection.AddOptions<MyOptions>().ValidateFluentValidation();

        // when
        serviceCollection.Configure<MyOptions>(options => options.PropertyName = "Test");

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var result = serviceProvider.GetRequiredService<IOptions<MyOptions>>();

        // then
        result.Value.PropertyName.Should().Be("Test");
    }

    [Fact]
    public void should_fail_for_invalid_options_when_validate_fluent_validation()
    {
        // given
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IValidator<MyOptions>, MyOptionsValidator>();

        serviceCollection.AddOptions<MyOptions>().ValidateFluentValidation();

        // when
        serviceCollection.Configure<MyOptions>(options => options.PropertyName = null);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var result = serviceProvider.GetRequiredService<IOptions<MyOptions>>();

        // then
        var assertions = result.Invoking(r => r.Value).Should().Throw<OptionsValidationException>();
        assertions.And.OptionsType.Should().Be(typeof(MyOptions));
        assertions.And.Failures.Should().ContainSingle("Property MyOptions.PropertyName: Name is required.");
        assertions.And.Message.Should().Be("Property MyOptions.PropertyName: Name is required.");
    }

    #region Helper Classes

    private sealed class MyOptions
    {
        public string? PropertyName { get; set; }
    }

    #endregion

    #region Helper Validators

    private sealed class MyOptionsValidator : AbstractValidator<MyOptions>
    {
        public MyOptionsValidator()
        {
            RuleFor(x => x.PropertyName).NotEmpty().WithMessage("Name is required.");
        }
    }

    #endregion
}
