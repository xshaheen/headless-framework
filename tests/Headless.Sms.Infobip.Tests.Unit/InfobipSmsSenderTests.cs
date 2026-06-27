// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;
using Headless.Sms.Infobip;
using Headless.Sms.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class InfobipSmsSenderTests : IClassFixture<SmsWireMockFixture>
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

    private InfobipSmsSender CreateSender(string? basePath = null)
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

        var result = await CreateSender().SendAsync(SmsRequests.Single());

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

        var result = await CreateSender().SendAsync(SmsRequests.Single());

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

        var response = await CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000"), (20, "1002220000")));

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

        var response = await CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000"), (20, "1002220000")));

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

        var response = await CreateSender()
            .SendBulkAsync(SmsRequests.Bulk("hi", (20, "1001110000"), (20, "1002220000")));

        response.AllSucceeded.Should().BeFalse();
        response.AnySucceeded.Should().BeFalse();
        response.ProviderBatchId.Should().Be("bulk-11");
        response.Results.Should().HaveCount(2);
        response.Results.Should().AllSatisfy(r => r.Result.FailureKind.Should().Be(SmsFailureKind.Unknown));
    }
}
