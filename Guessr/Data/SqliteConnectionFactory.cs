using System.Data;
using Microsoft.Data.Sqlite;
using Dapper;

namespace Guessr.Data;

public class SqliteConnectionFactory(string dbPath) : IDbConnectionFactory
{
    private readonly string _connectionString = $"Data Source={dbPath}";

    public IDbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        connection.Execute("PRAGMA journal_mode=WAL");

        return connection;
    }
}
