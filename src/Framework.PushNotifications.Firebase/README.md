# Framework.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation for push notifications.

## Problem Solved

Provides push notification delivery via Firebase Cloud Messaging using the `IPushNotificationService` abstraction for production mobile app notifications.

## Key Features

- `GoogleCloudMessagingPushNotificationService` - FCM implementation
- Single device and multicast support
- Custom data payload support
- Automatic token validation
- Detailed error logging

## Installation

```bash
dotnet add package Framework.PushNotifications.Firebase
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFirebasePushNotificationService(options =>
{
    options.CredentialsPath = "path/to/service-account.json";
    // Or use environment variable: GOOGLE_APPLICATION_CREDENTIALS
});
```

## Configuration

### Options

```csharp
services.AddFirebasePushNotificationService(options =>
{
    options.CredentialsPath = "firebase-credentials.json";
    options.ProjectId = "your-project-id"; // Optional, read from credentials
});
```

### appsettings.json

```json
{
  "Firebase": {
    "CredentialsPath": "firebase-credentials.json"
  }
}
```

## Dependencies

- `Framework.PushNotifications.Abstractions`
- `FirebaseAdmin`

## Side Effects

- Registers `IPushNotificationService` as singleton
- Initializes Firebase Admin SDK
