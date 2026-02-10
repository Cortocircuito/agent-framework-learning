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
        // Input validation
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Patient name cannot be empty.";

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
        // Input validation
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Patient name cannot be empty.";

        if (name.Length > 100)
            return "Error: Patient name too long (max 100 characters).";

        if (string.IsNullOrWhiteSpace(conditions) && string.IsNullOrWhiteSpace(allergies))
            return "Error: Must provide at least one of: conditions or allergies.";

        // Validate blood type format if provided
        if (!string.IsNullOrWhiteSpace(bloodType))
        {
            var validBloodTypes = new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };
            if (!validBloodTypes.Contains(bloodType.ToUpper()))
                return $"Error: Invalid blood type '{bloodType}'. Must be one of: {string.Join(", ", validBloodTypes)}";
        }

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
            // Convert empty strings to DBNull to prevent wiping existing data
            command.Parameters.AddWithValue("@conditions",
                string.IsNullOrWhiteSpace(conditions) ? DBNull.Value : (object)conditions);
            command.Parameters.AddWithValue("@allergies",
                string.IsNullOrWhiteSpace(allergies) ? DBNull.Value : (object)allergies);
            command.Parameters.AddWithValue("@medications",
                string.IsNullOrWhiteSpace(medications) ? DBNull.Value : (object)medications);
            command.Parameters.AddWithValue("@bloodType",
                string.IsNullOrWhiteSpace(bloodType) ? DBNull.Value : (object)bloodType);
            command.Parameters.AddWithValue("@lastVisit", DateTime.Now.ToString("O"));

            var rowsAffected = command.ExecuteNonQuery();
            
            return rowsAffected > 0
                ? $"Success: Patient record for '{name}' has been saved to the database."
                : $"Warning: No changes made to patient record for '{name}'.";
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
                SELECT Name, Conditions, LastVisit
                FROM Patients
                ORDER BY LastVisit DESC
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
                var conditions = reader.IsDBNull(1) ? "No conditions recorded" : reader.GetString(1);
                var lastVisit = reader.IsDBNull(2) ? "Never" : reader.GetString(2);

                result.AppendLine($"\n{count}. {name}");
                result.AppendLine($"   Conditions: {conditions}");
                result.AppendLine($"   Last Visit: {lastVisit}");
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
}
