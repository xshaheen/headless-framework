// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;
using Headless.Sms;
using Headless.Sms.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

public sealed class DevSmsSenderCaptureTests : TestBase
{
    private readonly SmsCaptureStore _store = new(500, new FakeTimeProvider());

    [Fact]
    public async Task should_capture_a_single_message_without_writing_a_file_when_no_path_is_set()
    {
        // given
        using var sender = new DevSmsSender(new DevSmsOptions(), _store, "otp");
        var request = new SendSingleSmsRequest
        {
            MessageId = "id-1",
            Destination = new SmsRequestDestination(20, "1001234567"),
            Text = "Your code is 4821",
            Properties = new Dictionary<string, object>(StringComparer.Ordinal) { ["template"] = "otp" },
        };

        // when
        var response = await sender.SendAsync(request, AbortToken);

        // then
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        response.Success.Should().BeTrue();
        response.ProviderMessageId.Should().Be(captured.Id.ToString());
        captured.SenderName.Should().Be("otp");
        captured.Destinations.Should().Equal(new SmsRequestDestination(20, "1001234567"));
        captured.IsBulk.Should().BeFalse();
        captured.Text.Should().Be("Your code is 4821");
        captured.MessageId.Should().Be("id-1");
        captured.Properties.Should().ContainKey("template").WhoseValue.Should().Be("otp");
        captured.Segmentation.Should().Be(new SmsSegmentation(SmsEncoding.Gsm7, 17, 1));
        captured.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_capture_a_bulk_send_once_with_every_recipient()
    {
        // given
        using var sender = new DevSmsSender(new DevSmsOptions(), _store);
        var request = new SendBulkSmsRequest
        {
            Destinations =
            [
                new SmsRequestDestination(20, "1"),
                new SmsRequestDestination(20, "2"),
                new SmsRequestDestination(1, "3"),
            ],
            Text = "مرحبا",
        };

        // when
        var response = await sender.SendBulkAsync(request, AbortToken);

        // then
        response.Results.Should().HaveCount(3);
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        captured.IsBulk.Should().BeTrue();
        captured.Destinations.Should().HaveCount(3);
        captured.Segmentation.Encoding.Should().Be(SmsEncoding.Ucs2);
    }

    [Fact]
    public async Task should_keep_the_capture_unchanged_when_the_caller_mutates_its_request()
    {
        // given
        using var sender = new DevSmsSender(new DevSmsOptions(), _store);
        var properties = new Dictionary<string, object>(StringComparer.Ordinal) { ["a"] = 1 };
        var destinations = new List<SmsRequestDestination> { new(20, "1") };

        // when
        await sender.SendBulkAsync(
            new SendBulkSmsRequest
            {
                Destinations = destinations,
                Text = "hi",
                Properties = properties,
            },
            AbortToken
        );
        properties["b"] = 2;
        destinations.Add(new SmsRequestDestination(20, "2"));

        // then
        var captured = _store.Query(new()).Single();
        captured.Properties.Should().ContainSingle();
        captured.Destinations.Should().ContainSingle();
    }

    [Fact]
    public async Task should_capture_a_file_write_failure_and_return_a_failed_response()
    {
        // given: a file inside a directory that does not exist cannot be appended to
        var path = Path.Combine(Path.GetTempPath(), $"sms-dev-{Guid.NewGuid():N}", "out.txt");
        using var sender = new DevSmsSender(new DevSmsOptions { FilePath = path }, _store);

        // when
        var response = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        // then
        response.Success.Should().BeFalse();
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        captured.Succeeded.Should().BeFalse();
        captured.FailureError.Should().Be(response.FailureError);
    }

    [Fact]
    public async Task should_record_the_current_trace_id_and_correlation_id()
    {
        // given
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.CorrelationId.Returns("corr-1");
        using var sender = new DevSmsSender(new DevSmsOptions(), _store, correlationIdProvider: correlation);
        using var activity = new Activity("send");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();

        // when
        await sender.SendAsync(SmsRequests.Single(), AbortToken);

        // then
        var captured = _store.Query(new()).Single();
        captured.TraceId.Should().Be(activity.TraceId.ToString());
        captured.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task should_not_capture_a_request_rejected_by_argument_validation()
    {
        using var sender = new DevSmsSender(new DevSmsOptions(), _store);

        var act = async () => await sender.SendAsync(SmsRequests.Single(text: ""), AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.Count.Should().Be(0);
    }
}

public sealed class SmsSegmentationTests
{
    [Theory]
    [InlineData("", SmsEncoding.Gsm7, 0, 0)]
    [InlineData("Hello", SmsEncoding.Gsm7, 5, 1)]
    [InlineData("Price: 5€ [promo]", SmsEncoding.Gsm7, 20, 1)]
    [InlineData("مرحبا", SmsEncoding.Ucs2, 5, 1)]
    [InlineData("Hi 👋", SmsEncoding.Ucs2, 5, 1)]
    public void should_measure_encoding_length_and_parts(string text, SmsEncoding encoding, int length, int parts)
    {
        SmsSegmentation.Measure(text).Should().Be(new SmsSegmentation(encoding, length, parts));
    }

    [Theory]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void should_split_gsm_text_at_160_then_153_per_part(int length, int parts)
    {
        SmsSegmentation.Measure(new string('a', length)).SegmentCount.Should().Be(parts);
    }

    [Theory]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(135, 3)]
    public void should_split_ucs2_text_at_70_then_67_per_part(int length, int parts)
    {
        SmsSegmentation.Measure(new string('ج', length)).SegmentCount.Should().Be(parts);
    }

    [Fact]
    public void should_count_an_extension_character_as_two_septets()
    {
        // 80 euro signs are 160 septets: one part. One more character tips it into two.
        SmsSegmentation.Measure(new string('€', 80)).Should().Be(new SmsSegmentation(SmsEncoding.Gsm7, 160, 1));
        SmsSegmentation.Measure(new string('€', 80) + "a").SegmentCount.Should().Be(2);
    }

    [Fact]
    public void should_reject_null_text()
    {
        var act = () => SmsSegmentation.Measure(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
