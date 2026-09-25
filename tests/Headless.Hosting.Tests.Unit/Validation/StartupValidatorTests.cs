// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Validation;

public sealed class StartupValidatorTests : TestBase
{
    [Fact]
    public async Task should_start_when_every_validator_passes()
    {
        // given
        var services = new ServiceCollection();
        services.AddStartupValidator(_ => new ScriptedValidator());
        services.AddStartupValidator(_ => new ScriptedValidator());

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_rethrow_a_single_failure_unwrapped_after_running_every_validator()
    {
        // given
        var after = new ScriptedValidator();
        var services = new ServiceCollection();
        services.AddStartupValidator(_ => new ScriptedValidator { Failure = new TypedStartupException("broken") });
        services.AddStartupValidator(_ => after);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().ThrowExactlyAsync<TypedStartupException>().WithMessage("broken");
        after.Calls.Should().Be(1);
    }

    [Fact]
    public async Task should_report_every_failure_together_when_several_validators_fail()
    {
        // given
        var first = new InvalidOperationException("first problem");
        var second = new TypedStartupException("second problem");
        var services = new ServiceCollection();
        services.AddStartupValidator(_ => new ScriptedValidator { Failure = first });
        services.AddStartupValidator(_ => new ScriptedValidator());
        services.AddStartupValidator(_ => new ScriptedValidator { Failure = second });

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowExactlyAsync<StartupValidationException>()).Which;
        exception.InnerExceptions.Should().Equal(first, second);
        exception.Message.Should().Contain("2 checks").And.Contain("first problem").And.Contain("second problem");
    }

    [Fact]
    public async Task should_propagate_cancellation_instead_of_reporting_it_as_a_failure()
    {
        // given
        using var startup = new CancellationTokenSource();
        var after = new ScriptedValidator();
        var services = new ServiceCollection();
        services.AddStartupValidator(_ => new ScriptedValidator { OnValidate = startup.Cancel });
        services.AddStartupValidator(_ => after);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => _Runner(provider).StartingAsync(startup.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        after.Calls.Should().Be(0);
    }

    [Fact]
    public void should_register_one_runner_and_one_validator_per_type()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddStartupValidator<PassingValidator>();
        services.AddStartupValidator<PassingValidator>();
#pragma warning disable CA2263 // False positive: this call exists to prove the Type overload dedupes against the generic one.
        services.AddStartupValidator(typeof(PassingValidator));
#pragma warning restore CA2263

        // then
        services.Count(descriptor => descriptor.ServiceType == typeof(IStartupValidator)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void should_add_a_validator_for_every_factory_call()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddStartupValidator(_ => new ScriptedValidator());
        services.AddStartupValidator(_ => new ScriptedValidator());

        // then
        services.Count(descriptor => descriptor.ServiceType == typeof(IStartupValidator)).Should().Be(2);
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void should_reject_a_type_that_is_not_a_validator()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddStartupValidator(typeof(string));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*IStartupValidator*");
    }

    [Fact]
    public async Task should_fail_the_host_before_any_hosted_service_starts()
    {
        // given
        var worker = new RecordingHostedService();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => worker);
        builder.Services.AddStartupValidator(_ => new ScriptedValidator { Failure = new TypedStartupException("x") });
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<TypedStartupException>();
        worker.Started.Should().BeFalse();
    }

    private static async Task _RunStartingAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
        await _Runner(provider).StartingAsync(AbortToken);
    }

    private static IHostedLifecycleService _Runner(IServiceProvider provider)
    {
        // The runner is internal; drive it the way the host does.
        return provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>().Single();
    }

    private sealed class TypedStartupException(string message) : InvalidOperationException(message);

    private sealed class ScriptedValidator : IStartupValidator
    {
        public Exception? Failure { get; init; }

        public Action? OnValidate { get; init; }

        public int Calls { get; private set; }

        public Task ValidateAsync(CancellationToken cancellationToken)
        {
            Calls++;
            OnValidate?.Invoke();

            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class PassingValidator : IStartupValidator
    {
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHostedService : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
