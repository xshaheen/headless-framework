# Headless.Orm.Couchbase

Couchbase integration with bucket context and cluster management.

## Problem Solved

Provides a structured approach to Couchbase database access with bucket contexts, document sets, and cluster management utilities for .NET applications.

## Key Features

- `CouchbaseBucketContext` - Base context for bucket operations
- Document set extensions for CRUD operations
- Cluster options and transaction configuration providers
- Eventing functions seeder
- Collection management via assembly scanning
- Bucket context initialization

## Installation

```bash
dotnet add package Headless.Orm.Couchbase
```

## Quick Start

```csharp
public class AppBucketContext : CouchbaseBucketContext
{
    public AppBucketContext(IBucket bucket) : base(bucket) { }

    public DocumentSet<Product> Products => GetDocumentSet<Product>("products");
}

// Registration
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCouchbase(options =>
{
    options.ConnectionString = "couchbase://localhost";
    options.UserName = "admin";
    options.Password = "password";
});
```

## Configuration

### Cluster Options

```csharp
services.AddSingleton<ICouchbaseClusterOptionsProvider, MyClusterOptionsProvider>();
```

## Dependencies

- `CouchbaseNetClient`
- `Headless.BuildingBlocks`

## Side Effects

- Registers bucket context services
- May register eventing functions via seeder
