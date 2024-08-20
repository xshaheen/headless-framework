namespace Framework.Database;

public interface IConnectionStringChecker
{
    Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString);
}
