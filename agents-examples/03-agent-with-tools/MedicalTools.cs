using System.ComponentModel;

namespace _03_agent_with_tools;

public class MedicalTools
{
    [Description("Gets the summarized clinical history of a patient by name.")]
    public string GetPatientHistory(string patientName)
    {
        // Here we would simulate a database query
        return patientName.ToLower() switch
        {
            "juan perez" =>
                "Patient with chronic hypertension. Last visit: 2 weeks ago. Medication: Enalapril.",
            "maria garcia" =>
                "Asthmatic patient. Allergic to penicillin. Last episode: January 2024.",
            _ => "No history found for this patient."
        };
    }
}