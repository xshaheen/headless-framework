# Framework.Emails.Core

Core utilities and MimeKit integration for email implementations.

## Problem Solved

Provides shared conversion logic to bridge framework email contracts with MimeKit, eliminating duplication across email provider implementations.

## Key Features

- `MimeMessage` conversion from `SendSingleEmailRequest`
- Address mapping (To, From, Cc, Bcc)
- Body building (Text/HTML)
- Attachment handling

## Installation

```bash
dotnet add package Framework.Emails.Core
```

## Usage

```csharp
// Convert framework request to MimeKit message
MimeMessage message = await request.ConvertToMimeMessageAsync(cancellationToken);
```

## Configuration

No configuration required.

## Dependencies

- `Framework.Emails.Abstractions`
- `Framework.Hosting`
- `MailKit`

## Side Effects

None. This is a utility package.
