# Headless.Sms.Abstractions

Defines the unified interface and message contract for SMS sending.

## Problem Solved

Provides a provider-agnostic SMS sending API so application code stays decoupled from the underlying gateway (Twilio, AWS SNS, Cequens, etc.). Provider selection is a DI registration concern only.

## Key Features

- `ISmsSender` — single method `SendAsync(SendSingleSmsRequest, CancellationToken) : ValueTask<SendSingleSmsResponse>`.
- `SendSingleSmsRequest` — message contract with `Destinations` (list of `SmsRequestDestination`), `Text`, optional `MessageId`, and optional `Properties`.
- `SmsRequestDestination(int Code, string Number)` — phone number with separate country calling code and subscriber number.
- `SendSingleSmsResponse` — closed result type; `Success` (bool) and `FailureError` (string? non-null on failure).
- Never throws for provider errors — only `OperationCanceledException` propagates.

## Installation

```bash
dotnet add package Headless.Sms.Abstractions
```

## Quick Start

```csharp
public sealed class OtpService(ISmsSender smsSender)
{
    public async Task SendOtpAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var request = new SendSingleSmsRequest
        {
            Destinations = [new SmsRequestDestination(20, phoneNumber)], // 20 = Egypt calling code
            Text = $"Your verification code is: {code}"
        };

        var response = await smsSender.SendAsync(request, ct);

        if (!response.Success)
        {
            throw new InvalidOperationException($"SMS failed: {response.FailureError}");
        }
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
