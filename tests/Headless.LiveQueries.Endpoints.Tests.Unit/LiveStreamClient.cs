// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Reads one open stream of a live endpoint line by line.</summary>
internal sealed class LiveStreamClient(HttpResponseMessage response, Stream body) : IDisposable
{
    // Ends a read that would otherwise hang the run; no assertion depends on it.
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    private readonly StreamReader _reader = new(body);

    public HttpResponseMessage Response => response;

    /// <summary>Returns the next line, or <see langword="null" /> once the server has ended the stream.</summary>
    public Task<string?> NextLineAsync(CancellationToken cancellationToken)
    {
        return _WithPatienceAsync(async patience => await _reader.ReadLineAsync(patience), cancellationToken);
    }

    /// <summary>
    /// Returns the next server-sent event, skipping comments and the reconnect hint, or <see langword="null" />
    /// once the server has ended the stream.
    /// </summary>
    public Task<ServerSentEvent?> NextEventAsync(CancellationToken cancellationToken)
    {
        // One wait for the whole event: heartbeats keep arriving on a stream that sends no event, so a wait
        // started over for each line would never end.
        return _WithPatienceAsync(
            async patience =>
            {
                string? name = null;
                string? id = null;
                List<string> data = [];

                while (await _reader.ReadLineAsync(patience) is { } line)
                {
                    if (line.Length == 0)
                    {
                        if (name is not null || data.Count > 0)
                        {
                            return new ServerSentEvent(name, id, data);
                        }

                        continue;
                    }

                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        name = line[7..];
                    }
                    else if (line.StartsWith("id: ", StringComparison.Ordinal))
                    {
                        id = line[4..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        data.Add(line[6..]);
                    }
                }

                return null;
            },
            cancellationToken
        );
    }

    // The read itself is cancelled, not abandoned: a read left pending would still hold the reader when the test
    // disposes it.
    private static async Task<T> _WithPatienceAsync<T>(
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        patience.CancelAfter(_Patience);

        try
        {
            return await read(patience.Token);
        }
        catch (Exception exception)
            when (patience.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The stream sent nothing awaited for {_Patience.TotalSeconds} seconds.",
                exception
            );
        }
    }

    /// <summary>Returns the next event, which must be a result, parsed.</summary>
    public async Task<JsonElement> NextResultAsync(CancellationToken cancellationToken)
    {
        var next = await NextEventAsync(cancellationToken);
        next.Should().NotBeNull("the stream should still be open");
        next!.Name.Should().Be("result", "the stream sent: {0}", string.Join('\n', next.Data));

        return next.Json;
    }

    /// <summary>Returns the next event, which must be a failure, parsed.</summary>
    public async Task<JsonElement> NextFailureAsync(CancellationToken cancellationToken)
    {
        var next = await NextEventAsync(cancellationToken);
        next.Should().NotBeNull("the stream should still be open");
        next!.Name.Should().Be("failure", "the stream sent: {0}", string.Join('\n', next.Data));

        return next.Json;
    }

    public void Dispose()
    {
        _reader.Dispose();
        response.Dispose();
    }
}
