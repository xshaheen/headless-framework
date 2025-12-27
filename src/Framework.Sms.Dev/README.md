# Framework.Sms.Dev

This package contains SMS implementations useful for development and testing environments.

## Features

-   **DevSmsSender**: A sender implementation that might log messages to the console or a file instead of sending them, or simulated behavior.
-   **NoopSmsSender**: A "No Operation" sender that does nothing, useful for environments where SMS sending should be disabled.
-   **Extensions**: Helper methods in `AddSmsExtensions` to register these providers.

## Usage

Use these providers in non-production environments to avoid incurring costs or spamming real phone numbers.

### Registration

```csharp
if (env.IsDevelopment())
{
    services.AddDevSms(); // or AddNoopSms()
}
```
