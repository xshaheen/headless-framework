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
}
