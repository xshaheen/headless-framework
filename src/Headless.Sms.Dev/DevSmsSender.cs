// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Dev.Internals;

namespace Headless.Sms.Dev;

internal sealed class DevSmsSender(string filePath) : ISmsSender, IBulkSmsSender, IDisposable
{
    private const string _Separator = "--------------------";
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        TypeInfoResolver = DevSmsJsonSerializerContext.Default,
    };

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        cancellationToken.ThrowIfCancellationRequested();

        return await _WriteAsync(
                request.MessageId,
                request.Destination.ToString(),
                request.Text,
                request.Properties,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        cancellationToken.ThrowIfCancellationRequested();

        var outcome = await _WriteAsync(
                request.MessageId,
                request.Destinations.JoinAsString(", "),
                request.Text,
                request.Properties,
                cancellationToken
            )
            .ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    private async ValueTask<SendSingleSmsResponse> _WriteAsync(
        string? messageId,
        string recipients,
        string text,
        IDictionary<string, object>? properties,
        CancellationToken cancellationToken
    )
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(messageId))
        {
            sb.Append("MessageId: ").AppendLine(messageId);
        }

        sb.Append("To: ").AppendLine(recipients);
        sb.Append("Text: ").AppendLine(text);

        if (properties is not null)
        {
            sb.Append("Properties: ").AppendLine(JsonSerializer.Serialize(properties, _JsonOptions));
        }

        sb.AppendLine(_Separator);

        // Singleton sender shares one file across concurrent callers; serialize the appends.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await File.AppendAllTextAsync(_filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            return SendSingleSmsResponse.Failed(e.Message);
        }
        finally
        {
            _writeLock.Release();
        }

        return SendSingleSmsResponse.Succeeded();
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
