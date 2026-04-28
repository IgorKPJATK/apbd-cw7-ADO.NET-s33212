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
    
    [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
        {
            if (request.AppointmentDate <= DateTime.Now)
                return BadRequest(new ErrorResponseDto { Message = "Appointment date must be in the future." });

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            
            var patientValid = await ExecuteScalarAsync<int>(connection, 
                "SELECT COUNT(1) FROM dbo.Patients WHERE IdPatient = @Id AND IsActive = 1;", 
                new SqlParameter("@Id", request.IdPatient));
            
            if (patientValid == 0) return BadRequest(new ErrorResponseDto { Message = "Patient does not exist or is inactive." });

            
            var doctorValid = await ExecuteScalarAsync<int>(connection, 
                "SELECT COUNT(1) FROM dbo.Doctors WHERE IdDoctor = @Id AND IsActive = 1;", 
                new SqlParameter("@Id", request.IdDoctor));
                
            if (doctorValid == 0) return BadRequest(new ErrorResponseDto { Message = "Doctor does not exist or is inactive." });

            
            var conflict = await ExecuteScalarAsync<int>(connection,
                "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate;",
                new SqlParameter("@IdDoctor", request.IdDoctor),
                new SqlParameter("@AppointmentDate", request.AppointmentDate));

            if (conflict > 0) return Conflict(new ErrorResponseDto { Message = "Doctor already has an appointment at this time." });

            
            var insertQuery = @"
                INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Reason, Status, CreatedAt)
                OUTPUT INSERTED.IdAppointment
                VALUES (@IdPatient, @IdDoctor, @AppointmentDate, @Reason, 'Scheduled', GETDATE());";

            await using var command = new SqlCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            command.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            command.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            command.Parameters.AddWithValue("@Reason", request.Reason);

            var newId = (int)await command.ExecuteScalarAsync();

            return CreatedAtAction(nameof(GetAppointments), new { idAppointment = newId }, request);
        }
    
        private async Task<T> ExecuteScalarAsync<T>(SqlConnection connection, string query, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync();
        return result == DBNull.Value || result == null ? default! : (T)result;
    }
}