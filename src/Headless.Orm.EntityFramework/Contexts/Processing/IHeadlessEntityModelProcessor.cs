// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework.Contexts;

public interface IHeadlessEntityModelProcessor
{
    string? TenantId { get; }

    void ProcessModelCreating(ModelBuilder modelBuilder);

    ProcessBeforeSaveReport ProcessEntries(DbContext db);
}
