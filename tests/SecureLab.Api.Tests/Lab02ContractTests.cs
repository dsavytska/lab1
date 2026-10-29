using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 02: автоматичні перевірки контракту створення інциденту (T-02, T-03).
public sealed class Lab02ContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_WithNumericSeverity_Returns400WithSeverityKey()
    {
        // T-02: "7" проходить Enum.TryParse, але має бути відхилене Enum.IsDefined
        var request = new
        {
            title = "Перевірка числового severity",
            description = "Тестовий опис.",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };

        using var response = await _client.PostAsJsonAsync("/api/incidents", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"severity\"", body);
        Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task Create_SameActiveTitleTwice_Returns409OnSecondRequest()
    {
        // T-03: унікальна назва, щоб тест не залежав від попередніх запусків
        var title = $"Regression-{Guid.NewGuid():N}";
        var request = new
        {
            title,
            description = "Перевірка предметного конфлікту.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };

        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", request);
            using var second = await _client.PostAsJsonAsync("/api/incidents", request);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            // прибираю створений тестовий запис, щоб не засмічувати штучну БД
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }
}
