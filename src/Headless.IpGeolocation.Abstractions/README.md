# Headless.IpGeolocation.Abstractions

Defines the provider-agnostic contract for resolving an IP address to a location.

## Why use this package

Services that inject `IIpGeolocator` depend on no geolocation database or vendor. The returned `IpLocation` carries country, subdivision, city, coordinates, time zone, and the autonomous system that owns the address.

## Install

```bash
dotnet add package Headless.IpGeolocation.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [IP geolocation guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/ip-geolocation.md#headlessipgeolocationabstractions)
