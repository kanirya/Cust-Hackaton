using Npgsql;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed class PostgresSnapshotStore
{
    private readonly string _connectionString;

    public PostgresSnapshotStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<TaxNetSnapshot?> LoadLatestAsync(JsonSerializerOptions jsonOptions, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = new NpgsqlCommand(
            "select content::text from taxnet_snapshots where id = 'current' limit 1;",
            connection);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<TaxNetSnapshot>(json, jsonOptions);
    }

    public async Task SaveAsync(TaxNetSnapshot snapshot, JsonSerializerOptions jsonOptions, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        var json = JsonSerializer.Serialize(snapshot, jsonOptions);
        await using var command = new NpgsqlCommand(
            """
            insert into taxnet_snapshots (id, saved_at_utc, content)
            values ('current', @saved_at_utc, @content::jsonb)
            on conflict (id) do update
            set saved_at_utc = excluded.saved_at_utc,
                content = excluded.content;
            """,
            connection);
        command.Parameters.AddWithValue("saved_at_utc", snapshot.SavedAtUtc);
        command.Parameters.AddWithValue("content", json);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PostgresSnapshotStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new PostgresSnapshotStatus(false, false, null, null, "Connection string not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = new NpgsqlCommand(
                "select saved_at_utc, octet_length(content::text) from taxnet_snapshots where id = 'current';",
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new PostgresSnapshotStatus(true, true, null, 0, null);
            }

            return new PostgresSnapshotStatus(true, true, reader.GetDateTime(0), reader.GetInt32(1), null);
        }
        catch (Exception ex)
        {
            return new PostgresSnapshotStatus(true, false, null, null, ex.Message);
        }
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            create table if not exists taxnet_snapshots (
                id text primary key,
                saved_at_utc timestamptz not null,
                content jsonb not null
            );
            create index if not exists ix_taxnet_snapshots_saved_at on taxnet_snapshots(saved_at_utc desc);
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record PostgresSnapshotStatus(
    bool Configured,
    bool Reachable,
    DateTimeOffset? SavedAtUtc,
    int? SnapshotBytes,
    string? Error);
