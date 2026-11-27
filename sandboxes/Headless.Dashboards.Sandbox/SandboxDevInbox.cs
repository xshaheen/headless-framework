// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Emails;
using Headless.PushNotifications;
using Headless.Sms;

namespace Headless.Dashboards.Sandbox;

/// <summary>
/// The Dev inbox wiring and its scenarios: the development senders capture what the app sends, and the module shows
/// them. It runs only when the sandbox runs as Development, which <c>make up</c> sets, because the module refuses to
/// start anywhere else.
/// </summary>
public static class SandboxDevInbox
{
    /// <summary>The named SMS sender whose every send fails, so the inbox shows a failed SMS.</summary>
    public const string FailingSmsSender = "failing";

    // Large enough to keep the inline logo and the PDF, small enough that the 300 KB scan is omitted.
    private const int _MaxCapturedAttachmentBytes = 256 * 1024;

    // A 1x1 transparent PNG: the inline image the HTML email references by cid:.
    private static readonly byte[] _Logo = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
    );

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        ["dev-inbox"] =
            "Captured emails, SMS, and push notifications: an HTML email with an inline logo and a PDF, one whose attachment is over the capture limit, a failed email, a GSM-7 code SMS, a multi-part Unicode SMS, a failed SMS, a bulk SMS, and normal, oversized, and data-only pushes.",
        ["dev-inbox-send"] = "One more captured email, to watch it arrive in an open Emails list.",
    };

    /// <summary>Registers the development senders and the Dev inbox module, only in Development.</summary>
    public static void Add(WebApplicationBuilder builder, HeadlessDashboardSetupBuilder dashboard)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return;
        }

        builder.Services.AddHeadlessEmails(setup =>
            setup.UseDevelopment(options => options.MaxCapturedAttachmentBytes = _MaxCapturedAttachmentBytes)
        );
        builder.Services.AddHeadlessSms(setup =>
        {
            setup.UseDevelopment();
            // The sender appends a copy to its file path, and a directory cannot be appended to, so every send through
            // it is captured as failed while the default sender stays healthy.
            setup.AddNamed(FailingSmsSender, instance => instance.UseDevelopment(AppContext.BaseDirectory));
        });
        builder.Services.AddHeadlessPushNotifications(setup => setup.UseDevelopment());
        dashboard.AddDevInbox();
    }

    /// <summary>Runs a Dev inbox scenario; a host that is not in Development captures nothing, so it creates nothing.</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(
        string name,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        if (services.GetService<IEmailSender>() is not { } emails)
        {
            return [];
        }

        if (string.Equals(name, "dev-inbox-send", StringComparison.Ordinal))
        {
            await emails.SendAsync(
                _Email("Your order has shipped", text: "Order 9601 is on its way."),
                cancellationToken
            );

            return ["email:order-shipped"];
        }

        await _SendEmailsAsync(emails, cancellationToken);
        await _SendSmsAsync(services, cancellationToken);
        await _SendPushesAsync(services.GetRequiredService<IPushNotificationService>(), cancellationToken);

        return
        [
            "email:welcome",
            "email:large-scan",
            "email:failed",
            "sms:code",
            "sms:unicode",
            "sms:failed",
            "sms:bulk",
            "push:normal",
            "push:oversized",
            "push:data-only",
        ];
    }

    private static async Task _SendEmailsAsync(IEmailSender emails, CancellationToken cancellationToken)
    {
        await emails.SendAsync(
            _Email(
                "Welcome to Headless",
                html: """
                <html><body style="font-family: sans-serif; background: #f4f4f7; padding: 24px">
                  <div style="max-width: 480px; margin: auto; background: #ffffff; border-radius: 8px; padding: 24px">
                    <img src="cid:logo.png" alt="Logo" width="48" height="48">
                    <h1 style="color: #4f46e5">Welcome aboard, Ada</h1>
                    <p>Confirm your email to finish signing up.</p>
                    <a href="https://example.com/confirm?token=sandbox" style="background: #4f46e5; color: #fff; padding: 8px 16px; border-radius: 4px; text-decoration: none">Confirm email</a>
                  </div>
                </body></html>
                """,
                text: "Welcome aboard, Ada. Confirm your email: https://example.com/confirm?token=sandbox",
                attachments:
                [
                    new()
                    {
                        Name = "logo.png",
                        File = _Logo,
                        ContentType = "image/png",
                    },
                    new()
                    {
                        Name = "terms.pdf",
                        File = "%PDF-1.4 sandbox terms"u8.ToArray(),
                        ContentType = "application/pdf",
                    },
                ]
            ),
            cancellationToken
        );
        await emails.SendAsync(
            _Email(
                "Your scanned receipt",
                text: "The scan of your receipt is attached.",
                attachments:
                [
                    new()
                    {
                        Name = "receipt-scan.tiff",
                        File = new byte[300 * 1024],
                        ContentType = "image/tiff",
                    },
                ]
            ),
            cancellationToken
        );

        try
        {
            // No body: the development sender captures it as failed, then refuses the send.
            await emails.SendAsync(_Email("Password reset", text: null), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The capture holds the failure; the refusal is what the app would see.
        }
    }

    private static async Task _SendSmsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sms = services.GetRequiredService<ISmsSender>();
        var destination = new SmsRequestDestination(20, "1000000001");
        await sms.SendAsync(
            new() { Destination = destination, Text = "Your Headless code is 482913." },
            cancellationToken
        );
        await sms.SendAsync(
            new()
            {
                Destination = destination,
                Text =
                    "مرحبًا بك في Headless. رمز التحقق الخاص بك هو 482913، وهو صالح لمدة عشر دقائق فقط. لا تشاركه مع أي شخص.",
            },
            cancellationToken
        );
        await services
            .GetRequiredKeyedService<ISmsSender>(FailingSmsSender)
            .SendAsync(
                new() { Destination = destination, Text = "Your order 9602 is ready for pickup." },
                cancellationToken
            );
        await services
            .GetRequiredService<IBulkSmsSender>()
            .SendBulkAsync(
                new()
                {
                    Destinations = [destination, new(20, "1000000002"), new(20, "1000000003")],
                    Text = "Flash sale: 20% off everything today.",
                },
                cancellationToken
            );
    }

    private static async Task _SendPushesAsync(IPushNotificationService push, CancellationToken cancellationToken)
    {
        await push.SendToDeviceAsync(
            "device-token-ada-iphone",
            new()
            {
                Title = "Order shipped",
                Body = "Order 9601 is on its way.",
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["orderId"] = "9601" },
            },
            cancellationToken
        );
        await push.SendToDeviceAsync(
            "device-token-ada-iphone",
            new()
            {
                Title = "Your weekly report",
                Body = "Everything that happened this week.",
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["report"] = new('x', 4_500) },
            },
            cancellationToken
        );
        await push.SendToDeviceAsync(
            "device-token-ada-pixel",
            new() { Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "inbox" } },
            cancellationToken
        );
    }

    private static SendSingleEmailRequest _Email(
        string subject,
        string? html = null,
        string? text = null,
        IReadOnlyList<EmailRequestAttachment>? attachments = null
    ) =>
        new()
        {
            From = new EmailRequestAddress("hello@example.com", "Headless Sandbox"),
            Destination = new EmailRequestDestination { ToAddresses = [new("ada@example.com", "Ada Lovelace")] },
            Subject = subject,
            MessageHtml = html,
            MessageText = text,
            Attachments = attachments ?? [],
        };
}
