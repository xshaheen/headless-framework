# Headless.Imaging.Core

Core image processing implementation with contributor-based extensibility.

## Problem Solved

Provides the orchestration layer for image processing, delegating to registered contributors (like ImageSharp) for actual processing while maintaining a unified API.

## Key Features

- `ImageResizer` - Orchestrates resize operations across contributors
- `ImageCompressor` - Orchestrates compression operations
- Contributor pattern for extensibility (`IImageResizerContributor`, `IImageCompressorContributor`)
- Builder pattern for fluent registration
- Options validation

## Installation

```bash
dotnet add package Headless.Imaging.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddImaging(options =>
    {
        options.DefaultQuality = 85;
    })
    .AddImageSharpContributors(); // From Headless.Imaging.ImageSharp
```

## Configuration

### Options

```csharp
services.AddImaging(options =>
{
    options.DefaultQuality = 85;        // Default compression quality
    options.MaxWidth = 4096;            // Maximum allowed width
    options.MaxHeight = 4096;           // Maximum allowed height
});
```

## Dependencies

- `Headless.Imaging.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IImageResizer` as singleton
- Registers `IImageCompressor` as singleton
