# Headless Ambient Transactions Abstractions

Coordinates provider-owned database transactions with deferred work that must run only after commit.

The package is messaging-agnostic. Messaging, jobs, and other subsystems attach their own buffers to an `IAmbientTransaction` and register commit drains through `RegisterCommitWork`.
