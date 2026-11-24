using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.Data.SqlClient;

namespace AIQueryGenerator.Infrastructure.TargetDatabase;

public interface ISqlConnectionFactory
{
    SqlConnection Create(DatabaseConnection connection);
}

/// <summary>
/// Builds target connections from discrete, validated fields with SqlConnectionStringBuilder. Arbitrary connection
/// strings are never accepted, so options such as "Integrated Security" or alternate servers cannot be injected.
/// </summary>
public sealed class SqlConnectionFactory(ISecretStore secrets) : ISqlConnectionFactory
{
    public SqlConnection Create(DatabaseConnection connection)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connection.ServerName,
            InitialCatalog = connection.DatabaseName,
            Encrypt = connection.EncryptConnection ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = connection.TrustServerCertificate,
            ConnectTimeout = Math.Clamp(connection.ConnectionTimeoutSeconds, 1, 120),
            ApplicationName = "AIQueryGenerator",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            MultipleActiveResultSets = false,
            PersistSecurityInfo = false,
            Pooling = true,
            MaxPoolSize = 50,
        };

        switch (connection.AuthenticationType)
        {
            case SqlAuthenticationType.SqlPassword:
                var password = secrets.Resolve(connection.EncryptedCredentialsReference);
                if (string.IsNullOrEmpty(connection.Username) || password is null)
                    throw new BusinessRuleException("The stored credentials for this connection are missing or cannot be decrypted. Update the connection password.");
                builder.UserID = connection.Username;
                builder.Password = password;
                break;
            case SqlAuthenticationType.WindowsIntegrated:
                builder.IntegratedSecurity = true;
                break;
            case SqlAuthenticationType.EntraManagedIdentity:
                builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
                break;
            case SqlAuthenticationType.EntraDefault:
                builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
                break;
            default:
                throw new BusinessRuleException("Unsupported authentication type.");
        }

        return new SqlConnection(builder.ConnectionString);
    }
}
