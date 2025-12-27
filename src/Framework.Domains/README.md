# Framework.Domains

The `Framework.Domains` package provides the core building blocks and abstractions for implementing the Domain Layer of your application, following **Domain-Driven Design (DDD)** principles. It includes base classes and interfaces for entities, value objects, domain events, auditing, and messaging.

## Table of Contents

-   [Domain Primitives](#domain-primitives)
-   [Audit Logs](#audit-logs)
-   [Events](#events)
-   [Messages](#messages)
-   [Concurrency](#concurrency)
-   [Multi-Tenancy](#multi-tenancy)

## Domain Primitives

Located in `Domain/`, this namespace provides the fundamental contracts for DDD.

-   **`IEntity<TId>`**: Defines an entity with a unique identifier.
-   **`IAggregateRoot<TId>`**: Marker interface for Aggregate Roots.
-   **`ValueObject`**: Base class for Value Objects, providing equality implementations based on their components.

## Audit Logs

Located in `AuditLogs/`, this namespace offers a rich set of base classes to automatically handle entity auditing. These classes enforce consistent audit properties (e.g., `DateCreated`, `CreatedBy`, `DateUpdated`, `UpdatedBy`, `IsDeleted`, etc.).

The framework provides combinations of audit capabilities:

-   **Create**: `EntityWithCreateAudit`
-   **Create + Update**: `EntityWithCreateUpdateAudit`
-   **Create + Update + Delete (Soft Delete)**: `EntityWithCreateUpdateDeleteAudit`
-   **Create + Update + Suspend**: `EntityWithCreateUpdateSuspendAudit`

Generic variants allow you to specify the type of the User identifier and the User entity itself (e.g., `EntityWithCreateAudit<TId, TUserId, TUser>`).

## Events

Located in `Events/`, this namespace defines standard domain event data structures.

-   **`EntityEventData<T>`**: Base class for entity-related events.
-   **Lifecycle Events**:
    -   `EntityCreatedEventData<T>`
    -   `EntityUpdatedEventData<T>`
    -   `EntityDeletedEventData<T>`
    -   `EntityChangedEventData<T>` (Generic change)

## Messages

Located in `Messages/`, this namespace handles both local (in-process) and distributed messaging abstractions.

-   **Distributed**: Interfaces and base classes for messages sent over a service bus (`IDistributedMessage`, `DistributedMessage`).
-   **Local**: Interfaces for in-memory domain events (`ILocalMessage`).

## Concurrency

Located in `Concurrency/`, this namespace provides standard interfaces for handling optimistic concurrency.

-   **`IHasConcurrencyStamp`**: For tracking entity versions/stamps.
-   **`IHasETag`**: For standard ETag support.

## Multi-Tenancy

Located in `Tenants/`, this namespace provides support for multi-tenant applications.

-   **`IMultiTenant`**: Interface to mark entities that belong to a specific tenant.
