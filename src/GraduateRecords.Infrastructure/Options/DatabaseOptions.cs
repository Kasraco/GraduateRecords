using Microsoft.Data.SqlClient;

namespace GraduateRecords.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public string Default { get; set; } = string.Empty;

    public static bool IsValid(string connectionString, string environmentName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return false;
            var value = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(value.DataSource) || string.IsNullOrWhiteSpace(value.InitialCatalog)) return false;
            if (environmentName is "Development" or "Testing")
            {
                return true;
            }

            var encrypted = value.Encrypt == SqlConnectionEncryptOption.Mandatory
                || value.Encrypt == SqlConnectionEncryptOption.Strict;
            return encrypted && !value.TrustServerCertificate;
        }
        catch (ArgumentException)
        {
            return false; // Never propagate parser errors containing configuration values.
        }
    }
}
