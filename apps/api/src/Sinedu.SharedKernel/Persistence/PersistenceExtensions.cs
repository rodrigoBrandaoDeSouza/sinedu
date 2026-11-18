using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Sinedu.SharedKernel.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Configuração padrão do PostgreSQL: nomes em snake_case e histórico de migrations no schema do módulo.</summary>
    public static DbContextOptionsBuilder UseSineduPostgres(this DbContextOptionsBuilder builder, string connectionString, string schema)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseNpgsql(connectionString, o => o
                .MigrationsHistoryTable("__ef_migrations", schema)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
    }

    public static DbContextOptionsBuilder<TContext> UseSineduPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString, string schema)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)builder).UseSineduPostgres(connectionString, schema);
        return builder;
    }

    public static string BuildConnectionString(string host, int port, string database, string user, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = user,
            Password = password,
            MaxPoolSize = 20,
        }.ConnectionString;

    /// <summary>Identificador SQL entre aspas duplas. Usado só com nomes já validados (slug).</summary>
    public static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Literal SQL entre aspas simples (para senhas em CREATE ROLE, que não aceita parâmetros).</summary>
    public static string QuoteLiteral(string literal) => "'" + literal.Replace("'", "''", StringComparison.Ordinal) + "'";
}
