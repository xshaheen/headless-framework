// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tests;

/// <summary>
/// A minimal SMTP server on a loopback port that a real MailKit <c>SmtpClient</c> talks to, so a test drives
/// MailKit's own send pipeline. It serves one connection at a time and can close the session at a chosen step of
/// a chosen transaction, the way a relay drops a session it timed out.
/// </summary>
internal sealed class LoopbackSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<int, SmtpDropPoint> _drops = [];
    private readonly Task _serving;
    private int _transactions;

    public LoopbackSmtpServer(bool pipelining = false)
    {
        Pipelining = pipelining;
        _listener.Start();
        _serving = _ServeAsync(_stop.Token);
    }

    public bool Pipelining { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections { get; private set; }

    public int MessagesQueued { get; private set; }

    /// <summary>Closes the session at <paramref name="point"/> of the transaction numbered <paramref name="transaction"/> (1-based).</summary>
    public void DropTransaction(int transaction, SmtpDropPoint point)
    {
        _drops[transaction] = point;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();

        try
        {
            await _serving;
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }

        _stop.Dispose();
    }

    private async Task _ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = await _listener.AcceptSocketAsync(cancellationToken);
            Connections++;

            await using var stream = new NetworkStream(socket, ownsSocket: false);
            await _ServeSessionAsync(stream, cancellationToken);
            socket.Close();
        }
    }

    private async Task _ServeSessionAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };
        var drop = SmtpDropPoint.None;

        await writer.WriteLineAsync("220 localhost ESMTP");

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var verb = line.Split(' ', 2)[0].ToUpperInvariant();

            switch (verb)
            {
                case "EHLO":
                    await writer.WriteLineAsync(Pipelining ? "250-localhost\r\n250 PIPELINING" : "250 localhost");
                    break;
                case "MAIL":
                    _transactions++;
                    drop = _drops.GetValueOrDefault(_transactions);

                    if (drop == SmtpDropPoint.BeforeSenderReply)
                    {
                        return;
                    }

                    await writer.WriteLineAsync("250 2.1.0 sender ok");
                    break;
                case "RCPT":
                    await writer.WriteLineAsync("250 2.1.5 recipient ok");
                    break;
                case "DATA":
                    if (drop == SmtpDropPoint.OnData)
                    {
                        return;
                    }

                    await writer.WriteLineAsync("354 end data with <CR><LF>.<CR><LF>");

                    while (
                        await reader.ReadLineAsync(cancellationToken) is { } data
                        && !string.Equals(data, ".", StringComparison.Ordinal)
                    ) { }

                    MessagesQueued++;
                    await writer.WriteLineAsync($"250 2.0.0 Ok: queued as Q{MessagesQueued}");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 2.0.0 bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 2.0.0 ok");
                    break;
            }
        }
    }
}

internal enum SmtpDropPoint
{
    None,

    /// <summary>The server closes the session on <c>MAIL FROM</c> without replying.</summary>
    BeforeSenderReply,

    /// <summary>The server accepts <c>MAIL FROM</c> and the recipients, then closes the session on <c>DATA</c>.</summary>
    OnData,
}
