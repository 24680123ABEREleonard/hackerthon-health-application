using System.Globalization;
using Fieldnote.Mobile.Models;
using Microsoft.Data.Sqlite;

namespace Fieldnote.Mobile.Services;

public sealed class CaseDatabase
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(FileSystem.AppDataDirectory, "fieldnote.db"),
        ForeignKeys = true,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public string DatabasePath => new SqliteConnectionStringBuilder(_connectionString).DataSource;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            await using var connection = await OpenConnectionAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    PRAGMA journal_mode = WAL;
                    CREATE TABLE IF NOT EXISTS cases (
                        case_id TEXT PRIMARY KEY COLLATE NOCASE,
                        imaging_type TEXT NOT NULL CHECK (imaging_type IN ('X-ray', 'Ultrasound', 'MRI', 'CT scan', 'Other')),
                        detail TEXT NOT NULL,
                        priority TEXT NOT NULL CHECK (priority IN ('Routine', 'Urgent', 'Time-sensitive')),
                        status TEXT NOT NULL CHECK (status IN ('Needs review', 'Reviewed')),
                        notes TEXT NOT NULL DEFAULT '',
                        created_utc TEXT NOT NULL,
                        updated_utc TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS audit_events (
                        event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        case_id TEXT NOT NULL,
                        action TEXT NOT NULL,
                        occurred_utc TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ix_cases_created_utc ON cases(created_utc DESC);
                    CREATE INDEX IF NOT EXISTS ix_cases_status ON cases(status);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await SeedExamplesAsync(connection);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<ImagingCase>> GetCasesAsync()
    {
        await InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT case_id, imaging_type, detail, priority, status, created_utc, notes
            FROM cases ORDER BY created_utc DESC;
            """;

        var cases = new List<ImagingCase>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            cases.Add(new ImagingCase(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.GetString(6)));
        }
        return cases;
    }

    public async Task AddCaseAsync(string id, string type, string priority, string notes)
    {
        await InitializeAsync();
        var created = DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cases (case_id, imaging_type, detail, priority, status, notes, created_utc, updated_utc)
                VALUES ($id, $type, 'Imaging · details not specified', $priority, 'Needs review', $notes, $created, $created);
                """;
            command.Parameters.AddWithValue("$id", id.Trim());
            command.Parameters.AddWithValue("$type", type);
            command.Parameters.AddWithValue("$priority", priority);
            command.Parameters.AddWithValue("$notes", notes.Trim());
            command.Parameters.AddWithValue("$created", created.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }
        await WriteAuditEventAsync(connection, transaction, id.Trim(), "case-created", created);
        await transaction.CommitAsync();
    }

    public async Task SetReviewStatusAsync(string id, string status)
    {
        await InitializeAsync();
        var updated = DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE cases SET status = $status, updated_utc = $updated
                WHERE case_id = $id COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$updated", updated.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", id);
            if (await command.ExecuteNonQueryAsync() == 0)
                throw new InvalidOperationException("Case not found.");
        }

        await WriteAuditEventAsync(
            connection,
            transaction,
            id,
            status == "Reviewed" ? "case-marked-reviewed" : "case-returned-to-review",
            updated);
        await transaction.CommitAsync();
    }

    public async Task ClearCasesAsync()
    {
        await InitializeAsync();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cases; DELETE FROM audit_events;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task SeedExamplesAsync(SqliteConnection connection)
    {
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM cases;";
        if ((long)(await count.ExecuteScalarAsync() ?? 0L) != 0)
            return;

        var now = DateTimeOffset.Now;
        var examples = new[]
        {
            new ImagingCase("KJ-026", "X-ray", "Chest · 2 views", "Urgent", "Needs review", now.AddMinutes(-18), "Fictional demo case. No image analysis performed."),
            new ImagingCase("KJ-025", "Ultrasound", "Abdominal", "Routine", "Reviewed", now.AddMinutes(-42), "Fictional demo case. No image analysis performed."),
            new ImagingCase("KJ-024", "X-ray", "Wrist · 3 views", "Time-sensitive", "Needs review", now.AddMinutes(-64), "Fictional demo case. No image analysis performed."),
            new ImagingCase("KJ-023", "MRI", "Knee · without contrast", "Routine", "Reviewed", now.AddDays(-1), "Fictional demo case. No image analysis performed."),
            new ImagingCase("KJ-022", "X-ray", "Ankle · 2 views", "Routine", "Reviewed", now.AddDays(-1).AddMinutes(-30), "Fictional demo case. No image analysis performed."),
            new ImagingCase("KJ-021", "CT scan", "Head · without contrast", "Urgent", "Needs review", now.AddDays(-1).AddHours(-1), "Fictional demo case. No image analysis performed.")
        };

        await using var transaction = connection.BeginTransaction();
        foreach (var item in examples)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO cases (case_id, imaging_type, detail, priority, status, notes, created_utc, updated_utc)
                VALUES ($id, $type, $detail, $priority, $status, $notes, $created, $created);
                """;
            insert.Parameters.AddWithValue("$id", item.Id);
            insert.Parameters.AddWithValue("$type", item.Type);
            insert.Parameters.AddWithValue("$detail", item.Detail);
            insert.Parameters.AddWithValue("$priority", item.Priority);
            insert.Parameters.AddWithValue("$status", item.Status);
            insert.Parameters.AddWithValue("$notes", item.Notes);
            insert.Parameters.AddWithValue("$created", item.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task WriteAuditEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string caseId,
        string action,
        DateTimeOffset occurred)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_events (case_id, action, occurred_utc)
            VALUES ($id, $action, $occurred);
            """;
        command.Parameters.AddWithValue("$id", caseId);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$occurred", occurred.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }
}
