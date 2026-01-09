# Framework.Blobs.SshNet

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
dotnet add package Framework.Blobs.SshNet
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSshNetBlobStorage(options =>
{
    options.Host = "sftp.example.com";
    options.Port = 22;
    options.Username = "user";
    options.Password = "password"; // Or use PrivateKeyPath
    options.BasePath = "/home/user/uploads";
});
```

## Configuration

### appsettings.json

```json
{
  "SftpBlob": {
    "Host": "sftp.example.com",
    "Port": 22,
    "Username": "user",
    "Password": "secret",
    "BasePath": "/home/user/uploads"
  }
}
```

### SSH Key Authentication

```json
{
  "SftpBlob": {
    "Host": "sftp.example.com",
    "Username": "user",
    "PrivateKeyPath": "/path/to/key",
    "PrivateKeyPassphrase": "optional-passphrase"
  }
}
```

## Dependencies

- `Framework.Blobs.Abstractions`
- `Framework.Hosting`
- `SSH.NET`

## Side Effects

- Registers `IBlobStorage` as singleton
- Opens SSH/SFTP connections to remote server
