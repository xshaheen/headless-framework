# Headless Ambient Transactions InMemory

In-process ambient transaction provider for tests, local development, and single-instance flows.

This provider coordinates commit/rollback callbacks but does not provide database durability or cross-process isolation.
