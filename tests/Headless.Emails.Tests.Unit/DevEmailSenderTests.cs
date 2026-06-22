// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;

namespace Tests;

public sealed class DevEmailSenderTests
{
    [Fact]
    public async Task should_append_the_message_to_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var result = await sender.SendAsync(_Request(), TestContext.Current.CancellationToken);

            result.Success.Should().BeTrue();
            var contents = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            contents.Should().Contain("from@example.com").And.Contain("to@example.com").And.Contain("hello dev");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_serialize_concurrent_writes_without_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var sends = Enumerable
                .Range(0, 20)
                .Select(i =>
                    sender.SendAsync(_Request(text: $"body-{i}"), TestContext.Current.CancellationToken).AsTask()
                );
            await Task.WhenAll(sends);

            var contents = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            // 20 sends -> 20 separators, and every message body present exactly once (no interleaving).
            (contents.Split("--------------------").Length - 1)
                .Should()
                .Be(20);
            foreach (var i in Enumerable.Range(0, 20))
            {
                contents.Should().Contain($"body-{i}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task missing_body_should_throw()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        var request = _Request() with { MessageText = null, MessageHtml = null };

        var act = async () => await sender.SendAsync(request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void constructor_should_reject_empty_path()
    {
        var act = () => new DevEmailSender("");

        act.Should().Throw<ArgumentException>();
    }

    private static SendSingleEmailRequest _Request(string text = "hello dev") =>
        new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = text,
        };
}

public sealed class NoopEmailSenderTests
{
    [Fact]
    public async Task should_report_success()
    {
        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };

        var result = await new NoopEmailSender().SendAsync(request, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
    }
}
