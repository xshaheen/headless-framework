// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Dashboard;

/// <summary>
/// The exception a run and an attempt show: the executor stores a failure as JSON (outer message and the root
/// exception's top frame), and the dashboard reads it back as a message and a frame, never as the stored JSON.
/// </summary>
public sealed class JobsModuleViewsTests : TestBase
{
    private const string _Message = "Chain step 'charge card' failed on purpose.";
    private const string _Frame = "MoveNext at offset 596 in /src/Sandbox/SandboxJobs.cs:96:13";

    private static readonly string _Stored = JsonSerializer.Serialize(
        new ExceptionDetailClassForSerialization { Message = _Message, StackTrace = _Frame },
        JobsExceptionJsonContext.Default.ExceptionDetailClassForSerialization
    );

    [Fact]
    public void should_show_the_message_and_frame_of_a_run_when_its_exception_is_stored_as_json()
    {
        // when
        var view = JobsRunView.From(_Run(_Stored));

        // then
        view.ExceptionMessage.Should().Be(_Message);
        view.ExceptionFrame.Should().Be(_Frame);
    }

    [Fact]
    public void should_show_the_message_and_frame_of_an_attempt_when_its_exception_is_stored_as_json()
    {
        // when
        var view = JobsAttemptView.From(new TimeJobAttemptEntity { Number = 1, ExceptionMessage = _Stored });

        // then
        view.ExceptionMessage.Should().Be(_Message);
        view.ExceptionFrame.Should().Be(_Frame);
    }

    [Theory]
    [InlineData("Lease lapsed while running!")]
    [InlineData("{ not json")]
    public void should_show_the_stored_text_as_the_message_when_it_is_not_an_exception_json(string stored)
    {
        // when
        var view = JobsRunView.From(_Run(stored));

        // then
        view.ExceptionMessage.Should().Be(stored);
        view.ExceptionFrame.Should().BeNull();
    }

    [Fact]
    public void should_show_no_exception_when_none_is_stored()
    {
        // when
        var view = JobsRunView.From(_Run(null));

        // then
        view.ExceptionMessage.Should().BeNull();
        view.ExceptionFrame.Should().BeNull();
    }

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
