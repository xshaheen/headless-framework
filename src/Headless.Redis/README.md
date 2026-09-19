# Headless.Redis

Redis utilities and Lua script management for StackExchange.Redis.

## Why use this package

Provides Redis helper extensions plus definition-first Lua script loading/execution for StackExchange.Redis. Scripts are loaded on demand by default; provider packages can warm their own script bundles through hosted initializers.

## Install

```bash
dotnet add package Headless.Redis
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Utilities guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#headlessredis)
