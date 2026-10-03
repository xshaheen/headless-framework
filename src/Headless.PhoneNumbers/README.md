# Headless.PhoneNumbers

Phone number formatting, normalization, and validation backed by Google's libphonenumber.

## Why use this package

Adds the libphonenumber-backed operations to the `PhoneNumber` value object from `Headless.Primitives`: national and international formatting, normalization, region lookup, and parsing from international format. It also ships mobile-number validation and FluentValidation rules such as `InternationalPhoneNumber()` and `MobilePhoneNumber()`. It is a separate package so the foundation packages do not carry libphonenumber's metadata.

## Install

```bash
dotnet add package Headless.PhoneNumbers
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Utilities guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#headlessphonenumbers)
