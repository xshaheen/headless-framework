# Headless.IpGeolocation

Registration entry point for Headless IP geolocation providers.

## Why use this package

Provides `AddHeadlessIpGeolocation`, which registers `IIpGeolocator` through exactly one provider and refuses a missing or duplicate provider at startup instead of at the first lookup.

## Install

```bash
dotnet add package Headless.IpGeolocation
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [IP geolocation guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/ip-geolocation.md#headlessipgeolocation)
