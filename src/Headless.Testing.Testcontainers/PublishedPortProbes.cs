// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Containers;
using Headless.Checks;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// <see cref="PublishedPortProbe"/>s for the servers the Headless fixtures run. Each compares a value the server sends
/// through the published port with the same value read inside the container, so another server of the same kind does
/// not pass.
/// </summary>
[PublicAPI]
public static partial class PublishedPortProbes
{
    private const int _MaxHttpReply = 64 * 1024;

    // A Kafka reply larger than this is not a metadata reply for one broker and no topics: it is another protocol.
    private const int _MaxKafkaReply = 1024 * 1024;

    /// <summary>
    /// Gets a Redis probe that compares the <c>run_id</c> of <c>INFO server</c>, new on every server start, with the one
    /// <c>redis-cli</c> reads inside the container.
    /// </summary>
    public static PublishedPortProbe RedisRunId { get; } = _RedisRunIdAsync;

    /// <summary>
    /// Gets a RabbitMQ probe that reads the AMQP 0-9-1 <c>connection.start</c> and checks that its cluster name carries
    /// the container's host name.
    /// </summary>
    public static PublishedPortProbe RabbitMqClusterName { get; } = _RabbitMqClusterNameAsync;

    /// <summary>
    /// Gets a LocalStack probe that compares the <c>session_id</c> of <c>/_localstack/info</c>, new on every start, with
    /// the one read inside the container.
    /// </summary>
    public static PublishedPortProbe LocalStackSessionId { get; } = _LocalStackSessionIdAsync;

    /// <summary>
    /// Gets a Kafka probe that compares the cluster id of a metadata reply with the one the broker stored in
    /// <c>/var/lib/kafka/data/meta.properties</c> inside the container. A ZooKeeper-mode broker gets a random id; start a
    /// KRaft broker with a <c>CLUSTER_ID</c> from <see cref="NewKafkaClusterId"/>, because a fixed id is the same in
    /// every container started with it.
    /// </summary>
    public static PublishedPortProbe KafkaClusterId { get; } = _KafkaClusterIdAsync;

    /// <summary>
    /// Returns a NATS probe that checks the <c>server_name</c> of the server's <c>INFO</c> greeting. Start the server
    /// with <c>--server_name</c> set to a name only it uses: by default the name is the server id, new on every start.
    /// </summary>
    public static PublishedPortProbe NatsServerName(string serverName)
    {
        Argument.IsNotNullOrWhiteSpace(serverName);

        return (_, _, stream, cancellationToken) => _NatsServerNameAsync(stream, serverName, cancellationToken);
    }

    /// <summary>Returns a fresh KRaft cluster id: 16 random bytes in unpadded URL-safe base64.</summary>
    public static string NewKafkaClusterId()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Returns a PostgreSQL probe that compares the server's <c>system_identifier</c>, fixed when its data directory was
    /// created, with the one <c>psql</c> reads inside the container.
    /// </summary>
    /// <param name="username">A superuser the probe logs in as, through the port and inside the container.</param>
    /// <param name="password">The superuser's password.</param>
    public static PublishedPortProbe PostgreSqlSystemIdentifier(string username, string password)
    {
        Argument.IsNotNullOrWhiteSpace(username);
        Argument.IsNotNull(password);

        return (container, containerPort, stream, cancellationToken) =>
            _PostgreSqlSystemIdentifierAsync(container, containerPort, stream, username, password, cancellationToken);
    }

    /// <summary>
    /// Returns a SQL Server probe that compares <c>SERVERPROPERTY('MachineName')</c> with the container's host name.
    /// </summary>
    /// <param name="password">The <c>sa</c> password the probe logs in with.</param>
    public static PublishedPortProbe SqlServerMachineName(string password)
    {
        Argument.IsNotNull(password);

        return (container, containerPort, stream, cancellationToken) =>
            _SqlServerMachineNameAsync(container, containerPort, stream, password, cancellationToken);
    }

    /// <summary>
    /// Returns an Azurite probe that checks that the server knows <paramref name="accountName"/>, an account only this
    /// container was started with (<c>AZURITE_ACCOUNTS</c>). Azurite refuses an unsigned request to an account it knows
    /// and reports any other account missing.
    /// </summary>
    /// <param name="accountName">The account only this container serves.</param>
    /// <param name="tablePort">The container port of the table service, which takes a different request.</param>
    public static PublishedPortProbe AzuriteAccount(string accountName, int tablePort)
    {
        Argument.IsNotNullOrWhiteSpace(accountName);

        return (_, containerPort, stream, cancellationToken) =>
            _AzuriteAccountAsync(stream, accountName, containerPort == tablePort, cancellationToken);
    }

