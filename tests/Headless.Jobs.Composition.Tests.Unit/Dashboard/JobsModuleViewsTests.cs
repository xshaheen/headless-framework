// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Dashboard;

/// <summary>
/// The exception a run and an attempt show: the executor stores a failure as JSON (the outer type and message, then its
/// frames and every inner exception), and the dashboard reads it back as text, never as the stored JSON.
/// </summary>
public sealed class JobsModuleViewsTests : TestBase
{
    private const string _Message = "Chain step 'charge card' failed on purpose.";
    private const string _InnerMessage = "The gateway did not answer.";

    private static readonly string _Stored = ExceptionDetailClassForSerialization.Serialize(_Thrown());

    [Fact]
    public void should_show_the_type_message_and_full_stack_of_a_run_when_its_exception_is_stored_as_json()
    {
        // when
        var view = JobsRunView.From(_Run(_Stored));

        // then
        view.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        view.ExceptionMessage.Should().Be(_Message);
        view.ExceptionStack.Should().Contain($"{nameof(JobsModuleViewsTests)}.{nameof(_Charge)}");
        view.ExceptionStack.Should().Contain($"{nameof(TimeoutException)}: {_InnerMessage}");
        view.ExceptionStack.Should().Contain($"{nameof(JobsModuleViewsTests)}.{nameof(_Gateway)}");
    }

    [Fact]
    public void should_show_the_type_message_and_full_stack_of_an_attempt_when_its_exception_is_stored_as_json()
    {
        // when
        var view = JobsAttemptView.From(new TimeJobAttemptEntity { Number = 1, ExceptionMessage = _Stored });

        // then
        view.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        view.ExceptionMessage.Should().Be(_Message);
        view.ExceptionStack.Should().Contain($"{nameof(TimeoutException)}: {_InnerMessage}");
    }

    [Theory]
    [InlineData("Lease lapsed while running!")]
    [InlineData("{ not json")]
    public void should_show_the_stored_text_as_the_message_when_it_is_not_an_exception_json(string stored)
    {
        // when
        var view = JobsRunView.From(_Run(stored));

        // then
        view.ExceptionType.Should().BeNull();
        view.ExceptionMessage.Should().Be(stored);
        view.ExceptionStack.Should().BeNull();
    }

    [Fact]
    public void should_show_no_exception_when_none_is_stored()
    {
        // when
        var view = JobsRunView.From(_Run(null));

        // then
        view.ExceptionType.Should().BeNull();
        view.ExceptionMessage.Should().BeNull();
        view.ExceptionStack.Should().BeNull();
    }

    private static Exception _Thrown()
    {
        try
        {
            _Charge();
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The charge was expected to throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Charge()
    {
        try
        {
            _Gateway();
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(_Message, exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Gateway() => throw new TimeoutException(_InnerMessage);

    private static JobRunSummary _Run(string? exception) =>
        new()
        {
            Kind = JobRunKind.TimeJob,
            Id = Guid.NewGuid(),
            Function = "billing.charge",
            Status = JobStatus.Failed,
            ExceptionMessage = exception,
        };
}
