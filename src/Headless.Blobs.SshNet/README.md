# Headless.Blobs.SshNet

SFTP/SSH implementation of `IBlobStorage` for storing files on remote servers via SFTP.

## Problem Solved

Provides blob storage via SFTP/SSH for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange with systems that do not expose a cloud API.

## Key Features

- Full `IBlobStorage` implementation using SFTP.
- SSH key and password authentication.
- Remote directory management.
- Metadata support via companion files.
- Connection pooling (`SftpClientPool`); each store owns its own pool.

## Installation

```bash
dotnet add package Headless.Blobs.SshNet
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
        options.ConnectionString = "sftp://user:password@sftp.example.com:22/home/user/uploads"));
```

## Configuration

### appsettings.json

```json
{
  "SftpBlob": {
    "ConnectionString": "sftp://user:password@sftp.example.com:22/home/user/uploads"
  }
}
```

### SSH Key Authentication

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
    {
        options.ConnectionString = "sftp://user@sftp.example.com:22/home/user/uploads";
        options.PrivateKey = File.OpenRead("/path/to/key");
        options.PrivateKeyPassPhrase = "optional-passphrase"; // nullable
    }));
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`
- `SSH.NET`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseSsh(...))` or `AddNamed("name", i => i.UseSsh(...))`:

- Default (`UseSsh`): registers `SftpClientPool` as unkeyed singleton; registers `IBlobStorage` as unkeyed singleton.
- Named (`AddNamed ... UseSsh`): registers `SftpClientPool` as keyed singleton (`name`); registers `IBlobStorage` as keyed singleton (`name`). Each named store owns its own pool instance bound to its named options.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for SshNet stores.
