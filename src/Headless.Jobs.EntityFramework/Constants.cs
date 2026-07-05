// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

public static class Constants
{
    public const string DefaultSchema = "jobs";

    // Bounded lengths for indexed string columns. SQL Server rejects nvarchar(max) as an index key
    // (error 1919), so any column in an index must be length-capped. These also feed the composite
    // IX_Function_Expression key, so their sum must stay within SQL Server's nonclustered key budget
    // (1700 bytes on 2016+): (256 + 256) * 2 = 1024 bytes. Both caps are generous for their content
    // (registered function names; 6-field cron or "%Config:Key%" placeholders).
    public const int FunctionMaxLength = 256;

    public const int CronExpressionMaxLength = 256;
}
