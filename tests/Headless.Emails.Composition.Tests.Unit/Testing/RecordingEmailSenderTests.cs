// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Headless.Emails.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Testing;

public sealed class RecordingEmailSenderTests : TestBase
{
    private readonly RecordingEmailSender _sut = new();

    [Fact]
    public async Task should_record_each_sent_request_in_order_and_succeed()
    {
        // given
        var first = _CreateRequest("first@example.com");
        var second = _CreateRequest("second@example.com");

        // when
        var firstResponse = await _sut.SendAsync(first, AbortToken);
        var secondResponse = await _sut.SendAsync(second, AbortToken);

        // then
        firstResponse.Success.Should().BeTrue();
        firstResponse.ProviderMessageId.Should().BeNull();
        secondResponse.Success.Should().BeTrue();
        _sut.Sent.Should().Equal(first, second);
    }

    [Fact]
    public async Task should_return_a_copy_that_later_sends_do_not_change()
    {
        // given
        await _sut.SendAsync(_CreateRequest("first@example.com"), AbortToken);
        var before = _sut.Sent;

        // when
        await _sut.SendAsync(_CreateRequest("second@example.com"), AbortToken);

        // then
        before.Should().ContainSingle();
        _sut.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_discard_recorded_requests_on_clear()
    {
        // given
        await _sut.SendAsync(_CreateRequest("first@example.com"), AbortToken);

        // when
        _sut.Clear();
        var next = _CreateRequest("next@example.com");
        await _sut.SendAsync(next, AbortToken);

        // then
        _sut.Sent.Should().Equal(next);
    }

    [Fact]
    public async Task should_record_every_concurrent_send_once()
    {
        // given
        const int count = 200;
        var requests = Enumerable.Range(0, count).Select(i => _CreateRequest($"user{i}@example.com")).ToList();

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
    public async Task should_reject_a_request_without_a_body_and_not_record_it()
    {
        // given
        var request = _CreateRequest("user@example.com") with
        {
            MessageText = null,
            MessageHtml = null,
        };

        // when
        var act = async () => await _sut.SendAsync(request, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        _sut.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_on_a_canceled_token_and_not_record_the_request()
    {
        // given
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await _sut.SendAsync(_CreateRequest("user@example.com"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _sut.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_replace_the_registered_default_sender_and_leave_named_senders_alone()
    {
        // given - the application's registration, as a test host sees it.
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", i => i.UseNoop());
        });

        // when - the replacement the guide shows.
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(_sut);
        await using var provider = services.BuildServiceProvider();
        var request = _CreateRequest("user@example.com");
        await provider.GetRequiredService<IEmailSender>().SendAsync(request, AbortToken);

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeSameAs(_sut);
        provider.GetRequiredKeyedService<IEmailSender>("audit").Should().BeOfType<NoopEmailSender>();
        _sut.Sent.Should().Equal(request);
    }

    [Fact]
    public async Task should_replace_a_named_sender_under_its_key()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", i => i.UseNoop());
        });

        // when
        services.RemoveAllKeyed<IEmailSender>("audit");
        services.AddKeyedSingleton<IEmailSender>("audit", _sut);
        await using var provider = services.BuildServiceProvider();
        var request = _CreateRequest("user@example.com");
        await provider.GetRequiredService<IEmailSenderProvider>().GetSender("audit").SendAsync(request, AbortToken);

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
        _sut.Sent.Should().Equal(request);
    }

    private static SendSingleEmailRequest _CreateRequest(string to) =>
        new()
        {
            From = "sender@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [to] },
            Subject = "Welcome",
            MessageText = "Hello",
        };
}
