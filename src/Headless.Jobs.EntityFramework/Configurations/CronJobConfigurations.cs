// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Jobs.Configurations;

public class CronJobConfigurations<TCronJob>(string schema = Constants.DefaultSchema)
    : IEntityTypeConfiguration<TCronJob>
    where TCronJob : CronJobEntity, new()
{
    public void Configure(EntityTypeBuilder<TCronJob> builder)
    {
        builder.HasKey("Id");

        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.OnNodeDeath).HasConversion<string>().HasMaxLength(32);

        // Bound the indexed string columns — SQL Server cannot index nvarchar(max) (error 1919).
        builder.Property(e => e.Function).HasMaxLength(Constants.FunctionMaxLength);
        builder.Property(e => e.Expression).HasMaxLength(Constants.CronExpressionMaxLength);

        builder.HasIndex("Expression").HasDatabaseName("IX_CronJobs_Expression");

        // Index for common lookups by function + expression
        builder.HasIndex("Function", "Expression").HasDatabaseName("IX_Function_Expression");

        builder.ToTable("CronJobs", schema);
    }
}
