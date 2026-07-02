// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms.Dev;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DevSmsSenderTests : TestBase
{
    [Fact]
    public async Task should_append_the_message_to_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sms-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevSmsSender(path);

        try
        {
            var result = await sender.SendAsync(SmsRequests.Single(text: "hello dev", messageId: "id-1"), AbortToken);
            result.Success.Should().BeTrue();
            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("hello dev").And.Contain("id-1");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class NoopSmsSenderTests : TestBase
{
    [Fact]
    public async Task should_report_success()
    {
        var result = await new NoopSmsSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task should_honor_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await new NoopSmsSender().SendAsync(SmsRequests.Single(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
