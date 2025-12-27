# Framework.Sms.Cequens

This package implements SMS sending capabilities using Cequens.

## Features

-   **CequensSmsSender**: The specialized sender for Cequens.
-   **Configuration**: `CequensSmsOptions` for API settings.
-   **Registration**: `AddCequensExtensions` simplifies startup configuration.

## Usage

### Configuration

Populate `CequensSmsOptions` from your application settings.

### Registration

```csharp
services.AddCequensSms(configuration);
```

### Operations

Use `ISmsSender` to dispatch generic SMS requests which are then translated to Cequens specific API calls.
