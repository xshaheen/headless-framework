# Headless.Blobs.Aws

AWS S3 implementation of the `IBlobStorage` interface for storing files in Amazon S3.

## Problem Solved

Provides seamless integration with AWS S3 for blob storage using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for AWS S3
- Bulk upload/delete with optimized batching
- Automatic path normalization for S3 object keys
- Metadata support
- Pre-signed URL generation capability
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
builder.Services.AddAwsS3BlobStorage(awsOptions, options =>
{
    options.BucketName = "my-bucket";
});

// Option 2: Manual configuration
builder.Services.AddAwsS3BlobStorage(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("access-key", "secret-key")
}, options =>
{
    options.BucketName = "my-bucket";
});
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
options.BucketName = "my-bucket";
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.BuildingBlocks`
- `Headless.Hosting`
- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonS3` if not already registered
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
