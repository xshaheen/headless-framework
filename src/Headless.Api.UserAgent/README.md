# Headless.Api.UserAgent

User-Agent parsing for Headless APIs, backed by DeviceDetector.NET.

## Why use this package

Implements `IUserAgentParser`, so `IWebClientInfoProvider.DeviceInfo` and `UserAgentInfo` report the device, OS, client, and bot behind a request. DeviceDetector.NET ships a large regex database, so this lives in its own package: without it, device information stays empty.

## Install

```bash
dotnet add package Headless.Api.UserAgent
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiuseragent)
