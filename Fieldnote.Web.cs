using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.AddHttpClient("ollama", client => client.Timeout = TimeSpan.FromSeconds(45));

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "fieldnote.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true,
    Mode = SqliteOpenMode.ReadWriteCreate
}.ToString();

var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")?.Trim() ?? "";
var ollamaAddress = GetLocalOllamaAddress();
var assistantTopics = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["add-case"] = "Explain how to add a fictional, non-identifying imaging case in Fieldnote. Explain only how to use the app.",
    ["review-case"] = "Explain how to use the Fieldnote case queue to record that a qualified clinician reviewed a case. Explain only app navigation.",
    ["find-case"] = "Explain how to search and filter cases in the Fieldnote app. Explain only app navigation.",
    ["privacy"] = "Explain how this Fieldnote demo stores records in local SQLite and uses an optional local Ollama model. Remind users not to enter real patient information."
};

var app = builder.Build();

app.MapGet("/api/status", async (IHttpClientFactory clients) =>
{
    var aiAvailable = false;
    var aiMessage = string.IsNullOrWhiteSpace(model)
        ? "Local AI is not configured. Set OLLAMA_MODEL and restart the app."
        : "The configured local Ollama model is not responding.";

    if (!string.IsNullOrWhiteSpace(model))
    {
        try
        {
            using var response = await clients.CreateClient("ollama")
                .GetAsync(new Uri(ollamaAddress!, "api/tags"));
            if (response.IsSuccessStatusCode)
            {
                using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
                var models = document?.RootElement.GetProperty("models");
                aiAvailable = models is { ValueKind: JsonValueKind.Array } &&
                    models.Value.EnumerateArray().Any(item =>
                        item.TryGetProperty("name", out var name) &&
                        string.Equals(name.GetString(), model, StringComparison.OrdinalIgnoreCase));
                aiMessage = aiAvailable
                    ? $"Local software-help AI connected: {model}. No clinical AI is connected."
                    : $"The Ollama service is online, but model '{model}' is not installed.";
            }
        }
        catch (HttpRequestException)
        {
            aiMessage = "The local Ollama service is unavailable.";
        }
        catch (TaskCanceledException)
        {
            aiMessage = "The local Ollama service did not respond in time.";
        }
    }

    return Results.Ok(new
    {
        database = new { connected = true, engine = "SQLite", path = "data/fieldnote.db" },
        ai = new
        {
            provider = "Local Ollama",
            connected = aiAvailable,
            configured = !string.IsNullOrWhiteSpace(model),
            model = string.IsNullOrWhiteSpace(model) ? null : model,
            message = aiMessage
        }
    });
});

app.MapGet("/api/cases", async () =>
{
    var cases = await ReadCasesAsync(connectionString);
    return Results.Ok(new
    {
        cases,
        today = cases.Count(item => item.Today),
        needsReview = cases.Count(item => item.Status == "Needs review"),
        reviewedToday = cases.Count(item => item.Today && item.Status == "Reviewed")
    });
});

