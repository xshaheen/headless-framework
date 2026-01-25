// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class ReceivedMessageEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task ReceivedMessageDetails_should_return_message_content()
    {
        // given
        const long messageId = 456;
        var message = new MediumMessage
        {
            DbId = messageId.ToString(CultureInfo.InvariantCulture),
            Content = "{\"received\":\"data\"}",
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal) { { "test", "header" } },
                new { Data = "received" }
            ),
        };

        _monitoringApi.GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(message));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);
        context.Request.RouteValues["id"] = messageId.ToString(CultureInfo.InvariantCulture);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ReceivedMessageDetails(context);

        // then
        context.Response.StatusCode.Should().NotBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ReceivedMessageDetails_should_return_404_for_missing_message()
    {
        // given
        const long messageId = 888;
        _monitoringApi.GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(null));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);
        context.Request.RouteValues["id"] = messageId.ToString(CultureInfo.InvariantCulture);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ReceivedMessageDetails(context);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ReceivedMessageDetails_should_return_400_for_invalid_id()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);
        context.Request.RouteValues["id"] = "invalid-id";

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ReceivedMessageDetails(context);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ReceivedRequeue_should_return_422_for_empty_array()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);
        var emptyArray = "[]"u8.ToArray();
        context.Request.Body = new MemoryStream(emptyArray);
        context.Request.ContentType = "application/json";

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ReceivedRequeue(context);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task ReceivedDelete_should_return_422_for_null_body()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);
        var nullJson = "null"u8.ToArray();
        context.Request.Body = new MemoryStream(nullJson);
        context.Request.ContentType = "application/json";

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ReceivedDelete(context);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task ReceivedDelete_should_return_204_on_success()
    {
        // given
        const long messageId = 789;
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage.DeleteReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(1));

        var context = _CreateHttpContext(_dataStorage);
        var json = System.Text.Encoding.UTF8.GetBytes($"[{messageId}]");
        context.Request.Body = new MemoryStream(json);
        context.Request.ContentType = "application/json";

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ReceivedDelete(context);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    private static DefaultHttpContext _CreateHttpContext(IDataStorage dataStorage)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(dataStorage)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        return context;
    }
}
