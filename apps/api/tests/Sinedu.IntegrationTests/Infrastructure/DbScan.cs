using Npgsql;

namespace Sinedu.IntegrationTests.Infrastructure;

/// <summary>Procura um texto em TODAS as linhas de TODAS as tabelas de um banco (varredura bruta, como um dump).</summary>
public static class DbScan
{
    public static async Task<IReadOnlyList<string>> FindAsync(string connectionString, string marker)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT quote_ident(table_schema) || '.' || quote_ident(table_name) FROM information_schema.tables " +
            "WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')", conn))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var hits = new List<string>();
        foreach (var table in tables)
        {
            // convert_from(bytea) não se aplica: colunas bytea viram hex em row_to_json, então procuramos também o hex do marcador.
            var hex = Convert.ToHexStringLower(System.Text.Encoding.UTF8.GetBytes(marker));
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} t WHERE row_to_json(t)::text LIKE @m OR row_to_json(t)::text LIKE @h", conn);
            cmd.Parameters.AddWithValue("m", "%" + marker + "%");
            cmd.Parameters.AddWithValue("h", "%" + hex + "%");
            if ((long)(await cmd.ExecuteScalarAsync())! > 0)
            {
                hits.Add(table);
            }
        }

        return hits;
    }
}
