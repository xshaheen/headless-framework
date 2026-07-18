# Repository Guidelines

All repository-specific instructions live in [CLAUDE.md](CLAUDE.md). Refer to that file as the single source of truth for project conventions, architecture, deployment behaviors, and development patterns.

## Learnings

- Automatic audit capture uses finalized EF model metadata as its single declarative policy; domain entities stay annotation-free, while PostgreSQL and SQL Server audit packages remain storage providers rather than parallel policy systems. (2026-07-15)
