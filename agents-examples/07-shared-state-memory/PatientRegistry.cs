using System.ComponentModel;
using Microsoft.Data.Sqlite;

namespace _07_shared_state_memory;

/// <summary>
/// SQLite-based patient registry for persistent medical records.
/// Uses primary constructor (C# 12) for lightweight dependency injection.
/// </summary>
/// <param name="connectionString">SQLite connection string (default: hospital.db)</param>
public class PatientRegistry(string connectionString = "Data Source=hospital.db")
{
    /// <summary>
    /// Initializes the database, creating the Patients table if it doesn't exist.
    /// Call this once at application startup.
    /// </summary>
    public void Initialize()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Patients (
                Name TEXT PRIMARY KEY,
                Conditions TEXT,
                Allergies TEXT,
                Medications TEXT,
                BloodType TEXT,
                LastVisit TEXT
            )
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Retrieves the complete medical record for a patient from the database.
    /// </summary>
    [Description("Retrieves the permanent medical record for a patient from the database. Returns patient data including conditions, allergies, medications, and blood type.")]
    public string GetPatientData(
        [Description("The patient's full name to search for")] string name)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Name, Conditions, Allergies, Medications, BloodType, LastVisit
                FROM Patients
                WHERE Name = @name COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("@name", name);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var conditions = reader.IsDBNull(1) ? "None recorded" : reader.GetString(1);
                var allergies = reader.IsDBNull(2) ? "None recorded" : reader.GetString(2);
                var medications = reader.IsDBNull(3) ? "None recorded" : reader.GetString(3);
                var bloodType = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4);
                var lastVisit = reader.IsDBNull(5) ? "N/A" : reader.GetString(5);

                return $"""
                    Patient: {reader.GetString(0)}
                    Blood Type: {bloodType}
                    Conditions: {conditions}
                    Allergies: {allergies}
                    Medications: {medications}
                    Last Visit: {lastVisit}
                    """;
            }

            return $"No patient record found for '{name}'. This appears to be a new patient.";
        }
        catch (Exception ex)
        {
            return $"Error retrieving patient data: {ex.Message}";
        }
    }

    /// <summary>
    /// Creates or updates a patient's medical record in the database.
    /// Uses UPSERT pattern (ON CONFLICT DO UPDATE) for safe concurrent updates.
    /// </summary>
    [Description("Creates or updates a patient's medical record in the database. Use this when DrHouse identifies new conditions, allergies, or medications.")]
    public string UpsertPatientData(
        [Description("The patient's full name")] string name,
        [Description("Comma-separated list of medical conditions/diagnoses (e.g., 'diabetes, hypertension')")] string conditions,
        [Description("Comma-separated list of known allergies (e.g., 'penicillin, peanuts')")] string allergies,
        [Description("Comma-separated list of current medications (optional)")] string? medications = null,
        [Description("Blood type if known (e.g., 'A+', 'O-') (optional)")] string? bloodType = null)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Patients (Name, Conditions, Allergies, Medications, BloodType, LastVisit)
                VALUES (@name, @conditions, @allergies, @medications, @bloodType, @lastVisit)
                ON CONFLICT(Name) DO UPDATE SET
                    Conditions = COALESCE(@conditions, Conditions),
                    Allergies = COALESCE(@allergies, Allergies),
                    Medications = COALESCE(@medications, Medications),
                    BloodType = COALESCE(@bloodType, BloodType),
                    LastVisit = @lastVisit
                """;
            
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@conditions", (object?)conditions ?? DBNull.Value);
            command.Parameters.AddWithValue("@allergies", (object?)allergies ?? DBNull.Value);
            command.Parameters.AddWithValue("@medications", (object?)medications ?? DBNull.Value);
            command.Parameters.AddWithValue("@bloodType", (object?)bloodType ?? DBNull.Value);
            command.Parameters.AddWithValue("@lastVisit", DateTime.Now.ToString("O"));

            var rowsAffected = command.ExecuteNonQuery();
            
            return rowsAffected > 0
                ? $"Success: Patient record for '{name}' has been saved to the database."
                : $"Warning: No changes made to patient record for '{name}'.";
        }
        catch (Exception ex)
        {
            return $"Error saving patient data: {ex.Message}";
        }
    }
}
