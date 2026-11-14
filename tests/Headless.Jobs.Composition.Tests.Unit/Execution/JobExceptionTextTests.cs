// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Execution;

/// <summary>
/// A failed attempt stores its exception as JSON on the run row and the attempt row. Whatever the handler threw, that
/// JSON must stay within the stored limit and stay readable, with a marker where text was cut.
/// </summary>
public sealed class JobExceptionTextTests : TestBase
{
    [Fact]
    public void a_huge_message_is_stored_as_bounded_valid_json_that_says_it_was_cut()
    {
        var exception = new InvalidOperationException(new string('m', 1024 * 1024));

        var stored = ExceptionDetailClassForSerialization.Serialize(exception);

        stored.Length.Should().BeLessThanOrEqualTo(JobExceptionText.StoredMaxLength);
        var detail = _Read(stored);
        detail.Type.Should().Be(typeof(InvalidOperationException).FullName);
        detail.Message.Should().EndWith(JobExceptionText.TruncationMarker);
        detail.Message.Should().StartWith("mmmm");
    }

    [Fact]
    public void text_that_escaping_lengthens_still_fits_the_stored_limit()
    {
        // A control character serializes as six characters, so the fields alone fitting is not enough.
        var exception = new InvalidOperationException(new string('\u0001', 64 * 1024));

        var stored = ExceptionDetailClassForSerialization.Serialize(exception);

        stored.Length.Should().BeLessThanOrEqualTo(JobExceptionText.StoredMaxLength);
        var message = _Read(stored).Message;
        message.Should().EndWith(JobExceptionText.TruncationMarker);
        message.Length.Should().BeGreaterThan(500, "only the escaping overshoot is cut, not the whole message");
    }

    [Fact]
    public void a_long_inner_chain_is_cut_from_the_stack_and_keeps_the_message()
    {
        // The inner chain is already capped at five exceptions, so each carries enough text to overrun the limit.
        var inner = Enumerable
            .Range(0, 5)
            .Select(i => new InvalidOperationException($"inner {i} " + new string('i', 2000)))
            .ToArray();
        var exception = new AggregateException("outer message", inner);

        var stored = ExceptionDetailClassForSerialization.Serialize(exception);

        stored.Length.Should().BeLessThanOrEqualTo(JobExceptionText.StoredMaxLength);
        var detail = _Read(stored);
        detail.Message.Should().StartWith("outer message");
        detail.StackTrace.Should().EndWith(JobExceptionText.TruncationMarker);
    }

    [Fact]
    public void a_short_exception_is_stored_whole()
    {
        var exception = new InvalidOperationException("boom");

        var detail = _Read(ExceptionDetailClassForSerialization.Serialize(exception));

        detail.Message.Should().Be("boom");
    }

    [Fact]
    public void a_plain_reason_is_bounded_for_the_store_and_for_a_tag()
    {
        var reason = new string('r', 10_000);

        JobExceptionText.ForStore(reason).Should().HaveLength(JobExceptionText.StoredMaxLength);
        JobExceptionText.ForTag(reason).Should().HaveLength(JobExceptionText.TagMaxLength);
        JobExceptionText.ForTag("short").Should().Be("short");
        JobExceptionText.ForTag(null).Should().BeNull();
    }

    private static ExceptionDetailClassForSerialization _Read(string stored) =>
        JsonSerializer.Deserialize(stored, JobsExceptionJsonContext.Default.ExceptionDetailClassForSerialization)!;
}
