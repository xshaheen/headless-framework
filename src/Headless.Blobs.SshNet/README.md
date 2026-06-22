# Headless.Blobs.SshNet

SFTP/SSH implementation of the `IBlobStorage` interface for storing files on remote servers via SFTP.

## Problem Solved

Provides blob storage via SFTP/SSH protocol for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange.

## Key Features

- Full `IBlobStorage` implementation using SFTP
- SSH key and password authentication
- Remote directory management
- Metadata support via companion files
- Connection pooling

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
// Key-based authentication: provide PrivateKey as a Stream
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

- Registers `IBlobStorage` as singleton
- Opens SSH/SFTP connections to remote server
