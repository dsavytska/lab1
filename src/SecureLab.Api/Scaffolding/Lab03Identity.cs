using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
        {
            // ЛР 03: спершу перевіряю поля за контрактом, пароль не обрізаю і нікуди не пишу
            var errors = ValidateRegistration(input);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = input.UserName!.Trim(), Email = input.Email!.Trim(),
                DisplayName = input.DisplayName!.Trim()
            };
            // хеш пароля рахує сам Identity (UserManager), власного хешування немає
            var result = await users.CreateAsync(user, input.Password!);
            if (!result.Succeeded)
                return Results.BadRequest(new { message = "Registration failed" }); // одна обережна відмова
            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            return Results.Created("/api/me", new RegisterResponse(user.Id, user.UserName!, user.DisplayName));
        });
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
                return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        app.MapPost("/api/auth/logout", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        // ЛР 03: поточний користувач береться лише з перевіреної cookie (HttpContext.User)
        app.MapGet("/api/me", GetCurrentUserAsync).RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    private static async Task<IResult> GetCurrentUserAsync(HttpContext httpContext, SecureLabDbContext db)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var verifiedUserId))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == verifiedUserId);
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(new CurrentUserResponse(user.Id, user.UserName ?? string.Empty, user.DisplayName));
    }

    private static readonly Regex AllowedUserName = new("^[A-Za-z0-9._@+-]+$");

    private static Dictionary<string, string[]> ValidateRegistration(RegistrationInput input)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(input.UserName))
            errors["userName"] = ["Ім'я користувача обов'язкове."];
        else if (input.UserName.Trim().Length > 256)
            errors["userName"] = ["Ім'я користувача не може бути довшим за 256 символів."];
        else if (!AllowedUserName.IsMatch(input.UserName.Trim()))
            errors["userName"] = ["Дозволені латинські літери, цифри та символи - . _ @ +"];

        if (string.IsNullOrWhiteSpace(input.Email))
            errors["email"] = ["Email обов'язковий."];
        else if (input.Email.Trim().Length > 256)
            errors["email"] = ["Email не може бути довшим за 256 символів."];
        else if (!MailAddress.TryCreate(input.Email.Trim(), out var parsed) || parsed.Address != input.Email.Trim())
            errors["email"] = ["Некоректний формат email."];

        if (string.IsNullOrWhiteSpace(input.DisplayName))
            errors["displayName"] = ["Ім'я для відображення обов'язкове."];
        else if (input.DisplayName.Trim().Length > 120)
            errors["displayName"] = ["Ім'я для відображення не може бути довшим за 120 символів."];

        if (string.IsNullOrEmpty(input.Password))
            errors["password"] = ["Пароль обов'язковий."];

        return errors;
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegisterResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid Id, string UserName, string DisplayName);
