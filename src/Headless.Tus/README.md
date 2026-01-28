# Headless.Tus

Core TUS protocol utilities and extensions.

## Problem Solved

Provides shared utilities and base functionality for TUS (resumable file upload) protocol implementations, building on the tusdotnet library.

## Key Features

- Shared TUS protocol utilities
- Base types for TUS stores
- File metadata handling
- Extension method helpers

## Installation

```bash
dotnet add package Headless.Tus
```

## Usage

This is a base package typically used by specific TUS store implementations (Azure, local filesystem). See `Headless.Tus.Azure` for a complete implementation.

## Configuration

No configuration required.

## Dependencies

- `tusdotnet`

## Side Effects

None.