    /// <summary>
    /// Returns a probe for a server with no value of its own to compare: it sends <paramref name="hello"/>, waits for any
    /// reply, and, with the connection still open, checks inside the container that an established connection reached the
    /// port. Use it only for a container no other client dials while it starts, and send a hello after which the server
    /// keeps the connection open.
    /// </summary>
    /// <param name="hello">The first bytes of the server's protocol, such as an HTTP/1.1 request without <c>Connection: close</c>.</param>
    public static PublishedPortProbe ConnectionSeenInside(ReadOnlyMemory<byte> hello)
    {
        return (container, containerPort, stream, cancellationToken) =>
            _ConnectionSeenInsideAsync(container, containerPort, stream, hello, cancellationToken);
    }

    /// <summary>
    /// Sends <paramref name="request"/> and reads the HTTP response until the server closes the connection.
    /// </summary>
    /// <returns>The status code and the whole response text, or <see langword="null"/> when nothing arrived.</returns>
    internal static async Task<(int Status, string Text)?> ReadHttpAsync(
        NetworkStream stream,
        string request,
        CancellationToken cancellationToken
    )
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        var text = await PublishedPortCheck
            .ReadReplyAsync(stream, static _ => false, _MaxHttpReply, cancellationToken)
            .ConfigureAwait(false);

        if (text is null)
        {
            return null;
        }

