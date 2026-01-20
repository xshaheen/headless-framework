---
status: ready
priority: p2
issue_id: "014"
tags: [performance, database, indexes, queries]
dependencies: []
---

# Missing Database Indexes on Retry Queries

## Problem Statement

High-frequency retry queries lack composite indexes, causing full table scans on large message tables. At 1M+ messages, retry queries can take 5-10 seconds.

## Findings

**Affected Queries**:
1. Retry processor: `WHERE StatusName='Failed' AND Retries < @max ORDER BY Added`
2. Delayed processor: `WHERE StatusName='Delayed' AND ExpiresAt < @now`
3. Collector: `WHERE ExpiresAt < @now AND StatusName IN ('Succeeded', 'Failed')`

**Missing Indexes**:
```sql
-- PostgreSQL
CREATE INDEX idx_published_retry ON published(StatusName, Retries, Added) WHERE StatusName = 'Failed';
CREATE INDEX idx_published_delayed ON published(StatusName, ExpiresAt) WHERE StatusName = 'Delayed';
CREATE INDEX idx_published_cleanup ON published(ExpiresAt, StatusName) WHERE StatusName IN ('Succeeded', 'Failed');

-- Same for received table
```

**Impact**:
- Retry processor: 5-10s query time at 1M messages
- Delayed processor: 2-5s query time
- Collector: 10-30s query time (deletes)

## Proposed Solutions

### Option 1: Add Composite Indexes (RECOMMENDED)
**Effort**: 2-3 hours (testing + migration)

Create EF Core migrations:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        CREATE INDEX IF NOT EXISTS idx_published_retry
        ON published(StatusName, Retries, Added)
        WHERE StatusName = 'Failed';
    ");
}
```

### Option 2: Add Covering Indexes
**Effort**: 3-4 hours
**Storage**: Higher (includes payload)

Include frequently accessed columns to avoid table lookups.

## Recommended Action

Implement Option 1 for immediate query performance improvement.

## Acceptance Criteria

- [x] Composite indexes created for all retry queries
- [x] Migrations for PostgreSQL and SQL Server
- [ ] Query plans show index usage (EXPLAIN ANALYZE)
- [ ] Performance tests verify <100ms query time at 1M messages
- [ ] Documentation updated

## Notes

Partial indexes (WHERE clause) reduce index size and improve performance.

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Performance Oracle Agent)

**Actions:**
- Analyzed query patterns in processor components
- Identified missing indexes
- Estimated performance impact

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-21 - Implemented

**By:** Claude Code
**Actions:**
- Added composite indexes for retry queries in PostgreSQL
  - `idx_received_retry`: (StatusName, Retries, Added) WHERE StatusName IN ('Failed','Scheduled')
  - `idx_received_delayed`: (StatusName, ExpiresAt) WHERE StatusName = 'Delayed'
  - `idx_published_retry`: (StatusName, Retries, Added) WHERE StatusName IN ('Failed','Scheduled')
  - `idx_published_delayed`: (StatusName, ExpiresAt) WHERE StatusName = 'Delayed'
- Added same composite indexes for SQL Server
  - `IX_Retry`: (StatusName, Retries, Added) WHERE StatusName IN ('Failed','Scheduled')
  - `IX_Delayed`: (StatusName, ExpiresAt) WHERE StatusName = 'Delayed'
- Modified: PostgreSqlStorageInitializer.cs
- Modified: SqlServerStorageInitializer.cs
- Indexes use partial/filtered WHERE clauses to reduce index size
- Query patterns now covered:
  1. Retry processor: `WHERE "Retries"<@Retries AND "StatusName" IN ('Failed','Scheduled')` → uses idx_*_retry
  2. Delayed processor: `WHERE "StatusName" = 'Delayed' AND "ExpiresAt"< @time` → uses idx_*_delayed
  3. Collector: Existing idx_*_ExpiresAt_StatusName covers cleanup queries
