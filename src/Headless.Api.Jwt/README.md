# Headless.Api.Jwt

JWT issuing, parsing, and bearer authentication for Headless APIs.

## Why use this package

Issues and validates HMAC-signed, optionally encrypted JWTs through `IJwtTokenFactory`, and adds a bearer scheme that accepts exactly the tokens the factory issues. Apps that authenticate another way do not need it.

## Install

```bash
dotnet add package Headless.Api.Jwt
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapijwt)
