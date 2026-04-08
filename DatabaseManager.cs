using Microsoft.Data.Sqlite;

namespace dlp_agent;

public class DatabaseManager
    {
        private readonly string _dbPath;

        public DatabaseManager()
        {
            // Save the database to the hidden ProgramData folder where Services have 100% full permissions!
            string safeDbFolder = @"C:\ProgramData\AerologueDLP";
            
            if (!Directory.Exists(safeDbFolder))
            {
                Directory.CreateDirectory(safeDbFolder);
            }

            _dbPath = Path.Combine(safeDbFolder, "telemetry.db");
            InitializeDatabase();
        }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var command = connection.CreateCommand();
        // Create table if it doesn't exist. IsSynced acts as our marker.
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Telemetry (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                Details TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                IsSynced INTEGER DEFAULT 0
            )";
        command.ExecuteNonQuery();
    }

    public void LogEvent(string eventType, string details)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Telemetry (EventType, Details) VALUES ($type, $details)";
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue("$details", details);
        command.ExecuteNonQuery();
    }

    public List<Dictionary<string, object>> GetUnsyncedEvents()
    {
        var events = new List<Dictionary<string, object>>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var command = connection.CreateCommand();
        
        // NEW: Select the Timestamp and order by oldest first (Id ASC)
        command.CommandText = "SELECT Id, EventType, Details, Timestamp FROM Telemetry WHERE IsSynced = 0 ORDER BY Id ASC LIMIT 500"; 
        
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            events.Add(new Dictionary<string, object>
            {
                { "id", reader.GetInt32(0) },
                { "type", reader.GetString(1) },
                { "details", reader.IsDBNull(2) ? "" : reader.GetString(2) },
                // NEW: Attach the exact local time to the JSON payload!
                { "timestamp", reader.GetString(3) } 
            });
        }
        return events;
    }

    public void MarkAsSynced(List<int> ids)
    {
        if (ids.Count == 0) return;
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var command = connection.CreateCommand();
        string idList = string.Join(",", ids);
        command.CommandText = $"UPDATE Telemetry SET IsSynced = 1 WHERE Id IN ({idList})";
        command.ExecuteNonQuery();
    }

    public void PurgeOldData()
    {
        // Deletes records older than 60 days to save disk space
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Telemetry WHERE Timestamp <= date('now', '-60 day')";
        command.ExecuteNonQuery();
    }
}