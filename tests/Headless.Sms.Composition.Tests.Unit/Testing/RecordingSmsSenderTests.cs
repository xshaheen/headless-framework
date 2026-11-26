// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Testing;

public sealed class RecordingSmsSenderTests : TestBase
{
    private readonly RecordingSmsSender _sut = new();

    [Fact]
    public async Task should_record_each_sent_request_in_order_and_succeed()
    {
        // given
        var first = _CreateRequest("1000001");
        var second = _CreateRequest("1000002");

        // when
        var firstResponse = await _sut.SendAsync(first, AbortToken);
        var secondResponse = await _sut.SendAsync(second, AbortToken);

        // then
        firstResponse.Success.Should().BeTrue();
        firstResponse.ProviderMessageId.Should().BeNull();
        secondResponse.Success.Should().BeTrue();
        _sut.Sent.Should().Equal(first, second);
        _sut.SentBulk.Should().BeEmpty();
    }

    [Fact]
    public async Task should_record_a_bulk_request_once_and_succeed_for_every_destination()
    {
        // given
        var request = new SendBulkSmsRequest
        {
            Destinations = [new SmsRequestDestination(20, "1000001"), new SmsRequestDestination(20, "1000002")],
            Text = "Your code is 123456",
        };

        // when
        var response = await _sut.SendBulkAsync(request, AbortToken);

        // then
        response.AllSucceeded.Should().BeTrue();
        response.Results.Select(static r => r.Destination).Should().Equal(request.Destinations);
        _sut.SentBulk.Should().Equal(request);
        _sut.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_discard_single_and_bulk_requests_on_clear()
    {
        // given
        await _sut.SendAsync(_CreateRequest("1000001"), AbortToken);
        await _sut.SendBulkAsync(
            new SendBulkSmsRequest { Destinations = [new SmsRequestDestination(20, "1000002")], Text = "Hi" },
            AbortToken
        );

        // when
        _sut.Clear();
        var next = _CreateRequest("1000003");
        await _sut.SendAsync(next, AbortToken);

        // then
        _sut.Sent.Should().Equal(next);
        _sut.SentBulk.Should().BeEmpty();
    }

    [Fact]
    public async Task should_record_every_concurrent_send_once()
    {
        // given
        const int count = 200;
        var requests = Enumerable.Range(0, count).Select(i => _CreateRequest($"{1000000 + i}")).ToList();

        // when
        await Parallel.ForEachAsync(
            requests,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = AbortToken },
            async (request, ct) => await _sut.SendAsync(request, ct)
        );

        // then
        _sut.Sent.Should().HaveCount(count).And.BeEquivalentTo(requests);
    }

    [Fact]
    public async Task should_reject_empty_text_and_not_record_it()
    {
        // given
        var request = new SendSingleSmsRequest { Destination = new SmsRequestDestination(20, "1000001"), Text = "" };

        // when
        var act = async () => await _sut.SendAsync(request, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _sut.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_a_bulk_request_without_destinations_and_not_record_it()
    {
        // given
        var request = new SendBulkSmsRequest { Destinations = [], Text = "Hi" };

        // when
        var act = async () => await _sut.SendBulkAsync(request, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _sut.SentBulk.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_on_a_canceled_token_and_not_record_the_request()
    {
        // given
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await _sut.SendAsync(_CreateRequest("1000001"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _sut.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_replace_the_registered_default_sender_for_single_and_bulk_sends()
    {
        // given - the application's registration, as a test host sees it.
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup => setup.UseNoop());

        // when - the replacement the guide shows. The provider's IBulkSmsSender forwards to the default ISmsSender.
        services.RemoveAll<ISmsSender>();
        services.AddSingleton<ISmsSender>(_sut);
        await using var provider = services.BuildServiceProvider();
        var request = _CreateRequest("1000001");
        await provider.GetRequiredService<ISmsSender>().SendAsync(request, AbortToken);

        // then
        provider.GetRequiredService<ISmsSender>().Should().BeSameAs(_sut);
        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(_sut);
        _sut.Sent.Should().Equal(request);
    }

    private static SendSingleSmsRequest _CreateRequest(string number) =>
        new() { Destination = new SmsRequestDestination(20, number), Text = "Your code is 123456" };
}
