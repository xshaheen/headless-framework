// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Infobip;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class InfobipSmsSenderTests : TestBase, IClassFixture<SmsWireMockFixture>
{
    private const string _SuccessBody = """
        {
          "bulkId": "bulk-1",
          "messages": [
            {
              "to": "201001234567",
              "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "Message sent to next instance" },
              "messageId": "m1"
            }
          ]
        }
        """;

    private readonly SmsWireMockFixture _fixture;

    public InfobipSmsSenderTests(SmsWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private InfobipSmsSender _CreateSender(string? basePath = null)
    {
        var options = Options.Create(
            new InfobipSmsOptions
            {
                Sender = "SENDER",
                ApiKey = "api-key",
                BasePath = basePath ?? _fixture.BaseUrl,
            }
        );

        return new InfobipSmsSender(_fixture.HttpClientFactory, options, NullLogger<InfobipSmsSender>.Instance);
    }

    [Fact]
    public async Task should_succeed_and_carry_the_bulk_id()
    {
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(_SuccessBody)
            );

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("bulk-1");
    }

    [Fact]
    public async Task should_fail_when_the_api_rejects_the_request()
    {
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.BadRequest)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"requestError":{"serviceException":{"messageId":"BAD_REQUEST","text":"invalid"}}}""")
            );

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_carry_per_recipient_message_ids_for_a_bulk_send()
    {
        const string body = """
            {
              "bulkId": "bulk-9",
              "messages": [
                { "to": "201001110000", "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "q" }, "messageId": "m-a" },
                { "to": "201002220000", "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "q" }, "messageId": "m-b" }
              ]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var response = await _CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000"), (20, "1002220000")), AbortToken);

        response.AllSucceeded.Should().BeTrue();
        response.ProviderBatchId.Should().Be("bulk-9");
        response.Results.Should().HaveCount(2);
        response.Results[0].Result.ProviderMessageId.Should().Be("m-a");
        response.Results[1].Result.ProviderMessageId.Should().Be("m-b");
    }

    [Fact]
    public async Task should_preserve_per_recipient_failure_status_for_a_bulk_send()
    {
        const string body = """
            {
              "bulkId": "bulk-10",
              "messages": [
                { "to": "201001110000", "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "queued" }, "messageId": "m-a" },
                { "to": "201002220000", "status": { "groupId": 5, "groupName": "REJECTED", "id": 99, "name": "REJECTED_DESTINATION", "description": "Invalid destination" }, "messageId": "m-b" }
              ]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var response = await _CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000"), (20, "1002220000")), AbortToken);

        response.AllSucceeded.Should().BeFalse();
        response.AnySucceeded.Should().BeTrue();
        response.ProviderBatchId.Should().Be("bulk-10");
        response.Results[0].Result.Success.Should().BeTrue();
        response.Results[0].Result.ProviderMessageId.Should().Be("m-a");
        response.Results[1].Result.Success.Should().BeFalse();
        response.Results[1].Result.FailureError.Should().Be("Invalid destination");
        response.Results[1].Result.FailureKind.Should().Be(SmsFailureKind.InvalidRecipient);
    }

    [Fact]
    public async Task should_not_report_success_when_the_bulk_response_count_does_not_match()
    {
        // Two recipients requested, but Infobip returns only one message result: the response cannot be
        // attributed per recipient, so the send must not be reported as all-succeeded.
        const string body = """
            {
              "bulkId": "bulk-11",
              "messages": [
                { "to": "201001110000", "status": { "groupId": 1, "groupName": "PENDING", "id": 7, "name": "PENDING_ENROUTE", "description": "q" }, "messageId": "m-a" }
              ]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var response = await _CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000"), (20, "1002220000")), AbortToken);

        response.AllSucceeded.Should().BeFalse();
        response.AnySucceeded.Should().BeFalse();
        response.ProviderBatchId.Should().Be("bulk-11");
        response.Results.Should().HaveCount(2);
        response.Results.Should().AllSatisfy(r => r.Result.FailureKind.Should().Be(SmsFailureKind.Unknown));
    }

    [Fact]
    public async Task should_classify_a_rejected_recipient_by_group_not_by_status_name()
    {
        // Deliberate: classify by Infobip's typed delivery group, not by parsing the free-form status name, so
        // even a "REJECTED_NOT_ENOUGH_CREDITS" rejection maps to InvalidRecipient (REJECTED group). The specific
        // reason is preserved in FailureError.
        const string body = """
            {
              "bulkId": "bulk-12",
              "messages": [
                { "to": "201001110000", "status": { "groupId": 5, "groupName": "REJECTED", "id": 6, "name": "REJECTED_NOT_ENOUGH_CREDITS", "description": "Not enough credits" }, "messageId": "m-a" }
              ]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var response = await _CreateSender().SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000")), AbortToken);

        response.AllSucceeded.Should().BeFalse();
        response.Results[0].Result.FailureKind.Should().Be(SmsFailureKind.InvalidRecipient);
        response.Results[0].Result.FailureError.Should().Be("Not enough credits");
    }

    [Fact]
    public async Task should_not_infer_a_failure_kind_from_the_http_status()
    {
        // Deliberate: Infobip's HTTP status is not a reliable per-provider kind signal (its real error lives in
        // the response body), so a request-level rejection is reported without a guessed kind (Unknown) rather
        // than run through the generic HTTP-status mapping.
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.Unauthorized)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """{"requestError":{"serviceException":{"messageId":"UNAUTHORIZED","text":"invalid api key"}}}"""
                    )
            );

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public async Task should_fail_a_single_send_when_the_message_is_rejected_in_a_200()
    {
        // Infobip can accept the request (HTTP 200) but reject the individual message; a single send must
        // surface that as a failure rather than reporting success.
        const string body = """
            {
              "bulkId": "bulk-13",
              "messages": [
                { "to": "201001234567", "status": { "groupId": 5, "groupName": "REJECTED", "id": 99, "name": "REJECTED_DESTINATION", "description": "Invalid destination" }, "messageId": "m1" }
              ]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var result = await _CreateSender().SendAsync(SmsRequests.Single(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("Invalid destination");
        result.FailureKind.Should().Be(SmsFailureKind.InvalidRecipient);
    }

    [Fact]
    public async Task should_classify_a_missing_status_as_unknown_for_a_bulk_recipient()
    {
        const string body = """
            {
              "bulkId": "bulk-14",
              "messages": [{ "to": "201001110000", "messageId": "m-a" }]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var response = await _CreateSender().SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000")), AbortToken);

        response.Results[0].Result.Success.Should().BeFalse();
        response.Results[0].Result.FailureKind.Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public async Task should_use_the_status_name_when_the_failure_description_is_blank()
    {
        const string body = """
            {
              "bulkId": "bulk-15",
              "messages": [
                { "to": "201001110000", "status": { "groupId": 5, "groupName": "REJECTED", "id": 99, "name": "REJECTED_DESTINATION", "description": "" }, "messageId": "m-a" }
              ]
            }
            """;
        _fixture
            .Server.Given(Request.Create().UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );

        var response = await _CreateSender().SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000")), AbortToken);

        response.Results[0].Result.Success.Should().BeFalse();
        response.Results[0].Result.FailureError.Should().Be("Infobip message status REJECTED_DESTINATION");
    }
}
