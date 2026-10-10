# Headless.IpGeolocation.MaxMind

MaxMind GeoIP2 and GeoLite2 provider for `IIpGeolocator`, with automatic database updates.

## Why use this package

Looks addresses up in local MaxMind databases (City or Country, plus ASN) and keeps them current. With a MaxMind account ID and license key (free for GeoLite2), a background service checks for new releases, verifies each download against MaxMind's published SHA-256, and swaps the database in without a restart. That keeps an application within the GeoLite license rule to use a new release within 30 days of it. Without a key, the provider reads the files you place in the database directory.

This package ships no MaxMind data. Downloads use your own account, under MaxMind's license terms.

## Install

```bash
dotnet add package Headless.IpGeolocation.MaxMind
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [IP geolocation guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/ip-geolocation.md#headlessipgeolocationmaxmind)
