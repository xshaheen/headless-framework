// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Testing;

/// <summary>
/// Cross-provider conformance contract that every <see cref="IBulkSmsSender"/> implementation must satisfy.
/// </summary>
/// <remarks>
/// Mirrors <see cref="SmsSenderConformanceTests"/> for the bulk capability. Concrete provider test classes
/// derive from this, wire the provider through <see cref="CreateSuccessfulSender"/> and
/// <see cref="CreateFaultingSender"/>, and re-expose each scenario as an xUnit <c>[Fact]</c> override.
/// </remarks>
public abstract class SmsBulkSenderConformanceTests
{
    /// <summary>Wires the provider so that a multi-recipient bulk send succeeds.</summary>
    protected abstract IBulkSmsSender CreateSuccessfulSender();

    /// <summary>Wires the provider so that the bulk send faults at the transport layer.</summary>
    protected abstract IBulkSmsSender CreateFaultingSender();

    public virtual async Task should_reject_a_null_request()
    {
        var sender = CreateSuccessfulSender();

        var act = async () => await sender.SendBulkAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public virtual async Task should_reject_a_request_without_destinations()
    {
        var sender = CreateSuccessfulSender();
        var request = new SendBulkSmsRequest { Destinations = [], Text = "Hello world" };

        var act = async () => await sender.SendBulkAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_reject_a_request_with_an_empty_body()
    {
        var sender = CreateSuccessfulSender();
        var request = new SendBulkSmsRequest
        {
            Destinations = [new SmsRequestDestination(20, "1001234567")],
            Text = "",
        };

        var act = async () => await sender.SendBulkAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_return_a_result_for_every_recipient()
    {
        var sender = CreateSuccessfulSender();
        var request = SmsRequests.Bulk("Hello world", (20, "1001234567"), (20, "1009876543"));

        var response = await sender.SendBulkAsync(request);

        response.AllSucceeded.Should().BeTrue();
        response.Results.Should().HaveCount(2);
        response.Results.Select(r => r.Destination).Should().Equal(request.Destinations);
    }

    public virtual async Task should_report_a_transient_failure_on_a_transport_fault()
    {
        var sender = CreateFaultingSender();
        var request = SmsRequests.Bulk("Hello world", (20, "1001234567"), (20, "1009876543"));

        var response = await sender.SendBulkAsync(request);

        response.AllSucceeded.Should().BeFalse();
        response.Results.Should().HaveCount(2);
        response.Results.Should().AllSatisfy(r => r.Result.FailureKind.Should().Be(SmsFailureKind.Transient));
    }

    public virtual async Task should_propagate_cancellation()
    {
        var sender = CreateSuccessfulSender();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var request = SmsRequests.Bulk("Hello world", (20, "1001234567"));

        var act = async () => await sender.SendBulkAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
