# Headless.LiveQueries.Endpoints

Turns a Minimal API GET endpoint into a live query: it streams its result over server-sent events or newline-delimited JSON and sends the result again when the data it read changes.

## Why use this package

A screen that must stay current otherwise needs a push channel, a list of which writes refresh which reads, and refetch code on the client. `.WithLiveUpdates()` on the map call replaces all three: an ordinary request still gets the ordinary JSON response, and a streaming request gets the result now and after every announced change. Each re-run binds the handler's arguments again, on a new service scope, and authenticates and authorizes the caller again. Streams are limited per user and per host by default. Invalidation reaches streams in the same process only; read the guide before running more than one instance.

## Install

```bash
dotnet add package Headless.LiveQueries.Endpoints
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Live Queries guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/live-queries.md#headlesslivequeriesendpoints)
