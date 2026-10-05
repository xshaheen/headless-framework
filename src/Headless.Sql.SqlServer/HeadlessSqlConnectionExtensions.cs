// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;

namespace Microsoft.Data.SqlClient;

/// <summary>
/// Copies a <see cref="SqlConnection" />'s connection string for a feature that opens connections of its own, such as
/// the Jobs coordination store or the Messaging storage built from an application <c>DbContext</c>. Declared in the
/// augmented type's namespace so the accessor is discoverable wherever a <see cref="SqlConnection" /> is in scope.
/// </summary>
[PublicAPI]
public static partial class HeadlessSqlConnectionExtensions
{
    extension(SqlConnection connection)
    {
        /// <summary>
        /// Returns a connection string for a separate connection, after checking that one opened from it
        /// authenticates the way this connection does.
        /// </summary>
        /// <remarks>
        /// A connection can authenticate with credentials its connection string does not carry, and a copied string
        /// then fails at the first login, long after startup, with an error that names neither the string nor the
        /// feature. This turns that into a startup error that names the cause. A token an interceptor attaches
        /// while the connection opens is invisible here, so the check cannot report it.
        /// </remarks>
        /// <param name="configuredConnectionString">
        /// The string the connection was configured from, when the caller holds it: EF Core's
        /// <c>Database.GetConnectionString()</c> keeps it whole for a context configured with a connection string.
        /// <see langword="null" /> reads the connection's own string, which SqlClient strips of its password once
        /// the connection has opened, so a pooled or shared connection may no longer carry it.
        /// </param>
        /// <returns>The connection string, credentials included.</returns>
        /// <exception cref="ArgumentNullException">The connection is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// The connection authenticates with an <see cref="SqlConnection.AccessToken" />, an
        /// <see cref="SqlConnection.AccessTokenCallback" />, or a <see cref="SqlConnection.Credential" />, none of
        /// which a connection string carries; or the string names a SQL login and no password keyword, as the
        /// connection's own string does once it has opened unless <c>Persist Security Info</c> is set. An explicit
        /// empty password (<c>Password=;</c>) is accepted.
        /// </exception>
        public string GetReusableConnectionString(string? configuredConnectionString = null)
        {
            Argument.IsNotNull(connection);

            if (connection.AccessToken is not null || connection.AccessTokenCallback is not null)
            {
                throw new InvalidOperationException(
                    "The SQL Server connection authenticates with an access token, which its connection string does "
                        + "not carry. Put the authentication in the connection string (for example "
                        + "'Authentication=Active Directory Default'), or give the feature its own connection string."
                );
            }

            if (connection.Credential is not null)
            {
                throw new InvalidOperationException(
                    "The SQL Server connection authenticates with a SqlCredential, which its connection string does "
                        + "not carry. Put the login in the connection string, or give the feature its own connection "
                        + "string."
                );
            }

            var connectionString = configuredConnectionString ?? connection.ConnectionString;
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (
                !builder.IntegratedSecurity
                && builder.Authentication is SqlAuthenticationMethod.NotSpecified or SqlAuthenticationMethod.SqlPassword
                && !string.IsNullOrEmpty(builder.UserID)
                && !_NamesPassword(connectionString)
            )
            {
                throw new InvalidOperationException(
                    "The SQL Server connection string names a login but no password. SqlClient removes the password "
                        + "once a connection opens, so the string of a connection that was already open cannot be "
                        + "copied. Configure the context with a connection string instead of an opened connection, or "
                        + "give the feature its own connection string."
                );
            }

            return connectionString;
        }
    }

    // SqlClient removes the password keyword itself, so a stripped string has no keyword at all, while an explicit
    // empty password ("Password=;") is a login that really has none. Both connection string builders drop a keyword
    // whose value is empty, so the keyword is looked for in the raw string.
    private static bool _NamesPassword(string connectionString) => _PasswordKeyword.IsMatch(connectionString);

    [GeneratedRegex(@"(?:^|;)\s*(?:password|pwd)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex _PasswordKeyword { get; }
}
