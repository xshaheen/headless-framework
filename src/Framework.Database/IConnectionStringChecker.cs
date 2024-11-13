// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Database;

public interface IConnectionStringChecker
{
    Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString);
}
