using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            // ЛР 02: server-side validation, бізнес-правила 2-A та власний response DTO.
            var now = DateTimeOffset.UtcNow; // фіксую поточний UTC-час один раз на запит
            var errors = new Dictionary<string, string[]>();

            // 1. title: обов'язковий, довжина рахується до Trim()
            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Назва інциденту обов'язкова."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Назва інциденту не може бути довшою за 160 символів."];

            // 2. description: обов'язковий, до 4000 символів
            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис інциденту обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис інциденту не може бути довшим за 4000 символів."];

            // 3. severity: TryParse приймає і "7", тому додатково IsDefined
            var severityIsValid = Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            // 4. occurredAtUtc: обов'язковий і не далі ніж на 5 хвилин у майбутньому
            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Час виявлення інциденту обов'язковий."];
            else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Час виявлення не може бути в майбутньому."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // 5. нормалізація: Trim() рівно один раз для кожного рядка
            var title = request.Title!.Trim();
            var description = request.Description!.Trim();

            // 6. cross-field правило (T-09): High/Critical вимагають опис від 40 символів
            if ((severity == IncidentSeverity.High || severity == IncidentSeverity.Critical) && description.Length < 40)
            {
                errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];
                return Results.ValidationProblem(errors);
            }

            // 7. предметний конфлікт (T-03): активний інцидент з тією самою назвою (регістр важливий)
            var duplicateExists = await db.Incidents.AnyAsync(
                item => item.Title == title && item.Status != IncidentStatus.Closed, ct);
            if (duplicateExists)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Інцидент уже існує",
                    detail: "Активний інцидент з такою назвою вже зареєстровано.");
            }

            // 8. server-managed поля задає лише сервер
            var incident = new Incident
            {
                Id = Guid.NewGuid(), OwnerUserId = DbSeeder.AliceId,
                Title = title, Description = description,
                Severity = severity, Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            var response = new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(), incident.Status.ToString(),
                incident.OccurredAtUtc, incident.CreatedAtUtc);
            return Results.Created($"/api/incidents/{incident.Id}", response);
        });
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);
