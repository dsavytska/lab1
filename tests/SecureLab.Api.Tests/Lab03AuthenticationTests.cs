using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 03: обов'язкові інтеграційні сценарії рівня «Задовільно» — A-01, A-02, A-05.
public sealed class Lab03AuthenticationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static object NewIncident(string title, object? ownerUserId = null) => ownerUserId is null
        ? new { title, description = "Інцидент для перевірки автентифікації.", severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1) }
        : new { title, description = "Інцидент для перевірки автентифікації.", severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1), ownerUserId };

    [Fact]
    public async Task A01_AnonymousCreate_Returns401_WithoutRedirect()
    {
        // анонімний клієнт без cookie, який не переходить за redirect
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await anonymous.PostAsJsonAsync("/api/incidents", NewIncident($"A01-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A02_LoginThenMe_ReturnsCurrentPrincipalOnly()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);

        using var response = await alice.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(DbSeeder.AliceId, root.GetProperty("id").GetGuid());
        Assert.Equal("alice", root.GetProperty("userName").GetString());
        // у відповіді немає жодних автентифікаційних артефактів
        foreach (var forbidden in new[] { "password", "passwordHash", "securityStamp", "concurrencyStamp", "token", "cookie" })
            Assert.False(root.TryGetProperty(forbidden, out _), $"Unexpected field: {forbidden}");
    }

    [Fact]
    public async Task A05_BobCreatesWithForgedOwner_OwnerIsBob()
    {
        var title = $"A05-{Guid.NewGuid():N}";
        using var bob = await factory.LoginAsync("bob", StudyPassword);
        try
        {
            // Bob навмисно підставляє в JSON власника Alice
            using var response = await bob.PostAsJsonAsync("/api/incidents", NewIncident(title, DbSeeder.AliceId));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var created = await db.Incidents.AsNoTracking().SingleAsync(item => item.Title == title);
            Assert.Equal(DbSeeder.BobId, created.OwnerUserId);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }
}
