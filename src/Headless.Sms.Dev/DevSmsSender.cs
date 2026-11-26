// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Context;
using Headless.Sms.Dev.Internal;

namespace Headless.Sms.Dev;

/// <summary>
/// Captures each message in <see cref="ISmsCaptureStore"/>, and appends a plain-text copy to a file when
/// <see cref="DevSmsOptions.FilePath"/> is set, instead of delivering it.
/// </summary>
/// <remarks>
/// A file that cannot be written returns a failed response, as a real provider reports a transport fault, and the
/// capture records the error. No SMS is ever sent.
/// </remarks>
internal sealed class DevSmsSender(
    DevSmsOptions options,
    SmsCaptureStore store,
    string senderName = "",
    ICorrelationIdProvider? correlationIdProvider = null
) : ISmsSender, IBulkSmsSender, IDisposable
{
    private const string _Separator = "--------------------";
    private readonly DevSmsOptions _options = Argument.IsNotNull(options);
    private readonly SmsCaptureStore _store = Argument.IsNotNull(store);
    private readonly string _senderName = Argument.IsNotNull(senderName);
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

        return await _SendAsync(
                request.MessageId,
                [request.Destination],
                isBulk: false,
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
        Argument.IsNotNull(request.Destinations);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        cancellationToken.ThrowIfCancellationRequested();

        var outcome = await _SendAsync(
                request.MessageId,
                [.. request.Destinations],
                isBulk: true,
                request.Text,
                request.Properties,
                cancellationToken
            )
            .ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }

    private async ValueTask<SendSingleSmsResponse> _SendAsync(
        string? messageId,
        SmsRequestDestination[] destinations,
        bool isBulk,
        string text,
        IDictionary<string, object>? properties,
        CancellationToken cancellationToken
    )
    {
        // Copy the caller's dictionary so a later change to it leaves the capture intact.
        var propertiesSnapshot = properties is null
            ? null
            : new Dictionary<string, object>(properties, StringComparer.Ordinal);

        if (_options.FilePath is { } filePath)
        {
            try
            {
                await _AppendToFileAsync(filePath, messageId, destinations, text, properties, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _Capture(messageId, destinations, isBulk, text, propertiesSnapshot, e.Message);

                return SendSingleSmsResponse.Failed(e.Message);
            }
        }

        var captured = _Capture(messageId, destinations, isBulk, text, propertiesSnapshot, failureError: null);

        return SendSingleSmsResponse.Succeeded(captured.Id.ToString());
    }

    private CapturedSms _Capture(
        string? messageId,
        SmsRequestDestination[] destinations,
        bool isBulk,
        string text,
        IReadOnlyDictionary<string, object>? properties,
        string? failureError
    )
    {
        return _store.Add(
            new CapturedSms
            {
                SenderName = _senderName,
                Destinations = destinations,
                IsBulk = isBulk,
                Text = text,
                Segmentation = SmsSegmentation.Measure(text),
                MessageId = messageId,
                Properties = properties,
                FailureError = failureError,
                TraceId = Activity.Current?.TraceId.ToString(),
                CorrelationId = correlationIdProvider?.CorrelationId,
            }
        );
    }

    private async Task _AppendToFileAsync(
        string filePath,
        string? messageId,
        SmsRequestDestination[] destinations,
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

        sb.Append("To: ").AppendLine(destinations.JoinAsString(", "));
        sb.Append("Text: ").AppendLine(text);

        if (properties is not null)
        {
            sb.Append("Properties: ").AppendLine(JsonSerializer.Serialize(properties, _JsonOptions));
        }

        sb.AppendLine(_Separator);

        // The sender is registered as a singleton and shares one file across concurrent callers;
        // serialize the appends so they do not interleave.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await File.AppendAllTextAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