app.MapPost("/api/cases", async (NewCaseRequest request) =>
{
    var id = request.Reference?.Trim() ?? "";
    var type = request.Type?.Trim() ?? "";
    var priority = request.Priority?.Trim() ?? "";
    var notes = request.Notes?.Trim() ?? "";
    if (id.Length is < 1 or > 32)
        return Results.BadRequest(new { error = "Case reference must be between 1 and 32 characters." });
    if (!new[] { "X-ray", "Ultrasound", "MRI", "CT scan", "Other" }.Contains(type, StringComparer.Ordinal))
        return Results.BadRequest(new { error = "Choose a valid imaging type." });
    if (!new[] { "Routine", "Urgent", "Time-sensitive" }.Contains(priority, StringComparer.Ordinal))
        return Results.BadRequest(new { error = "Choose a valid priority." });
    if (notes.Length > 240)
        return Results.BadRequest(new { error = "Context must be 240 characters or fewer." });

    var created = DateTimeOffset.UtcNow;
    try
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO cases (case_id, imaging_type, detail, priority, status, notes, created_utc, updated_utc)
                VALUES ($id, $type, 'Imaging · details not specified', $priority, 'Needs review', $notes, $created, $created);
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$type", type);
            insert.Parameters.AddWithValue("$priority", priority);
            insert.Parameters.AddWithValue("$notes", notes);
            insert.Parameters.AddWithValue("$created", created.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }
        await WriteAuditEventAsync(connection, transaction, id, "case-created", created);
        await transaction.CommitAsync();
    }
    catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
    {
        return Results.Conflict(new { error = "That case reference is already in the queue." });
    }

    return Results.Created($"/api/cases/{Uri.EscapeDataString(id)}", ToRecord(
        id, type, "Imaging · details not specified", priority, "Needs review", notes, created));
});

app.MapPatch("/api/cases/{id}/status", async (string id, ReviewStatusRequest request) =>
{
    if (request.Status is not ("Reviewed" or "Needs review"))
        return Results.BadRequest(new { error = "Choose a valid review status." });

    var updated = DateTimeOffset.UtcNow;
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = connection.BeginTransaction();
    await using (var command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cases SET status = $status, updated_utc = $updated
            WHERE case_id = $id COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$updated", updated.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id);
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { error = "Case not found." });
        }
    }

    await WriteAuditEventAsync(connection, transaction, id,
        request.Status == "Reviewed" ? "case-marked-reviewed" : "case-returned-to-review", updated);
    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapDelete("/api/cases", async () =>
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM cases; DELETE FROM audit_events;";
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/assistant", async (AssistantRequest request, IHttpClientFactory clients) =>
{
    if (!assistantTopics.TryGetValue(request.Topic ?? "", out var prompt))
        return Results.BadRequest(new { error = "Choose one of the predefined app-help topics." });
    if (string.IsNullOrWhiteSpace(model))
        return Results.Problem(
            title: "Local AI is not configured",
            detail: "Set OLLAMA_MODEL to an installed Ollama model and restart the app. No case data was sent.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var body = new
    {
        model,
        stream = false,
        messages = new[]
        {
            new
            {
                role = "system",
                content = "You are a concise software-help assistant for the Fieldnote user interface. Explain software only. Never analyze images or provide medical, diagnostic, triage, urgency, or treatment advice. Do not request patient information. If asked a clinical question, refuse and direct the user to a qualified clinician. This is not a medical device."
            },
            new { role = "user", content = prompt }
        }
    };

    try
    {
        using var response = await clients.CreateClient("ollama")
            .PostAsJsonAsync(new Uri(ollamaAddress!, "api/chat"), body);
        if (!response.IsSuccessStatusCode)
            return Results.Problem(
                title: "Local AI request failed",
                detail: $"Ollama returned HTTP {(int)response.StatusCode}. Check the local model service.",
                statusCode: StatusCodes.Status502BadGateway);

        using var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var answer = result?.RootElement.GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(answer)
            ? Results.Problem(
                title: "Local AI returned an empty response",
                detail: "No assistant answer was received from Ollama.",
                statusCode: StatusCodes.Status502BadGateway)
            : Results.Ok(new { answer, provider = "Local Ollama", model });
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            title: "Local AI service unavailable",
            detail: "Start Ollama locally and verify the configured model. No case data was sent.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException)
    {
        return Results.Problem(
            title: "Local AI request timed out",
            detail: "The local model did not respond in time. No case data was sent.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.ContentRootPath, "index.html"), "text/html"));
app.MapGet("/index.html", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.ContentRootPath, "index.html"), "text/html"));
app.MapGet("/styles.css", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.ContentRootPath, "styles.css"), "text/css"));
app.MapGet("/app.js", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.ContentRootPath, "app.js"), "text/javascript"));

