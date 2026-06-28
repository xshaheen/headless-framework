// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Dev;
using Headless.Sms.Testing;

namespace Tests;

public sealed class DevSmsSenderTests
{
    [Fact]
    public async Task should_append_the_message_to_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sms-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevSmsSender(path);

        try
        {
            var result = await sender.SendAsync(SmsRequests.Single(text: "hello dev", messageId: "id-1"));

            result.Success.Should().BeTrue();
            var contents = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            contents.Should().Contain("hello dev").And.Contain("id-1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_append_a_bulk_message_for_every_recipient()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sms-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevSmsSender(path);

        try
        {
            var response = await sender.SendBulkAsync(SmsRequests.Bulk("bulk dev", (20, "1001"), (20, "1002")));

            response.AllSucceeded.Should().BeTrue();
            response.Results.Should().HaveCount(2);
            var contents = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            contents.Should().Contain("bulk dev").And.Contain("201001").And.Contain("201002");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class NoopSmsSenderTests
{
    [Fact]
    public async Task should_report_success()
    {
        var result = await new NoopSmsSender().SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_a_null_single_request()
    {
        var act = async () => await new NoopSmsSender().SendAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_reject_a_single_request_without_destination()
    {
        var request = new SendSingleSmsRequest { Destination = null!, Text = "Hello world" };

        var act = async () => await new NoopSmsSender().SendAsync(request);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_reject_a_single_request_with_an_empty_body()
    {
        var act = async () => await new NoopSmsSender().SendAsync(SmsRequests.Single(text: ""));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_report_bulk_success()
    {
        var response = await new NoopSmsSender().SendBulkAsync(SmsRequests.Bulk("Hello world", (20, "1001")));

        response.AllSucceeded.Should().BeTrue();
        response.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task should_reject_a_bulk_request_with_an_empty_body()
    {
        var request = SmsRequests.Bulk("", (20, "1001"));

        var act = async () => await new NoopSmsSender().SendBulkAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_reject_a_bulk_request_without_destinations()
    {
        var request = new SendBulkSmsRequest { Destinations = null!, Text = "Hello world" };

        var act = async () => await new NoopSmsSender().SendBulkAsync(request);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_honor_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await new NoopSmsSender().SendAsync(SmsRequests.Single(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_honor_bulk_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
            await new NoopSmsSender().SendBulkAsync(SmsRequests.Bulk("Hello world", (20, "1001")), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
