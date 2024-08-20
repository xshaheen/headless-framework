using System.Data;

namespace Framework.Database;

public interface ISqlConnectionFactory
{
    string GetConnectionString();

    IDbConnection GetOpenConnection();

    IDbConnection CreateNewConnection();
}
