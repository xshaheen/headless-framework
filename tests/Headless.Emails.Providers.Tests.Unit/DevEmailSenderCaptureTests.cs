// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;
using Headless.Emails;
using Headless.Emails.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

public sealed class DevEmailSenderCaptureTests : TestBase
{
    private readonly EmailCaptureStore _store = new(
        500,
        new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 14, 20, 31, TimeSpan.Zero))
    );

    [Fact]
    public async Task should_capture_the_full_email_without_writing_a_file_when_no_path_is_set()
    {
        // given
        using var sender = new DevEmailSender(new DevEmailOptions(), _store, "audit");
        var request = _Request() with
        {
            From = new EmailRequestAddress("orders@example.com", "Orders"),
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress("lina@example.com", "Lina")],
                CcAddresses = [new EmailRequestAddress("cc@example.com")],
                BccAddresses = [new EmailRequestAddress("bcc@example.com")],
            },
            MessageHtml = "<p>html</p>",
        };

        // when
        var response = await sender.SendAsync(request, AbortToken);

        // then
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        response.Success.Should().BeTrue();
        response.ProviderMessageId.Should().Be(captured.Id.ToString());
        captured.SenderName.Should().Be("audit");
        captured.From.Should().Be(new EmailRequestAddress("orders@example.com", "Orders"));
        captured.To.Should().Equal(new EmailRequestAddress("lina@example.com", "Lina"));
        captured.Cc.Should().Equal(new EmailRequestAddress("cc@example.com"));
        captured.Bcc.Should().Equal(new EmailRequestAddress("bcc@example.com"));
        captured.Subject.Should().Be("subject");
        captured.TextBody.Should().Be("hello dev");
        captured.HtmlBody.Should().Be("<p>html</p>");
        captured.Succeeded.Should().BeTrue();
        captured.FailureError.Should().BeNull();
        captured.CapturedAt.Should().Be(new DateTimeOffset(2026, 11, 4, 14, 20, 31, TimeSpan.Zero));
    }

    [Fact]
    public async Task should_keep_the_capture_unchanged_when_the_caller_mutates_its_request()
    {
        // given
        using var sender = new DevEmailSender(new DevEmailOptions { MaxCapturedAttachmentBytes = 16 }, _store);
        var to = new List<EmailRequestAddress> { new("to@example.com") };
        var bytes = "abc"u8.ToArray();
        var request = _Request() with
        {
            Destination = new EmailRequestDestination { ToAddresses = to },
            Attachments = [new EmailRequestAttachment { Name = "a.txt", File = bytes }],
        };

        // when
        await sender.SendAsync(request, AbortToken);
        to.Add(new EmailRequestAddress("late@example.com"));
        bytes[0] = (byte)'z';

        // then
        var captured = _store.Query(new()).Single();
        captured.To.Should().ContainSingle();
        captured.Attachments.Single().Content!.Value.ToArray().Should().Equal("abc"u8.ToArray());
    }

    [Fact]
    public async Task should_keep_attachment_metadata_and_only_the_content_under_the_limit()
    {
        // given
        using var sender = new DevEmailSender(new DevEmailOptions { MaxCapturedAttachmentBytes = 4 }, _store);
        var request = _Request() with
        {
            Attachments =
            [
                new EmailRequestAttachment
                {
                    Name = "small.txt",
                    File = "abcd"u8.ToArray(),
                    ContentType = "text/plain",
                },
                new EmailRequestAttachment
                {
                    Name = "large.pdf",
                    File = "abcde"u8.ToArray(),
                    ContentType = "application/pdf",
                },
            ],
        };

        // when
        await sender.SendAsync(request, AbortToken);

        // then
        var attachments = _store.Query(new()).Single().Attachments;
        attachments[0]
            .Should()
            .BeEquivalentTo(
                new
                {
                    FileName = "small.txt",
                    ContentType = "text/plain",
                    SizeBytes = 4L,
                }
            );
        attachments[0].Content.HasValue.Should().BeTrue();
        attachments[1]
            .Should()
            .BeEquivalentTo(
                new
                {
                    FileName = "large.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 5L,
                }
            );
        attachments[1].Content.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_no_attachment_content_by_default()
    {
        using var sender = new DevEmailSender(new DevEmailOptions(), _store);
        var request = _Request() with
        {
            Attachments = [new EmailRequestAttachment { Name = "a.bin", File = "x"u8.ToArray() }],
        };

        await sender.SendAsync(request, AbortToken);

        _store.Query(new()).Single().Attachments.Single().Content.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_capture_a_body_less_email_as_failed_and_still_throw()
    {
        // given
        using var sender = new DevEmailSender(new DevEmailOptions(), _store);
        var request = _Request() with { MessageText = null, MessageHtml = null };

        // when
        var act = async () => await sender.SendAsync(request, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        captured.Succeeded.Should().BeFalse();
        captured.FailureError.Should().Contain("MessageHtml or MessageText");
    }

    [Fact]
    public async Task should_capture_a_file_write_failure_and_still_throw()
    {
        // given: a file inside a directory that does not exist cannot be appended to
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}", "out.txt");
        using var sender = new DevEmailSender(new DevEmailOptions { FilePath = path }, _store);

        // when
        var act = async () => await sender.SendAsync(_Request(), AbortToken);

        // then
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        _store.Query(new()).Should().ContainSingle().Which.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_record_the_current_trace_id_and_correlation_id()
    {
        // given
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.CorrelationId.Returns("corr-1");
        using var sender = new DevEmailSender(new DevEmailOptions(), _store, correlationIdProvider: correlation);
        using var activity = new Activity("send");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();

        // when
        await sender.SendAsync(_Request(), AbortToken);

        // then
        var captured = _store.Query(new()).Single();
        captured.TraceId.Should().Be(activity.TraceId.ToString());
        captured.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task should_record_no_trace_id_without_a_current_activity()
    {
        using var sender = new DevEmailSender(new DevEmailOptions(), _store);
        var previous = Activity.Current;
        Activity.Current = null;

        try
        {
            await sender.SendAsync(_Request(), AbortToken);
        }
        finally
        {
            Activity.Current = previous;
        }

        _store.Query(new()).Single().TraceId.Should().BeNull();
    }

    private static SendSingleEmailRequest _Request()
    {
        return new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "hello dev",
        };
    }
}
