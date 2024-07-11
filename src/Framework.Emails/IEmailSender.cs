using Framework.Emails.Contracts;

namespace Framework.Emails;

/// <summary>Email sender abstraction.</summary>
public interface IEmailSender
{
    ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    );
}
