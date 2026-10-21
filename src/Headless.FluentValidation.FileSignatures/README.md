# Headless.FluentValidation.FileSignatures

FluentValidation rule that checks a file's binary signature (magic bytes) with FileSignatures.

## Why use this package

Lets a command, message, or job reject a file whose content does not match an accepted format, without depending on ASP.NET Core. It is separate from `Headless.FluentValidation` so packages that never handle files do not carry FileSignatures and OpenMcdf.

## Install

```bash
dotnet add package Headless.FluentValidation.FileSignatures
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Utilities guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#headlessfluentvalidationfilesignatures)
