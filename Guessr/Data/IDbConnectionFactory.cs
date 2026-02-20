using System.Data;

namespace Guessr.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
