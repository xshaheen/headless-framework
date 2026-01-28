# Headless.Sms.Abstractions

Defines the unified interface for SMS sending.

## Problem Solved

Provides a provider-agnostic SMS sending API, enabling consistent SMS functionality across different providers (Twilio, AWS SNS, etc.) without changing application code.

## Key Features

- `ISmsSender` - Core interface for sending SMS
- `SendSingleSmsRequest` - SMS request model
- `SendSingleSmsResponse` - SMS response with status

## Installation

```bash
dotnet add package Headless.Sms.Abstractions
```

## Usage

```csharp
public sealed class OtpService(ISmsSender smsSender)
{
    public async Task SendOtpAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var request = new SendSingleSmsRequest
        {
            To = phoneNumber,
            Message = $"Your verification code is: {code}"
        };

        var response = await smsSender.SendAsync(request, ct);

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"SMS failed: {response.ErrorMessage}");
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
