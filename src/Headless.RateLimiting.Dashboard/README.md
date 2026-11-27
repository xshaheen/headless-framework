# Headless.RateLimiting.Dashboard

The Rate limits module of the Headless dashboard: the attempt-limit purposes an app declares, a lookup of one subject's attempts, and a confirmed, reasoned reset of a lockout.

## Why use this package

Support staff need to see why a user is locked out of sign-in, an OTP, or a password reset, and sometimes lift it. Writing that screen per app tends to leak the email or phone number into URLs and logs. This module looks a subject up through a POST, shows its fingerprint instead of the subject, and resets through a dashboard command whose operator-log entry names the purpose and fingerprint only. It is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in.

## Install

```bash
dotnet add package Headless.RateLimiting.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Rate Limiting guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/rate-limiting.md#headlessratelimitingdashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards/rate-limiting-module.md#rate-limits-module)
