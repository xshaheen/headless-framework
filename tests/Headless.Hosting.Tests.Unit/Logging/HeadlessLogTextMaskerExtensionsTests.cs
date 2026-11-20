// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Hosting;
using Headless.Testing.Tests;

namespace Tests.Logging;

public sealed class HeadlessLogTextMaskerExtensionsTests : TestBase
{
    private const string _NationalId = "29001011234567";

    [Fact]
    public void should_record_the_event_with_its_message_and_rendered_text_masked()
    {
        // given: the id is in the message and, separately, only in the exception's own ToString()
        var masker = new ReplacingMasker();
        using var activity = new Activity("test");
        activity.Start();

        // when
        masker.RecordException(activity, new CitizenException($"lookup for {_NationalId} failed"));

        // then
        var tags = activity.Events.Should().ContainSingle().Which.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags["exception.message"].Should().Be("lookup for *** failed");
        tags["exception.stacktrace"]
            .Should()
            .BeOfType<string>()
            .Which.Should()
            .NotContain(_NationalId)
            .And.Contain("(citizen ***)");
        tags["exception.type"].Should().Be(typeof(CitizenException).FullName);
    }

    [Fact]
    public void should_hand_the_masked_exception_to_an_exception_recorder()
    {
        // given
        var masker = new ReplacingMasker();
        var original = new InvalidOperationException("boom");
        Exception? recorded = null;
        using var source = new ActivitySource(nameof(should_hand_the_masked_exception_to_an_exception_recorder));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ExceptionRecorder = (Activity _, Exception exception, ref TagList _) => recorded = exception,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("test")!;

        // when
        masker.RecordException(activity, original);

        // then
        recorded.Should().BeSameAs(masker.MaskedException);
    }

    [Fact]
    public void should_throw_when_arguments_are_null()
    {
        var masker = new ReplacingMasker();
        using var activity = new Activity("test");

        var nullActivity = () => masker.RecordException(null!, new InvalidOperationException());
        var nullException = () => masker.RecordException(activity, null!);

        nullActivity.Should().Throw<ArgumentNullException>();
        nullException.Should().Throw<ArgumentNullException>();
    }

    private sealed class ReplacingMasker : ILogTextMasker
    {
        public Exception MaskedException { get; } = new InvalidOperationException("masked");

        public string Mask(string text) => text.Replace(_NationalId, "***", StringComparison.Ordinal);

        public Exception Mask(Exception exception) => MaskedException;
    }

    private sealed class CitizenException(string message) : Exception(message)
    {
        public override string ToString() => $"{base.ToString()} (citizen {_NationalId})";
    }
}
