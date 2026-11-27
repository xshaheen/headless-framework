# Headless.DevInbox.Dashboard

The Dev inbox module of the Headless dashboard: the emails, SMS, and push notifications a Development host captured instead of sending.

## Why use this package

A developer reads what the app tried to send without a debugger: an email as it renders, with its inline images, text, headers, attachments, and `.eml`; an SMS's encoding and part count; a push's payload and whether a real provider would reject it. They can delete one capture or clear a channel. The module reads the capture stores of `Headless.Emails.Dev`, `Headless.Sms.Dev`, and `Headless.PushNotifications.Dev`, and a host that is not in Development refuses to start with it.

## Install

```bash
dotnet add package Headless.DevInbox.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards/dev-inbox-module.md#headlessdevinboxdashboard)
