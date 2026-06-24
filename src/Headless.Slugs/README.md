# Headless.Slugs

URL-friendly slug generation from text.

## Problem Solved

Converts arbitrary text into URL-safe slugs with proper Unicode normalization, configurable separators, character replacements, and length limits for SEO-friendly URLs.

## Key Features

- `Slug.Create()` - Static slug generation method
- Unicode normalization (NFC/NFD)
- Configurable separator character
- Character replacement rules
- Maximum length enforcement
- Case transformation options
- Handles non-ASCII characters properly

## Installation

```bash
dotnet add package Headless.Slugs
```

## Quick Start

### Basic Usage

```csharp
var slug = Slug.Create("Hello World!");

// Result: "hello-world"

var slug2 = Slug.Create("مرحبا بالعالم");
// Result: "مرحبا-بالعالم"
```

### Custom Options

```csharp
var options = new SlugOptions
{
    Separator = "_",
    MaximumLength = 50,
    CasingTransformation = CasingTransformation.ToLowerCase,
    CanEndWithSeparator = false,
};

var slug = Slug.Create("Long Title That Needs Truncation", options);
```

### Character Replacements

```csharp
var options = new SlugOptions
{
    Replacements =
    {
        ["&"] = "and",
        ["@"] = "at",
        ["+"] = "plus",
    },
};

var slug = Slug.Create("Tom & Jerry @ Home", options);
// Result: "tom-and-jerry-at-home"
```

## Configuration

```csharp
var options = new SlugOptions
{
    Separator = "-", // Default: "-"
    MaximumLength = 100, // Default: 80
    CanEndWithSeparator = false, // Default: false
    CasingTransformation = CasingTransformation.ToLowerCase,
};
```

## Dependencies

None.

## Side Effects

None.