        var match = _HttpStatus.Match(text);
        return (match.Success ? int.Parse(match.Groups["value"].ValueSpan, CultureInfo.InvariantCulture) : 0, text);
    }

    private static async Task<PublishedPortReply> _RedisRunIdAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        await stream.WriteAsync("INFO server\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        var reply = await PublishedPortCheck
            .ReadReplyAsync(stream, _IsCompleteRespReply, maxBytes: 16 * 1024, cancellationToken)
            .ConfigureAwait(false);

        switch (reply)
        {
            // The runtime accepted the connection but cannot reach the server yet.
            case null:
            // Redis answers LOADING while it reads its data at start, and serves once that finishes.
            case not null when reply.StartsWith("-LOADING", StringComparison.Ordinal):
                return PublishedPortReply.NotYet;
        }

        var answered = _RedisRunIdLine.Match(reply);

        if (!answered.Success)
        {
            return PublishedPortReply.AnotherListener;
        }

        var inside = await container
            .ExecAsync(
                ["redis-cli", "-p", containerPort.ToString(CultureInfo.InvariantCulture), "INFO", "server"],
                cancellationToken
            )
            .ConfigureAwait(false);
        var own = _RedisRunIdLine.Match(inside.Stdout);

        if (inside.ExitCode != 0 || !own.Success)
        {
            return PublishedPortReply.NotYet;
        }

        return string.Equals(answered.Groups["value"].Value, own.Groups["value"].Value, StringComparison.Ordinal)
            ? PublishedPortReply.Container
            : PublishedPortReply.AnotherListener;
    }

    // A bulk string ($<length>\r\n<data>\r\n) is whole once its data arrived; a status or error is one line; anything
    // else is not Redis and is whole as it is.
    private static bool _IsCompleteRespReply(string text)
    {
        if (text.StartsWith('$', StringComparison.Ordinal))
        {
            var header = text.IndexOf("\r\n", StringComparison.Ordinal);

            return header > 0
                && int.TryParse(text.AsSpan(1, header - 1), CultureInfo.InvariantCulture, out var length)
                && text.Length >= header + 2 + Math.Max(length, 0) + 2;
        }

        return text[0] is not ('+' or '-') || text.Contains("\r\n", StringComparison.Ordinal);
    }

    private static async Task<PublishedPortReply> _NatsServerNameAsync(
        NetworkStream stream,
        string serverName,
        CancellationToken cancellationToken
    )
    {
        // A NATS server greets every connection with an INFO line before the client speaks. The PING goes first anyway,
        // so a listener that waits for the client, such as an HTTP server, answers it and shows it is not NATS.
        await stream.WriteAsync("PING\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        var greeting = await PublishedPortCheck
            .ReadReplyAsync(
                stream,
                static text => text.Contains("\r\n", StringComparison.Ordinal),
                8192,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (greeting is null)
        {
            return PublishedPortReply.NotYet;
        }

        return
            greeting.StartsWith("INFO ", StringComparison.Ordinal)
            && greeting.Contains($"\"server_name\":\"{serverName}\"", StringComparison.Ordinal)
            ? PublishedPortReply.Container
            : PublishedPortReply.AnotherListener;
    }

    private static async Task<PublishedPortReply> _RabbitMqClusterNameAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        await stream.WriteAsync("AMQP\0\0\u0009\u0001"u8.ToArray(), cancellationToken).ConfigureAwait(false);

        // A frame is a 7-byte header (type, channel, payload size), the payload, and an end byte.
        var header = new byte[7];
        var read = await stream
            .ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read == 0)
        {
            return PublishedPortReply.NotYet;
        }

        var size = read == header.Length ? BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(3)) : -1;

        if (header[0] != 1 || size is <= 0 or > _MaxHttpReply)
        {
            return PublishedPortReply.AnotherListener;
        }

        var payload = new byte[size + 1];
        read = await stream
            .ReadAtLeastAsync(payload, payload.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read < payload.Length)
        {
            return PublishedPortReply.NotYet;
        }

        var hostName = await _ReadHostNameAsync(container, cancellationToken).ConfigureAwait(false);

        if (hostName is null)
        {
            return PublishedPortReply.NotYet;
        }

        return Encoding.ASCII.GetString(payload).Contains("rabbit@" + hostName, StringComparison.Ordinal)
            ? PublishedPortReply.Container
            : PublishedPortReply.AnotherListener;
    }

    private static async Task<PublishedPortReply> _LocalStackSessionIdAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        var response = await ReadHttpAsync(
                stream,
                "GET /_localstack/info HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n",
                cancellationToken
            )
            .ConfigureAwait(false);

        if (response is not { } answered)
        {
            return PublishedPortReply.NotYet;
        }

        var session = _LocalStackSession.Match(answered.Text);

        if (!session.Success)
        {
            // A gateway still starting answers 5xx; any other reply without a session is another server.
            return answered.Status >= 500 ? PublishedPortReply.NotYet : PublishedPortReply.AnotherListener;
        }

        var inside = await container
            .ExecAsync(
                [
                    "curl",
                    "-s",
                    $"http://localhost:{containerPort.ToString(CultureInfo.InvariantCulture)}/_localstack/info",
                ],
                cancellationToken
            )
            .ConfigureAwait(false);
        var own = _LocalStackSession.Match(inside.Stdout);

        if (inside.ExitCode != 0 || !own.Success)
        {
            return PublishedPortReply.NotYet;
        }

        return string.Equals(session.Groups["value"].Value, own.Groups["value"].Value, StringComparison.Ordinal)
            ? PublishedPortReply.Container
            : PublishedPortReply.AnotherListener;
    }

    private static async Task<PublishedPortReply> _KafkaClusterIdAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        // Metadata v2 (the first version with the cluster id), correlation 1, client id "probe", no topics.
        byte[] request =
        [
            0,
            0,
            0,
            19, // size of what follows
            0,
            3, // api key: Metadata
            0,
            2, // api version
            0,
            0,
            0,
            1, // correlation id
            0,
            5,
            (byte)'p',
            (byte)'r',
            (byte)'o',
            (byte)'b',
            (byte)'e', // client id
            0,
            0,
            0,
            0, // topics: none
        ];
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var sizeBytes = new byte[4];
        var read = await stream
            .ReadAtLeastAsync(sizeBytes, sizeBytes.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read == 0)
        {
            return PublishedPortReply.NotYet;
        }

        var size = read == sizeBytes.Length ? BinaryPrimitives.ReadInt32BigEndian(sizeBytes) : -1;

        if (size is < 8 or > _MaxKafkaReply)
        {
            return PublishedPortReply.AnotherListener;
        }

        var body = new byte[size];
        read = await stream
            .ReadAtLeastAsync(body, body.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read < body.Length)
        {
            return PublishedPortReply.NotYet;
        }

        var answered = _ReadKafkaClusterId(body);

        if (answered is null)
        {
            return PublishedPortReply.AnotherListener;
        }

        var inside = await container
            .ExecAsync(["cat", "/var/lib/kafka/data/meta.properties"], cancellationToken)
            .ConfigureAwait(false);
        var own = _KafkaStoredClusterId.Match(inside.Stdout);

        if (inside.ExitCode != 0 || !own.Success)
        {
            return PublishedPortReply.NotYet;
        }

        return string.Equals(answered, own.Groups["value"].Value, StringComparison.Ordinal)
            ? PublishedPortReply.Container
            : PublishedPortReply.AnotherListener;
    }

    // Reads the cluster id of a Metadata v2 response body: correlation id, brokers (node id, host, port, rack), then the
    // nullable cluster id. Returns null for a body that is not one.
    private static string? _ReadKafkaClusterId(ReadOnlySpan<byte> body)
    {
        try
        {
            var offset = 4; // correlation id
            var brokers = _Int32(body, ref offset);

            if (brokers is < 0 or > 1024)
            {
                return null;
            }

            for (var i = 0; i < brokers; i++)
            {
                offset += 4; // node id
                _String(body, ref offset); // host
                offset += 4; // port
                _String(body, ref offset); // rack
            }

            return _String(body, ref offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        static int _Int32(ReadOnlySpan<byte> body, ref int offset)
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(body[offset..]);
            offset += 4;
            return value;
        }

        static string? _String(ReadOnlySpan<byte> body, ref int offset)
        {
            var length = BinaryPrimitives.ReadInt16BigEndian(body[offset..]);
            offset += 2;

            if (length < 0)
            {
                return null;
            }

            var value = Encoding.UTF8.GetString(body.Slice(offset, length));
            offset += length;
            return value;
        }
    }

    private static async Task<PublishedPortReply> _PostgreSqlSystemIdentifierAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        string username,
        string password,
        CancellationToken cancellationToken
    )
    {
        // An SSLRequest gets one byte from any PostgreSQL server, S or N, before any login.
        await stream.WriteAsync(new byte[] { 0, 0, 0, 8, 4, 210, 22, 47 }, cancellationToken).ConfigureAwait(false);
        var answer = new byte[1];
        var read = await stream
            .ReadAtLeastAsync(answer, 1, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read == 0)
        {
            return PublishedPortReply.NotYet;
        }

        if (answer[0] is not ((byte)'S' or (byte)'N'))
        {
            return PublishedPortReply.AnotherListener;
        }

        const string query = "SELECT system_identifier::text FROM pg_control_system()";
        var inside = await container
            .ExecAsync(["psql", "-U", username, "-d", "postgres", "-tAc", query], cancellationToken)
            .ConfigureAwait(false);

        if (inside.ExitCode != 0 || string.IsNullOrWhiteSpace(inside.Stdout))
        {
            return PublishedPortReply.NotYet;
        }

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(containerPort),
            Username = username,
            Password = password,
            Database = "postgres",
            // A pooled connection would outlive a restart that moves the server to another port.
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 5,
        }.ConnectionString;

        try
        {
            var connection = new NpgsqlConnection(connectionString);

            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = new NpgsqlCommand(query, connection);

                await using (command.ConfigureAwait(false))
                {
                    var answered = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                    return string.Equals(answered, inside.Stdout.Trim(), StringComparison.Ordinal)
                        ? PublishedPortReply.Container
                        : PublishedPortReply.AnotherListener;
                }
            }
        }
        catch (PostgresException e)
            when (e.SqlState
                    is PostgresErrorCodes.InvalidPassword
                        or PostgresErrorCodes.InvalidAuthorizationSpecification
            )
        {
            // This container accepts these credentials, so a server that refuses them is another one.
            return PublishedPortReply.AnotherListener;
        }
        catch (NpgsqlException)
        {
            return PublishedPortReply.NotYet;
        }
    }

    private static async Task<PublishedPortReply> _SqlServerMachineNameAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        string password,
        CancellationToken cancellationToken
    )
    {
        // A TDS PRELOGIN with only the version option; any SQL Server answers it with a tabular-result packet (0x04)
        // before any login.
        byte[] prelogin =
        [
            0x12,
            0x01,
            0x00,
            0x14,
            0x00,
            0x00,
            0x01,
            0x00,
            0x00,
            0x00,
            0x06,
            0x00,
            0x06,
            0xFF,
            0,
            0,
            0,
            0,
            0,
            0,
        ];
        await stream.WriteAsync(prelogin, cancellationToken).ConfigureAwait(false);
        var answer = new byte[1];
        var read = await stream
            .ReadAtLeastAsync(answer, 1, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read == 0)
        {
            return PublishedPortReply.NotYet;
        }

        if (answer[0] != 0x04)
        {
            return PublishedPortReply.AnotherListener;
        }

        var hostName = await _ReadHostNameAsync(container, cancellationToken).ConfigureAwait(false);

        if (hostName is null)
        {
            return PublishedPortReply.NotYet;
        }

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource =
                $"{container.Hostname},{container.GetMappedPublicPort(containerPort).ToString(CultureInfo.InvariantCulture)}",
            InitialCatalog = "master",
            UserID = "sa",
            Password = password,
            TrustServerCertificate = true,
            // A pooled connection would outlive a restart that moves the server to another port.
            Pooling = false,
            ConnectTimeout = 5,
            CommandTimeout = 5,
        }.ConnectionString;

        try
        {
            var connection = new SqlConnection(connectionString);

            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = new SqlCommand(
                    "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('MachineName'))",
                    connection
                );

                await using (command.ConfigureAwait(false))
                {
                    var answered = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                    return string.Equals(answered, hostName, StringComparison.OrdinalIgnoreCase)
                        ? PublishedPortReply.Container
                        : PublishedPortReply.AnotherListener;
                }
            }
        }
        catch (SqlException)
        {
            // A login refused while the server recovers its databases looks like one refused by another server, so it
            // waits out the check's answer budget instead of naming either.
            return PublishedPortReply.NotYet;
        }
    }

    private static async Task<PublishedPortReply> _AzuriteAccountAsync(
        NetworkStream stream,
        string accountName,
        bool table,
        CancellationToken cancellationToken
    )
    {
        var path = table ? $"/{accountName}/Tables" : $"/{accountName}?comp=list";
        var response = await ReadHttpAsync(
                stream,
                $"GET {path} HTTP/1.1\r\nHost: localhost\r\nAccept: application/json\r\nConnection: close\r\n\r\n",
                cancellationToken
            )
            .ConfigureAwait(false);

        if (response is not { } answered)
        {
            return PublishedPortReply.NotYet;
        }

        // Azurite refuses the unsigned request to an account it serves (blob and table say AuthorizationFailure, queue
        // AuthenticationFailed), and reports an unknown account missing (404).
        return
            answered.Status == 403
            && (
                answered.Text.Contains("AuthorizationFailure", StringComparison.Ordinal)
                || answered.Text.Contains("AuthenticationFailed", StringComparison.Ordinal)
            )
            ? PublishedPortReply.Container
            : PublishedPortReply.AnotherListener;
    }

    private static async Task<PublishedPortReply> _ConnectionSeenInsideAsync(
        IContainer container,
        int containerPort,
        NetworkStream stream,
        ReadOnlyMemory<byte> hello,
        CancellationToken cancellationToken
    )
    {
        await stream.WriteAsync(hello, cancellationToken).ConfigureAwait(false);
        var reply = new byte[1];
        var read = await stream
            .ReadAtLeastAsync(reply, 1, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        if (read == 0)
        {
            return PublishedPortReply.NotYet;
        }

        // The connection is still open, so a container it reached shows it as established (state 01) on its port.
        var inside = await container
            .ExecAsync(["cat", "/proc/net/tcp", "/proc/net/tcp6"], cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(inside.Stdout))
        {
            return PublishedPortReply.NotYet;
        }

        var port = containerPort.ToString("X4", CultureInfo.InvariantCulture);

        foreach (var line in inside.Stdout.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (
                fields.Length > 3
                && fields[1].EndsWith(":" + port, StringComparison.Ordinal)
                && string.Equals(fields[3], "01", StringComparison.Ordinal)
            )
            {
                return PublishedPortReply.Container;
            }
        }

        return PublishedPortReply.AnotherListener;
    }

    private static async Task<string?> _ReadHostNameAsync(IContainer container, CancellationToken cancellationToken)
    {
        var result = await container.ExecAsync(["cat", "/etc/hostname"], cancellationToken).ConfigureAwait(false);
        var hostName = result.Stdout.Trim();

        return result.ExitCode == 0 && hostName.Length > 0 ? hostName : null;
    }

    [GeneratedRegex(@"^HTTP/\d(\.\d)? (?<value>\d{3})", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex _HttpStatus { get; }

    [GeneratedRegex(@"run_id:(?<value>[0-9a-f]+)", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex _RedisRunIdLine { get; }

    [GeneratedRegex("\"session_id\"\\s*:\\s*\"(?<value>[^\"]+)\"", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex _LocalStackSession { get; }

    [GeneratedRegex(@"^cluster\.id=(?<value>\S+)", RegexOptions.ExplicitCapture | RegexOptions.Multiline, 1000)]
    private static partial Regex _KafkaStoredClusterId { get; }
}
