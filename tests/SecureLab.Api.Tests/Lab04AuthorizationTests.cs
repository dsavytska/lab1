using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 04, рівень «Задовільно»: головна action — details GET /api/incidents/{id}.
// Політика відмови для чужого ресурсу — 404, однаково з неіснуючим id.
public sealed class Lab04AuthorizationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task A02_Details_AsOwnerAlice_ReturnsOwnIncident()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);

        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DbSeeder.AliceIncidentId.ToString(), body!["id"]!.GetValue<string>());
        Assert.Equal("Аліса Коваль", body["ownerDisplayName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A03_Details_AsNonOwnerAlice_ReturnsSame404AsUnknownId_WithoutBobData()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);

        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");
        var foreignText = await foreign.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.ToString(), foreign.Content.Headers.ContentType?.ToString());

        // тіло відрізняється лише traceId — чужий інцидент не відрізнити від неіснуючого
        var foreignBody = JsonNode.Parse(foreignText)!.AsObject();
        var unknownBody = (await unknown.Content.ReadFromJsonAsync<JsonObject>())!;
        foreignBody.Remove("traceId");
        unknownBody.Remove("traceId");
        Assert.True(JsonNode.DeepEquals(unknownBody, foreignBody));

        // дані Bob у відповідь не потрапили
        Assert.DoesNotContain(DbSeeder.BobIncidentId.ToString(), foreignText);
        Assert.DoesNotContain("Боб Мельник", foreignText);
        Assert.DoesNotContain("Невідома спроба входу", foreignText);
    }

    [Fact]
    public async Task A03R_Details_AsAnalystMorgan_ReturnsAliceIncidentForAnalysis()
    {
        using var morgan = await factory.LoginAsync("morgan", StudyPassword);

        using var response = await morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DbSeeder.AliceIncidentId.ToString(), body!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Details_AsAnonymous_Returns401_WithoutRedirect()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await anonymous.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
