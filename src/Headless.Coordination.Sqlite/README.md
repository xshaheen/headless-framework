# Headless.Coordination.Sqlite

Stores coordination membership in a SQLite file, with liveness judged by the database clock.

## Why use this package

Provides an authoritative membership provider for several processes on one host that share a SQLite file, such as an embedded or edge deployment. SQLite locking does not work over a network file system, so every node must run on the host that holds the file.

## Install

```bash
dotnet add package Headless.Coordination.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Coordination guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/coordination.md#headlesscoordinationsqlite)
