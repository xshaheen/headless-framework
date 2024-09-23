// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Database;

public interface IConnectionStringChecker
{
    Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString);
}
