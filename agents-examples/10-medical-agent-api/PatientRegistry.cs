using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace _10_medical_agent_api;

/// <summary>
/// SQLite-based patient registry for persistent medical records.
/// Uses primary constructor (C# 12) for lightweight dependency injection.
/// </summary>
/// <param name="connectionString">SQLite connection string (default: hospital.db)</param>
public class PatientRegistry(string connectionString = "Data Source=hospital.db")
{
    /// <summary>
    /// Initializes the database, creating the Patients table with the new schema.
    /// Call this once at application startup.
    /// </summary>
    public void Initialize()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Drop old table if exists and create new schema
        var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS Patients;
            
            CREATE TABLE Patients (
                FullName TEXT PRIMARY KEY,
                Room TEXT,
                Age INTEGER,
                MedicalHistory TEXT,
                CurrentDiagnosis TEXT,
                Evolution INTEGER,
                Plan TEXT,
                Observations TEXT
            )
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Retrieves the complete medical record for a patient from the database.
    /// </summary>
    [Description("Retrieves the permanent medical record for a patient from the database. Returns patient data including room, age, medical history, diagnosis, evolution, plan, and observations.")]
    public string GetPatientData(
        [Description("The patient's full name to search for")] string name)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Patient name cannot be empty.";

        try
        {
            var record = GetPatientRecord(name);
            
            if (record == null)
                return $"No patient record found for '{name}'. This appears to be a new patient.";

            // Format for display
            var result = $"""
                Patient: {record.FullName}
                Room: {record.Room ?? "Not assigned"}
                Age: {(record.Age.HasValue ? record.Age.ToString() : "Unknown")}
                Medical History (AP): {(record.MedicalHistory?.Any() == true ? string.Join(", ", record.MedicalHistory) : "None recorded")}
                Current Diagnosis (Dx): {record.CurrentDiagnosis ?? "Not documented"}
                Evolution: {(record.Evolution.HasValue ? record.Evolution.ToString() : "Not assessed")}
                Plan:
                {FormatPlan(record.Plan)}
                Observations: {record.Observations ?? "None"}
                """;

            return result;
        }
        catch (SqliteException ex)
        {
            return $"Database error retrieving patient data: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Unexpected error retrieving patient data: {ex.Message}";
        }
    }

    /// <summary>
    /// Retrieves a strongly-typed patient record from the database.
    /// </summary>
    public PatientRecord? GetPatientRecord(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT FullName, Room, Age, MedicalHistory, CurrentDiagnosis, Evolution, Plan, Observations
                FROM Patients
                WHERE FullName = @name COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("@name", name);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new PatientRecord(
                    FullName: reader.GetString(0),
                    Room: reader.IsDBNull(1) ? null : reader.GetString(1),
                    Age: reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    MedicalHistory: reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<List<string>>(reader.GetString(3)),
                    CurrentDiagnosis: reader.IsDBNull(4) ? null : reader.GetString(4),
                    Evolution: reader.IsDBNull(5) ? null : (Evolution)reader.GetInt32(5),
                    Plan: reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<List<string>>(reader.GetString(6)),
                    Observations: reader.IsDBNull(7) ? null : reader.GetString(7)
                );
            }

            return null;
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"Database error: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON deserialization error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates or updates a patient's medical record in the database.
    /// Uses UPSERT pattern (ON CONFLICT DO UPDATE) for safe concurrent updates.
    /// </summary>
    [Description("Creates or updates a patient's medical record in the database. Use this when ClinicalDataExtractor identifies patient information.")]
    public string UpsertPatientRecord(
        [Description("The patient's full name")] string fullName,
        [Description("Room number or identifier (optional)")] string? room = null,
        [Description("Patient age in years (optional)")] int? age = null,
        [Description("Comma-separated list of chronic conditions as acronyms (HTA, DL, ICC, etc.), allergies (e.g. Allergy:Penicillin), and ongoing medications (e.g. Med:Metformin) (optional)")] string? medicalHistory = null,
        [Description("Full-text current diagnosis - NO acronyms (optional)")] string? currentDiagnosis = null,
        [Description("Clinical evolution: Good, Stable, or Bad (optional)")] string? evolution = null,
        [Description("Comma-separated list of plan items: active treatments, medication adjustments, pending labs/microbiology/radiology/procedures, surgeries, specialist consultations, rehabilitation, discharge, transfer, or repatriation (optional)")] string? plan = null,
        [Description("Any clinical information that does not fit in the other fields (e.g. vital signs, social/family history, contextual notes). Must NOT include allergies or medications (those belong in medicalHistory), pending/scheduled/ordered items (those belong in plan), or anything already in currentDiagnosis (optional)")] string? observations = null)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(fullName))
            return "Error: Patient name cannot be empty.";

        if (fullName.Length > 100)
            return "Error: Patient name too long (max 100 characters).";

        try
        {
            // Parse evolution enum
            Evolution? evolutionEnum = null;
            if (!string.IsNullOrWhiteSpace(evolution))
            {
                if (Enum.TryParse<Evolution>(evolution, ignoreCase: true, out var parsed))
                    evolutionEnum = parsed;
                else
                    return $"Error: Invalid evolution value '{evolution}'. Must be: Good, Stable, or Bad.";
            }

            // Parse medical history
            List<string>? medicalHistoryList = null;
            if (!string.IsNullOrWhiteSpace(medicalHistory))
            {
                medicalHistoryList = medicalHistory
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            // Parse plan
            List<string>? planList = null;
            if (!string.IsNullOrWhiteSpace(plan))
            {
                planList = plan
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            // Create record
            var record = new PatientRecord(
                FullName: fullName,
                Room: room,
                Age: age,
                MedicalHistory: medicalHistoryList,
                CurrentDiagnosis: currentDiagnosis,
                Evolution: evolutionEnum,
                Plan: planList,
                Observations: observations
            );

            // Validate
            if (!record.IsValid(out var validationError))
                return $"Error: {validationError}";

            // Save to database
            return UpsertPatientRecord(record);
        }
        catch (Exception ex)
        {
            return $"Error saving patient data: {ex.Message}";
        }
    }

    /// <summary>
    /// Creates or updates a patient record using the strongly-typed PatientRecord.
    /// </summary>
    private string UpsertPatientRecord(PatientRecord record)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Patients (FullName, Room, Age, MedicalHistory, CurrentDiagnosis, Evolution, Plan, Observations)
                VALUES (@fullName, @room, @age, @medicalHistory, @currentDiagnosis, @evolution, @plan, @observations)
                ON CONFLICT(FullName) DO UPDATE SET
                    Room = COALESCE(@room, Room),
                    Age = COALESCE(@age, Age),
                    MedicalHistory = COALESCE(@medicalHistory, MedicalHistory),
                    CurrentDiagnosis = COALESCE(@currentDiagnosis, CurrentDiagnosis),
                    Evolution = COALESCE(@evolution, Evolution),
                    Plan = COALESCE(@plan, Plan),
                    Observations = COALESCE(@observations, Observations)
                """;

            command.Parameters.AddWithValue("@fullName", record.FullName);
            command.Parameters.AddWithValue("@room", (object?)record.Room ?? DBNull.Value);
            command.Parameters.AddWithValue("@age", record.Age.HasValue ? record.Age.Value : DBNull.Value);
            command.Parameters.AddWithValue("@medicalHistory", 
                record.MedicalHistory?.Any() == true ? JsonSerializer.Serialize(record.MedicalHistory) : DBNull.Value);
            command.Parameters.AddWithValue("@currentDiagnosis", (object?)record.CurrentDiagnosis ?? DBNull.Value);
            command.Parameters.AddWithValue("@evolution", 
                record.Evolution.HasValue ? (int)record.Evolution.Value : DBNull.Value);
            command.Parameters.AddWithValue("@plan",
                record.Plan?.Any() == true ? JsonSerializer.Serialize(record.Plan) : DBNull.Value);
            command.Parameters.AddWithValue("@observations", (object?)record.Observations ?? DBNull.Value);

            var rowsAffected = command.ExecuteNonQuery();

            return rowsAffected > 0
                ? $"Success: Patient record for '{record.FullName}' has been saved to the database."
                : $"Warning: No changes made to patient record for '{record.FullName}'.";
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint
        {
            return $"Error: Database constraint violation: {ex.Message}";
        }
        catch (SqliteException ex)
        {
            return $"Database error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Unexpected error saving patient data: {ex.Message}";
        }
    }

    /// <summary>
    /// Lists all patients in the database with basic information.
    /// </summary>
    public string ListAllPatients()
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT FullName, Room, Age, Evolution
                FROM Patients
                ORDER BY FullName
                """;

            using var reader = command.ExecuteReader();

            if (!reader.HasRows)
                return "\n📋 No patients found in database.";

            var result = new System.Text.StringBuilder();
            result.AppendLine("\n📋 PATIENT REGISTRY");
            result.AppendLine("═══════════════════════════════════════════════════");

            int count = 0;
            while (reader.Read())
            {
                count++;
                var name = reader.GetString(0);
                var room = reader.IsDBNull(1) ? "N/A" : reader.GetString(1);
                var age = reader.IsDBNull(2) ? "N/A" : reader.GetInt32(2).ToString();
                var evolution = reader.IsDBNull(3) ? "Not assessed" : ((Evolution)reader.GetInt32(3)).ToString();

                result.AppendLine($"\n{count}. {name}");
                result.AppendLine($"   Room: {room} | Age: {age} | Evolution: {evolution}");
            }

            result.AppendLine($"\nTotal patients: {count}");
            return result.ToString();
        }
        catch (SqliteException ex)
        {
            return $"Database error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error listing patients: {ex.Message}";
        }
    }

    /// <summary>
    /// Formats plan list for display.
    /// </summary>
    private static string FormatPlan(List<string>? plan)
    {
        if (plan == null || !plan.Any())
            return "  - None documented";

        return string.Join("\n", plan.Select(item => $"  - {item}"));
    }
}