await InitializeDatabaseAsync(connectionString);
await app.RunAsync();

static Uri? GetLocalOllamaAddress()
{
    var configured = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
    var address = new Uri(string.IsNullOrWhiteSpace(configured)
        ? "http://127.0.0.1:11434/"
        : configured.TrimEnd('/') + "/");
    if (!IPAddress.IsLoopback(address.HostNameType == UriHostNameType.IPv4 || address.HostNameType == UriHostNameType.IPv6
            ? IPAddress.Parse(address.Host)
            : IPAddress.None) ||
        address.HostNameType == UriHostNameType.Dns &&
        !address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrEmpty(address.UserInfo))
        throw new InvalidOperationException("OLLAMA_BASE_URL must use localhost or a loopback IP address.");
    return address;
}

static async Task InitializeDatabaseAsync(string connectionString)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
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

    await using var count = connection.CreateCommand();
    count.CommandText = "SELECT COUNT(*) FROM cases;";
    if ((long)(await count.ExecuteScalarAsync() ?? 0L) != 0)
        return;

    var now = DateTimeOffset.Now;
    var examples = new[]
    {
        ("KJ-026", "X-ray", "Chest · 2 views", "Urgent", "Needs review", now.AddMinutes(-18)),
        ("KJ-025", "Ultrasound", "Abdominal", "Routine", "Reviewed", now.AddMinutes(-42)),
        ("KJ-024", "X-ray", "Wrist · 3 views", "Time-sensitive", "Needs review", now.AddMinutes(-64)),
        ("KJ-023", "MRI", "Knee · without contrast", "Routine", "Reviewed", now.AddDays(-1)),
        ("KJ-022", "X-ray", "Ankle · 2 views", "Routine", "Reviewed", now.AddDays(-1).AddMinutes(-30)),
        ("KJ-021", "CT scan", "Head · without contrast", "Urgent", "Needs review", now.AddDays(-1).AddHours(-1))
    };
    await using var transaction = connection.BeginTransaction();
    foreach (var item in examples)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO cases (case_id, imaging_type, detail, priority, status, notes, created_utc, updated_utc)
            VALUES ($id, $type, $detail, $priority, $status, 'Fictional demo case. No image analysis performed.', $created, $created);
            """;
        insert.Parameters.AddWithValue("$id", item.Item1);
        insert.Parameters.AddWithValue("$type", item.Item2);
        insert.Parameters.AddWithValue("$detail", item.Item3);
        insert.Parameters.AddWithValue("$priority", item.Item4);
        insert.Parameters.AddWithValue("$status", item.Item5);
        insert.Parameters.AddWithValue("$created", item.Item6.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await insert.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
}

static async Task<List<CaseRecord>> ReadCasesAsync(string connectionString)
{
    var cases = new List<CaseRecord>();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT case_id, imaging_type, detail, priority, status, notes, created_utc
        FROM cases ORDER BY created_utc DESC;
        """;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var created = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
        cases.Add(ToRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), created));
    }
    return cases;
}

static CaseRecord ToRecord(string id, string type, string detail, string priority, string status, string notes, DateTimeOffset created)
{
    var local = created.ToLocalTime();
    var added = local.Date == DateTime.Now.Date
        ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
        : local.Date == DateTime.Now.Date.AddDays(-1)
            ? "Yesterday"
            : local.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    return new CaseRecord(id, type, detail, priority, status, added, local.Date == DateTime.Now.Date, notes);
}

static async Task WriteAuditEventAsync(SqliteConnection connection, SqliteTransaction transaction, string caseId, string action, DateTimeOffset occurred)
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

record NewCaseRequest(string? Reference, string? Type, string? Priority, string? Notes);
record ReviewStatusRequest(string? Status);
record AssistantRequest(string? Topic);
record CaseRecord(string Id, string Type, string Detail, string Priority, string Status, string Added, bool Today, string Notes);
