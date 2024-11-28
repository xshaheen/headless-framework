// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.Hosting.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public class OptionsBuilderFluentValidationExtensionsTests
{
    [Fact]
    public void validate_fluent_validation_should_register_is_validate_options()
    {
        // given
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddSingleton<IValidator<MyOptions>, MyOptionsValidator>();

        serviceCollection.AddOptions<MyOptions>().ValidateFluentValidation();

        // when
        serviceCollection.Configure<MyOptions>(
            options => options.PropertyName = "Test"
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var result = serviceProvider.GetRequiredService<IOptions<MyOptions>>();

        // then
        result.Value.PropertyName.Should().Be("Test");
    }

    [Fact]
    public void validate_fluent_validation_should_fail_for_invalid_options()
    {
        // given
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IValidator<MyOptions>, MyOptionsValidator>();

        serviceCollection.AddOptions<MyOptions>()
            .ValidateFluentValidation();

        // when
        serviceCollection.Configure<MyOptions>(
            options => options.PropertyName = null
        );

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var result = serviceProvider.GetRequiredService<IOptions<MyOptions>>();

        // then
        result.Invoking(r => r.Value)
            .Should().Throw<OptionsValidationException>();
    }

    #region Helper Classes

    private class MyOptions
    {
        public string? PropertyName { get; set; }
    }

    #endregion

    #region Helper Validators

    private class MyOptionsValidator : AbstractValidator<MyOptions>
    {
        public MyOptionsValidator()
        {
            RuleFor(x => x.PropertyName).NotNull().NotEmpty().WithMessage("Name is required.");
        }
    }

    #endregion
}
