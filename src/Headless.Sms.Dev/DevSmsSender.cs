// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Dev.Internals;

namespace Headless.Sms.Dev;

public sealed class DevSmsSender(string filePath) : ISmsSender, IDisposable
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
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(request.MessageId))
        {
            sb.Append("MessageId: ").AppendLine(request.MessageId);
        }

        sb.Append("To: ").AppendLine(request.Destinations.JoinAsString(", "));
        sb.Append("Text: ").AppendLine(request.Text);

        if (request.Properties is not null)
        {
            sb.Append("Properties: ").AppendLine(JsonSerializer.Serialize(request.Properties, _JsonOptions));
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
