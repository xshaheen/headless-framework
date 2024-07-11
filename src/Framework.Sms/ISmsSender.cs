namespace Framework.Sms;

public interface ISmsSender
{
    ValueTask<SendSingleSmsResponse> SendAsync(SendSingleSmsRequest request, CancellationToken token = default);
}
