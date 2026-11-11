// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Guarantees;

namespace Tests;

public abstract partial class MessagingIntegrationTestsBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Guarantee("MSG-RR-06")]
    public virtual async Task should_send_a_reply_only_after_the_success_write_of_its_request_takes_effect(
        bool transactional
    )
    {
        Assert.SkipWhen(
            ServiceProvider.GetService<IReplyTransport>() is null,
            "This transport does not offer request/reply."
        );

        // given: a responder held mid-request, on the transactional inbox tier or the plain one
        var gate = new ReplyOrderingGate();
        var requestName = $"reply-ordering.{Guid.NewGuid():N}";
        await using var host = await _StartReplyOrderingHostAsync(gate, requestName, transactional);
        var reply = host.GetRequiredService<IRequestClient>()
            .RequestAsync<ReplyOrderingRequest, ReplyOrderingAnswer>(
                new ReplyOrderingRequest("sku-1"),
                new RequestOptions { Timeout = _DeliveryTimeout },
                AbortToken
            );
        await gate.Started.Task.WaitAsync(_DeliveryTimeout, AbortToken);
        var storageId = await _ReplyOrderingRowIdAsync(host, ResolveMessageName(requestName));

        // when: another transaction holds the request's received row, so the success write cannot take effect, and
        // the responder returns its answer
        await using var connection = CreateStorageConnection();
        await connection.OpenAsync(AbortToken);
        var dialect = ReplyOrderingDialect.Of(connection);
        await using var holder = await connection.BeginTransactionAsync(AbortToken);
        var receivedTable = host.GetRequiredService<IStorageTableNames>().GetReceivedTableName();
        await _ExecuteScalarAsync(connection, holder, dialect.LockRowSql(receivedTable), storageId);
        gate.Release.TrySetResult();
        await _WaitUntilAWriteWaitsOnAsync(connection, holder, dialect);

        // then: no reply leaves while the success write waits
        var early = async () => await reply.WaitAsync(_NoDuplicateWindow, AbortToken);
        await early
            .Should()
            .ThrowAsync<TimeoutException>("the reply waits until the success write of its request takes effect");

        // and: once the row is free the write lands, then the reply arrives
        await holder.CommitAsync(AbortToken);
        (await reply.WaitAsync(_DeliveryTimeout, AbortToken)).Sku.Should().Be("sku-1");
        var row = await host.GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetReceivedMessageAsync(storageId, AbortToken);
        row!.StatusName.Should().Be(StatusName.Succeeded);
    }

    private async Task<ServiceProvider> _StartReplyOrderingHostAsync(
        ReplyOrderingGate gate,
        string requestName,
        bool transactional
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddSingleton(gate);
        services.AddHeadlessMessaging(setup =>
        {
            ConfigureTransport(setup);
            ConfigureStorage(setup);
            setup.Options.RetryPolicy.DispatchTimeout = TimeSpan.FromSeconds(40);
            setup.Options.ShutdownTimeout = TimeSpan.FromSeconds(5);
            setup.AddRequestReply();
            ConfigureMessaging(setup);

            // Set after the leaf's own configuration, so the tier under test always wins.
            setup.Options.MinimumInboxGuarantee = transactional ? InboxGuarantee.Transactional : InboxGuarantee.Durable;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ReplyOrderingRequest>(requestName);
            messaging.Message<ReplyOrderingAnswer>($"{requestName}.answer");
            messaging.AddModule<ReplyOrderingModule>();
        });
        ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        try
        {
            await provider.ApplyMessagingSchemaAsync(AbortToken);
            await provider.ProvisionResourcesAsync(AbortToken);
            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        return provider;
    }

    private static async Task<Guid> _ReplyOrderingRowIdAsync(IServiceProvider host, string name)
    {
        var page = await host.GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = name,
                    CurrentPage = 0,
                    PageSize = 10,
                },
                AbortToken
            );

        return page.Items.Should().ContainSingle("the request is admitted before its responder runs").Subject.StorageId;
    }

    private async Task _WaitUntilAWriteWaitsOnAsync(
        DbConnection connection,
        DbTransaction holder,
        ReplyOrderingDialect dialect
    )
    {
        var timeProvider = ServiceProvider.GetRequiredService<TimeProvider>();
        var deadline = timeProvider.GetUtcNow() + _DeliveryTimeout;

        while (await _ExecuteScalarAsync(connection, holder, dialect.CountBlockedSql, storageId: null) is 0)
        {
            timeProvider.GetUtcNow().Should().BeBefore(deadline, "the success write reaches the held row");
            await timeProvider.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        }
    }

    private static async Task<long> _ExecuteScalarAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        Guid? storageId
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (storageId is { } id)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = id;
            command.Parameters.Add(parameter);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The SQL that holds a received row and asks whether another session waits on it, in the dialect of the leaf's
    /// messaging database. The pair projects run PostgreSQL or SQL Server.
    /// </summary>
    private sealed record ReplyOrderingDialect(Func<string, string> LockRowSql, string CountBlockedSql)
    {
        public static ReplyOrderingDialect Of(DbConnection connection) =>
            connection.GetType().Name switch
            {
                "NpgsqlConnection" => new(
                    table => $"SELECT 1 FROM {table} WHERE \"id\" = @id FOR UPDATE",
                    "SELECT COUNT(*) FROM pg_stat_activity WHERE pg_backend_pid() = ANY(pg_blocking_pids(pid))"
                ),
                "SqlConnection" => new(
                    table => $"SELECT 1 FROM {table} WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = @id",
                    "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @@SPID"
                ),
                var other => throw new NotSupportedException(
                    $"The reply-ordering scenario has no row-lock SQL for {other}; add its dialect here."
                ),
            };
    }
}

[PublicAPI]
public sealed record ReplyOrderingRequest(string Sku);

[PublicAPI]
public sealed record ReplyOrderingAnswer(string Sku);

/// <summary>Holds the reply-ordering responder until the scenario has locked the request's received row.</summary>
[PublicAPI]
public sealed class ReplyOrderingGate
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[PublicAPI]
public sealed class ReplyOrderingResponder(ReplyOrderingGate gate) : IRespond<ReplyOrderingRequest, ReplyOrderingAnswer>
{
    public async ValueTask<ReplyOrderingAnswer> RespondAsync(
        ConsumeContext<ReplyOrderingRequest> context,
        CancellationToken cancellationToken
    )
    {
        gate.Started.TrySetResult();

        // Host shutdown cancels the token, so a scenario that fails before releasing never hangs disposal.
        await gate.Release.Task.WaitAsync(cancellationToken);
        return new ReplyOrderingAnswer(context.Message.Sku);
    }
}

/// <summary>What the source generator emits for <see cref="ReplyOrderingResponder"/>; the harness does not run it.</summary>
internal sealed class ReplyOrderingModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueResponder<ReplyOrderingResponder, ReplyOrderingRequest, ReplyOrderingAnswer>(
            "tests.reply-ordering.desk",
            static async (services, context, cancellationToken) =>
            {
                var typed = (ConsumeContext<ReplyOrderingRequest>)context;
                var answer = await ActivatorUtilities
                    .CreateInstance<ReplyOrderingResponder>(services)
                    .RespondAsync(typed, cancellationToken)
                    .ConfigureAwait(false);
                typed.RecordReply(answer);
            }
        );
    }
}
