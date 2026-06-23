# Headless.Blobs.Aws

AWS S3 implementation of the `IBlobStorage` interface for storing files in Amazon S3.

## Problem Solved

Provides AWS S3 integration for blob storage using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for AWS S3
- Bulk upload/delete with optimized batching
- Two-tier name normalization: the bucket name is normalized to S3 rules; object-key path segments are validated and preserved
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage`
- Opt-in, cached bucket auto-create (`AutoCreateContainer`)
- Integration with AWS SDK configuration

## Installation

```bash
dotnet add package Headless.Blobs.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: Use configuration-based AWS options
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddAwsS3BlobStorage(awsOptions);

// Option 2: Manual configuration
builder.Services.AddAwsS3BlobStorage(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("access-key", "secret-key")
});
```

Buckets and keys are passed per operation, not configured at registration:

```csharp
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);
```

## Configuration

### appsettings.json

```json
{
  "AWS": {
    "Region": "us-east-1",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key"
  }
}
```

### Options

```csharp
options.AutoCreateContainer = true; // create buckets on upload/copy (default true)
options.CannedAcl = S3CannedACL.Private;
options.UseChunkEncoding = true;
options.DisablePayloadSigning = false;
options.MaxBulkParallelism = 10;
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonS3` if not already registered
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
