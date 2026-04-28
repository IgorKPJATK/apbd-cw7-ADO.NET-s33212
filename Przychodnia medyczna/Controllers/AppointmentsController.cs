using Microsoft.AspNetCore.Mvc;
using Przychodnia_medyczna.DTO;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Przychodnia_medyczna.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AppointmentsController :ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
                            ?? throw new InvalidOperationException("Connection string not found.");
    }
    
    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
                SELECT 
                    a.IdAppointment, a.AppointmentDate, a.Status, a.Reason,
                    p.FirstName + ' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail
                FROM dbo.Appointments a
                JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                WHERE (@Status IS NULL OR a.Status = @Status)
                  AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                ORDER BY a.AppointmentDate;";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Status", string.IsNullOrEmpty(status) ? DBNull.Value : status);
        command.Parameters.AddWithValue("@PatientLastName", string.IsNullOrEmpty(patientLastName) ? DBNull.Value : patientLastName);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }
}