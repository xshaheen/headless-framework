// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Headless.Dashboard.Authentication;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class ReceivedMessageEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task should_return_message_content_when_received_message_details()
    {
        // given
        var messageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x14, 0x56);
        var message = new MediumMessage
        {
            StorageId = messageId,
            Content = "{\"received\":\"data\"}",
            Lane = MessageLane.Bus,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "logical-rec-456",
                    [Headers.MessageName] = "orders.received",
                    [Headers.Group] = "workers",
                },
                new { Data = "received" }
            ),
        };

        _monitoringApi
            .GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(message));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/received/message/{messageId}", AbortToken);

        // then
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(
            cancellationToken: AbortToken
        );
        payload.Should().ContainKey("storageId");
        payload.Should().ContainKey("messageId");
        payload.Should().ContainKey("lane");
        payload["lane"].GetString().Should().Be(nameof(MessageLane.Bus));
        payload["requestedDeliveryMode"].ValueKind.Should().Be(JsonValueKind.Null);
        payload["resolvedDeliveryMode"].GetString().Should().Be(nameof(DeliveryMode.Durable));
    }

    [Fact]
    public async Task should_return_404_for_missing_message_when_received_message_details()
    {
        // given
        var messageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x18, 0x88);
        _monitoringApi
            .GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(null));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/received/message/{messageId}", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_bind_lane_filter_and_project_delivery_metadata_with_pagination_when_received_list()
    {
        // given
        var result = new IndexPage<MessageView>(
            [
                new MessageView
                {
                    StorageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x14, 0x56),
                    MessageId = "logical-rec-456",
                    Version = "v1",
                    Name = "orders.received",
                    Group = "workers",
                    Lane = MessageLane.Queue,
                    RequestedDeliveryMode = DeliveryMode.Direct,
                    ResolvedDeliveryMode = DeliveryMode.Direct,
                    Content = "{\"received\":\"data\"}",
                    Added = new DateTimeOffset(2026, 03, 24, 11, 00, 00, TimeSpan.Zero),
                    Retries = 1,
                    StatusName = StatusName.Failed,
                },
            ],
            index: 0,
            size: 10,
            totalItems: 1
        );

        _monitoringApi
            .GetMessagesAsync(Arg.Any<MessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync(
            "/api/received/Failed?currentPage=1&perPage=10&group=workers&lane=Queue",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(
            cancellationToken: AbortToken
        );
        payload
            .Should()
            .ContainKeys("items", "index", "size", "totalItems", "totalPages", "hasPrevious", "hasNext", "totals");
        payload["index"].GetInt32().Should().Be(0);
        payload["size"].GetInt32().Should().Be(10);
        payload["totalItems"].GetInt32().Should().Be(1);
        payload["totals"].GetInt32().Should().Be(1);

        var item = payload["items"].EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("storageId").GetString().Should().Be("11111111-1111-1111-1111-111111111456");
        item.GetProperty("messageId").GetString().Should().Be("logical-rec-456");
        item.GetProperty("group").GetString().Should().Be("workers");
        item.GetProperty("lane").GetString().Should().Be(nameof(MessageLane.Queue));
        item.GetProperty("requestedDeliveryMode").GetString().Should().Be(nameof(DeliveryMode.Direct));
        item.GetProperty("resolvedDeliveryMode").GetString().Should().Be(nameof(DeliveryMode.Direct));

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Subscribe
                    && query.StatusName == StatusName.Failed
                    && query.Group == "workers"
                    && query.Lane == MessageLane.Queue
                    && query.CurrentPage == 0
                    && query.PageSize == 10
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bind_null_intent_filter_when_received_list_intent_type_is_omitted()
    {
        // given
        var result = new IndexPage<MessageView>([], index: 0, size: 10, totalItems: 0);

        _monitoringApi
            .GetMessagesAsync(Arg.Any<MessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when — lane omitted from query string
        var response = await client.GetAsync("/api/received/Failed?currentPage=1&perPage=10&group=workers", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Subscribe
                    && query.StatusName == StatusName.Failed
                    && query.Group == "workers"
                    && query.Lane == null
                    && query.CurrentPage == 0
                    && query.PageSize == 10
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_422_for_empty_array_when_received_requeue()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/received/reexecute", Array.Empty<long>(), AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task should_return_422_for_null_body_when_received_delete()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        using var stringContent = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/received/delete", stringContent, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(StatusName.Failed, false, InboxOperationOutcome.Applied, HttpStatusCode.OK)]
    [InlineData(StatusName.Succeeded, false, InboxOperationOutcome.Applied, HttpStatusCode.OK)]
    [InlineData(StatusName.Failed, true, InboxOperationOutcome.Applied, HttpStatusCode.OK)]
    [InlineData(StatusName.Succeeded, true, InboxOperationOutcome.Applied, HttpStatusCode.OK)]
    [InlineData(StatusName.Failed, true, InboxOperationOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(StatusName.Succeeded, true, InboxOperationOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(StatusName.Failed, true, InboxOperationOutcome.StateConflict, HttpStatusCode.Conflict)]
    [InlineData(StatusName.Succeeded, true, InboxOperationOutcome.StateConflict, HttpStatusCode.Conflict)]
    public async Task should_round_trip_authorized_inbox_json_independently_of_host_options(
        StatusName status,
        bool customizeHostJson,
        InboxOperationOutcome outcome,
        HttpStatusCode expectedHttpStatus
    )
    {
        var operations = Substitute.For<IInboxOperationsApi>();
        var incarnationId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        operations
            .QueryAsync(
                Arg.Any<InboxGenerationQuery>(),
                Arg.Any<InboxAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                ValueTask.FromResult(
                    new IndexPage<InboxGenerationView>(
                        [
                            new InboxGenerationView(
                                Guid.NewGuid(),
                                incarnationId,
                                3,
                                "tenant-7",
                                "message-1",
                                MessageLane.Queue,
                                "orders.created",
                                "v1",
                                "orders.consumer",
                                status,
                                true,
                                true,
                                Guid.NewGuid(),
                                Guid.NewGuid(),
                                DateTimeOffset.UtcNow,
                                DateTimeOffset.UtcNow.AddDays(30),
                                true,
                                DateTimeOffset.UtcNow,
                                "dashboard-operator",
                                "investigation"
                            ),
                        ],
                        index: 0,
                        size: 20,
                        totalItems: 1
                    )
                )
            );
        operations
            .HoldAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                ValueTask.FromResult(
                    new InboxOperationResult(
                        operationId,
                        InboxOperationType.Hold,
                        outcome,
                        incarnationId,
                        status,
                        null,
                        null,
                        null,
                        null,
                        "api-user",
                        "investigation",
                        DateTimeOffset.UtcNow
                    )
                )
            );
        _dataStorage.GetInboxOperationsApi().Returns(operations);
        await using var app = _CreateTestApp(
            _dataStorage,
            authenticate: false,
            config: new MessagingDashboardOptionsBuilder().WithApiKey("secret"),
            useAuthenticationMiddleware: true,
            customizeHostJson: customizeHostJson
        );
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        var response = await client.GetAsync("/api/inbox?currentPage=1&perPage=20", AbortToken);

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var json = document.RootElement.ToString();
        json.Should()
            .Contain("tenant-7")
            .And.Contain("message-1")
            .And.Contain("orders.consumer")
            .And.Contain(incarnationId.ToString());
        json.Should().NotContain("content").And.NotContain("headers").And.NotContain("payload");
        var generation = document.RootElement.GetProperty("items")[0];
        generation.GetProperty("status").GetString().Should().Be(status.ToString());
        generation.GetProperty("lane").GetString().Should().Be(nameof(MessageLane.Queue));

        // Send the values returned to the browser unchanged through the authenticated mutation boundary.
        using var mutation = await client.PostAsJsonAsync(
            "/api/inbox/hold",
            new
            {
                operationId,
                expectedIncarnationId = generation.GetProperty("incarnationId").GetString(),
                expectedStatus = generation.GetProperty("status").GetString(),
                reason = "investigation",
            },
            AbortToken
        );

        mutation.StatusCode.Should().Be(expectedHttpStatus);
        using var mutationDocument = JsonDocument.Parse(await mutation.Content.ReadAsStringAsync(AbortToken));
        var mutationResult = mutationDocument.RootElement;
        mutationResult.GetProperty("expectedStatus").GetString().Should().Be(status.ToString());
        mutationResult.GetProperty("operationType").GetString().Should().Be(nameof(InboxOperationType.Hold));
        mutationResult.GetProperty("outcome").GetString().Should().Be(outcome.ToString());
        await operations
            .Received(1)
            .HoldAsync(
                Arg.Is<InboxOperationRequest>(request =>
                    request.OperationId == operationId
                    && request.ExpectedIncarnationId == incarnationId
                    && request.ExpectedStatus == status
                    && request.Actor == "api-user"
                    && request.Reason == "investigation"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_route_received_delete_through_audited_inbox_operations()
    {
        // given
        var operationId = Guid.NewGuid();
        var incarnationId = Guid.NewGuid();
        var operations = Substitute.For<IInboxOperationsApi>();
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage.GetInboxOperationsApi().Returns(operations);
        operations
            .PurgeAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                ValueTask.FromResult(
                    new InboxOperationResult(
                        operationId,
                        InboxOperationType.Purge,
                        InboxOperationOutcome.Applied,
                        incarnationId,
                        StatusName.Failed,
                        Guid.NewGuid(),
                        null,
                        null,
                        null,
                        "dashboard-operator",
                        "retention complete",
                        DateTimeOffset.UtcNow
                    )
                )
            );

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync(
            "/api/received/delete",
            new
            {
                operationId,
                expectedIncarnationId = incarnationId,
                expectedStatus = nameof(StatusName.Failed),
                reason = "retention complete",
            },
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await operations
            .Received(1)
            .PurgeAsync(
                Arg.Is<InboxOperationRequest>(request =>
                    request.OperationId == operationId
                    && request.ExpectedIncarnationId == incarnationId
                    && request.Actor == "dashboard-operator"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("/api/received/reexecute", InboxOperationType.ForceReprocess)]
    [InlineData("/api/inbox/hold", InboxOperationType.Hold)]
    [InlineData("/api/inbox/release", InboxOperationType.ReleaseHold)]
    public async Task should_route_generation_actions_through_audited_inbox_operations(
        string path,
        InboxOperationType operationType
    )
    {
        var operationId = Guid.NewGuid();
        var incarnationId = Guid.NewGuid();
        var operations = Substitute.For<IInboxOperationsApi>();
        var result = new InboxOperationResult(
            operationId,
            operationType,
            InboxOperationOutcome.Applied,
            incarnationId,
            StatusName.Failed,
            null,
            null,
            null,
            null,
            "dashboard-operator",
            "dashboard action",
            DateTimeOffset.UtcNow
        );
        operations
            .ForceReprocessAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        operations
            .HoldAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        operations
            .ReleaseHoldAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _dataStorage.GetInboxOperationsApi().Returns(operations);
        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            path,
            new
            {
                operationId,
                expectedIncarnationId = incarnationId,
                expectedStatus = nameof(StatusName.Failed),
                reason = "dashboard action",
            },
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Func<InboxOperationRequest, bool> matches = request =>
            request.OperationId == operationId
            && request.ExpectedIncarnationId == incarnationId
            && request.Actor == "dashboard-operator";
        switch (operationType)
        {
            case InboxOperationType.ForceReprocess:
                await operations
                    .Received(1)
                    .ForceReprocessAsync(
                        Arg.Is<InboxOperationRequest>(request => matches(request)),
                        Arg.Any<CancellationToken>()
                    );
                break;
            case InboxOperationType.Hold:
                await operations
                    .Received(1)
                    .HoldAsync(
                        Arg.Is<InboxOperationRequest>(request => matches(request)),
                        Arg.Any<CancellationToken>()
                    );
                break;
            case InboxOperationType.ReleaseHold:
                await operations
                    .Received(1)
                    .ReleaseHoldAsync(
                        Arg.Is<InboxOperationRequest>(request => matches(request)),
                        Arg.Any<CancellationToken>()
                    );
                break;
            default:
                throw new InvalidOperationException($"Unsupported operation type: {operationType}.");
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("\"-1\"")]
    [InlineData("\"Unknown\"")]
    public async Task should_reject_inbox_mutation_with_non_contract_status(string statusJson)
    {
        var operations = Substitute.For<IInboxOperationsApi>();
        _dataStorage.GetInboxOperationsApi().Returns(operations);
        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var status = JsonDocument.Parse(statusJson);

        using var response = await client.PostAsJsonAsync(
            "/api/inbox/hold",
            new
            {
                operationId = Guid.NewGuid(),
                expectedIncarnationId = Guid.NewGuid(),
                expectedStatus = status.RootElement,
                reason = "investigation",
            },
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await operations.DidNotReceive().HoldAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_inbox_mutation_without_authenticated_actor()
    {
        await using var app = _CreateTestApp(_dataStorage, authenticate: false);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/received/delete",
            new
            {
                operationId = Guid.NewGuid(),
                expectedIncarnationId = Guid.NewGuid(),
                expectedStatus = nameof(StatusName.Failed),
                reason = "retention complete",
            },
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_authorize_inbox_query_with_builtin_api_key_identity()
    {
        var operations = Substitute.For<IInboxOperationsApi>();
        operations
            .QueryAsync(
                Arg.Any<InboxGenerationQuery>(),
                Arg.Any<InboxAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(new IndexPage<InboxGenerationView>([], 0, 20, 0)));
        _dataStorage.GetInboxOperationsApi().Returns(operations);
        var config = new MessagingDashboardOptionsBuilder().WithApiKey("secret");
        await using var app = _CreateTestApp(
            _dataStorage,
            authenticate: false,
            config: config,
            useAuthenticationMiddleware: true
        );
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");

        var response = await client.GetAsync("/api/inbox", AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await operations
            .Received(1)
            .QueryAsync(
                Arg.Any<InboxGenerationQuery>(),
                Arg.Is<InboxAuthorizationContext>(authorization => authorization.Actor == "api-user"),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier, ClaimTypes.Name, null)]
    [InlineData("sub", ClaimTypes.Name, null)]
    [InlineData("sub", "display_name", " ")]
    [InlineData(ClaimTypes.NameIdentifier, "display_name", " ")]
    public async Task should_attribute_host_inbox_queries_and_mutations_to_stable_identity(
        string identifierClaimType,
        string nameClaimType,
        string? name
    )
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(identifierClaimType, "operator-42"),
                new Claim("tenant", "tenant-7"),
                new Claim("permission", "inbox.manage"),
                new Claim("host_role", "operator"),
            ],
            "test",
            nameClaimType,
            "host_role"
        );
        if (name is not null)
        {
            identity.AddClaim(new Claim(nameClaimType, name));
        }
        // NameIdentifier wins when a token exposes both identifier forms.
        identity.AddClaim(new Claim("sub", "secondary-subject"));
        var principal = new ClaimsPrincipal(identity);
        principal.AddIdentity(new ClaimsIdentity([new Claim("extra", "preserved")], "secondary"));
        var operations = Substitute.For<IInboxOperationsApi>();
        InboxAuthorizationContext? queryAuthority = null;
        InboxOperationRequest? mutationRequest = null;
        operations
            .QueryAsync(
                Arg.Any<InboxGenerationQuery>(),
                Arg.Any<InboxAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                queryAuthority = call.Arg<InboxAuthorizationContext>();
                queryAuthority.Validate();
                return ValueTask.FromResult(new IndexPage<InboxGenerationView>([], 0, 20, 0));
            });
        operations
            .HoldAsync(Arg.Any<InboxOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                mutationRequest = call.Arg<InboxOperationRequest>();
                mutationRequest.Validate();
                return ValueTask.FromResult(
                    new InboxOperationResult(
                        mutationRequest.OperationId,
                        InboxOperationType.Hold,
                        InboxOperationOutcome.Applied,
                        mutationRequest.ExpectedIncarnationId,
                        mutationRequest.ExpectedStatus,
                        null,
                        null,
                        null,
                        null,
                        mutationRequest.Actor,
                        mutationRequest.Reason,
                        DateTimeOffset.UtcNow
                    )
                );
            });
        _dataStorage.GetInboxOperationsApi().Returns(operations);
        await using var app = _CreateTestApp(
            _dataStorage,
            config: new MessagingDashboardOptionsBuilder().WithHostAuthentication("InboxOperator"),
            useAuthenticationMiddleware: true,
            principal: principal
        );
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var query = await client.GetAsync("/api/inbox", AbortToken);
        using var mutation = await client.PostAsJsonAsync(
            "/api/inbox/hold",
            new
            {
                operationId = Guid.NewGuid(),
                expectedIncarnationId = Guid.NewGuid(),
                expectedStatus = nameof(StatusName.Failed),
                reason = "investigation",
            },
            AbortToken
        );

        query.StatusCode.Should().Be(HttpStatusCode.OK);
        mutation.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var authority in new[] { queryAuthority!, mutationRequest!.Authorization })
        {
            authority.Actor.Should().Be("operator-42");
            authority.Principal.IsInRole("operator").Should().BeTrue();
            authority.Principal.HasClaim("tenant", "tenant-7").Should().BeTrue();
            authority.Principal.HasClaim("permission", "inbox.manage").Should().BeTrue();
            authority.Principal.HasClaim("extra", "preserved").Should().BeTrue();
            authority.Principal.Should().NotBeSameAs(principal);
        }
        identity.Name.Should().Be(name);
        principal.Identities.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_reject_inbox_actor_without_authenticated_stable_identity(bool authenticated)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("host_role", "operator"),
                    new Claim("tenant", "tenant-7"),
                    new Claim("permission", "inbox.manage"),
                ],
                authenticated ? "test" : null,
                ClaimTypes.Name,
                "host_role"
            )
        );
        principal.AddIdentity(new ClaimsIdentity([new Claim("sub", "untrusted-subject")]));
        await using var app = _CreateTestApp(
            _dataStorage,
            config: authenticated
                ? new MessagingDashboardOptionsBuilder().WithHostAuthentication("InboxOperator")
                : null,
            useAuthenticationMiddleware: authenticated,
            principal: principal
        );
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var query = await client.GetAsync("/api/inbox", AbortToken);
        using var content = new StringContent("{}", Encoding.UTF8, "text/plain");
        using var mutation = await client.PostAsync("/api/inbox/hold", content, AbortToken);

        query.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mutation.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _dataStorage.DidNotReceive().GetInboxOperationsApi();
    }

    [Theory]
    [InlineData("host_role", "viewer")]
    [InlineData("tenant", "tenant-8")]
    [InlineData("permission", "inbox.read")]
    public async Task should_preserve_host_inbox_policy_for_unnamed_identity(string claimType, string value)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", "operator-42"),
                new Claim("host_role", "operator"),
                new Claim("tenant", "tenant-7"),
                new Claim("permission", "inbox.manage"),
            ],
            "test",
            ClaimTypes.Name,
            "host_role"
        );
        identity.RemoveClaim(identity.FindFirst(claimType)!);
        identity.AddClaim(new Claim(claimType, value));
        await using var app = _CreateTestApp(
            _dataStorage,
            config: new MessagingDashboardOptionsBuilder().WithHostAuthentication("InboxOperator"),
            useAuthenticationMiddleware: true,
            principal: new ClaimsPrincipal(identity)
        );
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        using var query = await client.GetAsync("/api/inbox", AbortToken);
        using var mutation = await client.PostAsJsonAsync("/api/inbox/hold", new { }, AbortToken);

        query.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        mutation.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _dataStorage.DidNotReceive().GetInboxOperationsApi();
    }

    [Theory]
    [InlineData("/api/inbox/hold", "text/plain")]
    [InlineData("/api/inbox/release", "application/x-www-form-urlencoded")]
    [InlineData("/api/received/delete", "application/xml")]
    [InlineData("/api/received/reexecute", "text/plain")]
    [InlineData("/api/inbox/hold", null)]
    public async Task should_return_415_for_non_json_inbox_mutations(string path, string? contentType)
    {
        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var content = new StringContent("{}", Encoding.UTF8);
        content.Headers.ContentType = contentType is null ? null : new MediaTypeHeaderValue(contentType);

        using var response = await client.PostAsync(path, content, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _dataStorage.DidNotReceive().GetInboxOperationsApi();
    }

    [Theory]
    [InlineData("/api/inbox/hold")]
    [InlineData("/api/inbox/release")]
    [InlineData("/api/received/delete")]
    [InlineData("/api/received/reexecute")]
    public async Task should_return_422_for_malformed_json_inbox_mutations(string path)
    {
        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(path, content, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _dataStorage.DidNotReceive().GetInboxOperationsApi();
    }

    private static WebApplication _CreateTestApp(
        IDataStorage dataStorage,
        bool authenticate = true,
        MessagingDashboardOptionsBuilder? config = null,
        bool useAuthenticationMiddleware = false,
        bool customizeHostJson = false,
        ClaimsPrincipal? principal = null
    )
    {
        config ??= new MessagingDashboardOptionsBuilder().WithNoAuth();

        var appBuilder = WebApplication.CreateSlimBuilder();
        appBuilder.WebHost.UseTestServer();
        if (customizeHostJson)
        {
            appBuilder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });
        }

        appBuilder.Services.AddSingleton(config);
        appBuilder.Services.AddSingleton(config.Auth);
        appBuilder.Services.AddScoped<IAuthService, AuthService>();
        appBuilder.Services.AddSingleton(dataStorage);
        appBuilder.Services.AddSingleton<MessagingMetricsEventListener>();

        // Gateway proxy deps for ActivatorUtilities resolution
        appBuilder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        appBuilder.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        appBuilder.Services.AddSingleton<MessagingDashboardCache>();
        appBuilder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        appBuilder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        appBuilder.Services.AddSingleton<GatewayProxyAgent>();

        appBuilder.Services.AddRouting();
        appBuilder.Services.AddAuthorization();
        if (config.Auth.Mode == AuthMode.Host)
        {
            appBuilder
                .Services.AddAuthentication("test")
                .AddCookie(
                    "test",
                    options =>
                    {
                        options.Events.OnRedirectToAccessDenied = context =>
                        {
                            context.Response.StatusCode = 403;
                            return Task.CompletedTask;
                        };
                    }
                );
            appBuilder
                .Services.AddAuthorizationBuilder()
                .AddPolicy(
                    "InboxOperator",
                    policy =>
                        policy
                            .RequireAuthenticatedUser()
                            .RequireRole("operator")
                            .RequireClaim("tenant", "tenant-7")
                            .RequireClaim("permission", "inbox.manage")
                );
        }
        appBuilder.Services.AddCors(o => o.AddPolicy("HeadlessMessagingDashboardCORS", p => p.AllowAnyOrigin()));

        var app = appBuilder.Build();
        app.UseRouting();
        if (principal is not null)
        {
            app.Use(
                (context, next) =>
                {
                    context.User = principal;
                    return next(context);
                }
            );
        }
        if (useAuthenticationMiddleware)
        {
            app.UseMiddleware<AuthMiddleware>();
        }
        else if (authenticate && principal is null)
        {
            app.Use(
                (context, next) =>
                {
                    context.User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim(ClaimTypes.Name, "dashboard-operator")],
                            authenticationType: "test"
                        )
                    );
                    return next(context);
                }
            );
        }
        app.UseCors("HeadlessMessagingDashboardCORS");
        app.UseAuthorization();
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }
}
