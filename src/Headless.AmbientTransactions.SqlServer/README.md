# Headless Ambient Transactions SQL Server

SQL Server ambient transaction provider.

Commit drains are intentionally not run inside `Commit` / `CommitAsync`. SQL Server messaging integration completes drains through the post-commit diagnostic path by calling `CompleteExternally()`.
