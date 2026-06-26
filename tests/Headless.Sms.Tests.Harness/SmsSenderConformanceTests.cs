// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.Testing;

/// <summary>
/// Cross-provider conformance contract that every <see cref="ISmsSender"/> implementation must satisfy.
/// </summary>
/// <remarks>
/// Concrete provider test classes derive from this, wire the provider through <see cref="CreateSuccessfulSender"/>
/// and <see cref="CreateFaultingSender"/>, and re-expose each scenario as an xUnit <c>[Fact]</c> override
/// (<c>[Fact] public override Task should_…() =&gt; base.should_…();</c>). Backend-specific behavior — provider
/// message ids, batch routing, auth headers, response-code mapping, token caching — lives in sibling test
/// classes; only the portable contract belongs here. Adding a new provider means implementing the two seams.
/// </remarks>
public abstract class SmsSenderConformanceTests
{
    /// <summary>
    /// Wires the provider so that a single-destination <see cref="SmsRequests.Single"/> send succeeds.
    /// </summary>
    protected abstract ISmsSender CreateSuccessfulSender();

    /// <summary>
    /// Wires the provider so that the send faults at the transport layer (connection failure or SDK throw),
    /// which the contract requires to surface as <see cref="SmsFailureKind.Transient"/>.
    /// </summary>
    protected abstract ISmsSender CreateFaultingSender();

    public virtual async Task should_reject_a_null_request()
    {
        var sender = CreateSuccessfulSender();

        var act = async () => await sender.SendAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public virtual async Task should_reject_a_request_without_destinations()
    {
        var sender = CreateSuccessfulSender();
        var request = new SendSingleSmsRequest { Destinations = [], Text = "Hello world" };

        var act = async () => await sender.SendAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_reject_a_request_with_an_empty_body()
    {
        var sender = CreateSuccessfulSender();
        var request = new SendSingleSmsRequest
        {
            Destinations = [new SmsRequestDestination(20, "1001234567")],
            Text = "",
        };

        var act = async () => await sender.SendAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_succeed_for_a_single_destination()
    {
        var sender = CreateSuccessfulSender();

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
        result.FailureError.Should().BeNull();
        result.FailureKind.Should().Be(SmsFailureKind.None);
    }

    public virtual async Task should_report_a_transient_failure_on_a_transport_fault()
    {
        var sender = CreateFaultingSender();

        var result = await sender.SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
        result.FailureError.Should().NotBeNullOrEmpty();
        result.FailureKind.Should().Be(SmsFailureKind.Transient);
    }

    public virtual async Task should_propagate_cancellation()
    {
        var sender = CreateSuccessfulSender();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sender.SendAsync(SmsRequests.Single(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
