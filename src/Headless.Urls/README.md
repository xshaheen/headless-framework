# Headless.Urls

Fluent, allocation-conscious URL building and parsing.

## Problem Solved

Assembling and manipulating URLs with raw string concatenation is error-prone: double slashes, missing
encoding, duplicated query parameters, and fragile parsing. `Headless.Urls` provides a mutable `Url` builder
plus an ordered, multi-value `QueryParamCollection` so path segments, query parameters, and fragments can be
composed and edited without hand-rolling encoding rules.

## Design Notes

This subsystem is **derived from [Flurl](https://github.com/tmenier/Flurl)** (MIT), specifically Flurl's
`Flurl.Url` URL-building API. It is packaged separately from `Headless.Extensions` so consumers who only need
URL building do not pull the full base library, and so the third-party derivation is attributed at the package
boundary. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the full MIT attribution.

`NullValueHandling` has explicit backing values (`NameOnly = 0`, `Remove = 1`, `Ignore = 2`) — they are part of
the public contract and must not be reordered.

## Key Features

- `Url` - mutable builder with `Scheme` / `Host` / `Path` / `Query` / `Fragment` accessors.
- `Url.Parse(string)` / `Url.ParseQueryParams(string)` / `Url.ParsePathSegments(string)` - parsing entry points.
- `AppendPathSegment` / `AppendPathSegments` / `RemovePathSegment` - path composition.
- `SetQueryParam` / `AppendQueryParam` / `RemoveQueryParam` with `NullValueHandling` control.
- `QueryParamCollection` - ordered, duplicate-preserving query parameter store.
- `Clone`, `Reset`, `ResetToRoot` for reuse.

## Installation

```bash
dotnet add package Headless.Urls
```

## Quick Start

```csharp
using Headless.Urls;

var url = Url.Parse("https://api.example.com")
    .AppendPathSegment("v1")
    .AppendPathSegments("users", "42")
    .SetQueryParam("include", "profile")
    .SetQueryParam("fields", null, NullValueHandling.Remove);

string result = url.ToString();
// https://api.example.com/v1/users/42?include=profile
```

## Configuration

None. `Url` is constructed directly; no DI registration or options are involved.

## Dependencies

- `Headless.Checks` - argument validation.

## Side Effects

None. The types are pure value/builder types with no DI registration or ambient state.
