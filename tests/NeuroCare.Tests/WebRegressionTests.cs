using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuroCare.Application;
using NeuroCare.Domain;
using NeuroCare.Infrastructure;

namespace NeuroCare.Tests;

public sealed class ReviewWebFactory(string database, FakeCurrentUser user) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=127.0.0.1;Database=Review;Integrated Security=true");
        builder.UseSetting("Database:AutoInitialize", "false");
        builder.UseSetting("Notifications:Enabled", "false");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1;Database=Review;Integrated Security=true",
            ["Database:AutoInitialize"] = "false", ["Notifications:Enabled"] = "false", ["Seed:Enabled"] = "false"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<NeuroCareDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<NeuroCareDbContext>>();
            services.AddDbContext<NeuroCareDbContext>(o => o.UseInMemoryDatabase(database));
            services.AddSingleton(user);
            services.AddSingleton<IClock, FakeClock>();
            services.AddSingleton<IStartupFilter, ReviewRemoteIpFilter>();
            services.AddSingleton<IFileStorage>(new FakeFileStorage { MaxFileBytes = 2 * 1048576 });
            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = "Review";
                o.DefaultChallengeScheme = "Review";
            }).AddScheme<AuthenticationSchemeOptions, ReviewAuthenticationHandler>("Review", _ => { });
        });
    }
}

// Header used exclusively by TestServer to simulate independent client connections.
public sealed class ReviewRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            if (IPAddress.TryParse(context.Request.Headers["X-Review-IP"], out var ip))
                context.Connection.RemoteIpAddress = ip;
            return continuation(context);
        });
        next(app);
    };
}

public sealed class ReviewAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, FakeCurrentUser user) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!user.IsAuthenticated) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = user.RoleSet.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, user.UserId!.Value.ToString()));
        claims.Add(new Claim(ClaimTypes.Name, "Usuário de teste"));
        if (user.OrganizationId is Guid org) claims.Add(new Claim(AppClaims.OrganizationId, org.ToString()));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}

public class WebRegressionTests
{
    [Fact]
    public async Task Medication_edit_form_round_trip_keeps_reminders_and_can_disable_them()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        var user = FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId);
        Guid id;
        using (var env = new ClinicalEnv(database, user))
            id = await env.Medications.CreateAsync(s.PatientA1, new SaveMedicationDto
            {
                Name = "Medicação teste", Dosage = "100 mg", Frequency = "2x ao dia",
                StartDate = new DateTime(2026, 3, 1), ReminderEnabled = true, ReminderTimes = "08:00,20:00"
            });

        await using var factory = new ReviewWebFactory(database, user);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var html = await client.GetStringAsync($"/Medications/Edit/{id}");
        Assert.Contains("Questionários da clínica", html);
        Assert.DoesNotContain("em breve", html);
        var fields = Inputs(html);
        Assert.Contains(fields, x => x.Key == "ReminderEnabled" && x.Value == "true");
        Assert.Contains(fields, x => x.Key == "ReminderTimes" && x.Value == "08:00,20:00");
        Replace(fields, "Dosage", "150 mg");
        using var response = await client.PostAsync($"/Medications/Edit/{id}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var env = new ClinicalEnv(database, user))
        {
            var saved = Assert.Single(await env.Medications.ListAsync(s.PatientA1));
            Assert.True(saved.ReminderEnabled);
            Assert.Equal("08:00,20:00", saved.ReminderTimes);
            Assert.Equal("150 mg", saved.Dosage);
        }

        fields = Inputs(await client.GetStringAsync($"/Medications/Edit/{id}"));
        fields.RemoveAll(x => x.Key == "ReminderEnabled" && x.Value == "true");
        using var disabled = await client.PostAsync($"/Medications/Edit/{id}", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, disabled.StatusCode);
        using var inspect = new ClinicalEnv(database, user);
        var item = Assert.Single(await inspect.Medications.ListAsync(s.PatientA1));
        Assert.False(item.ReminderEnabled);
        Assert.Null(item.ReminderTimes);
    }

    [Fact]
    public async Task Patient_menu_exposes_privacy_and_implemented_clinical_pages()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        await using var factory = new ReviewWebFactory(database, FakeCurrentUser.With(Roles.Patient, s.OrgA, s.PatientA1UserId));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/PatientPortal");
        Assert.Contains("href=\"/Lgpd\"", html);
        Assert.Contains("href=\"/Falls\"", html);
        Assert.Contains("href=\"/Questionnaires\"", html);
        Assert.Contains("href=\"/Documents\"", html);
        Assert.DoesNotContain("Questionários da clínica", html);
        using var privacy = await client.GetAsync("/Lgpd");
        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
    }

    [Fact]
    public async Task Report_page_renders_patient_summary_and_print_controls()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        await using var factory = new ReviewWebFactory(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync($"/Reports?patientId={s.PatientA1}");
        Assert.Contains("Resumo do prontu", html);
        Assert.Contains("Paciente A1", html);
        Assert.Contains("data-print", html);
        Assert.Contains("no-print", html);
        var artifacts = Environment.GetEnvironmentVariable("NEUROCARE_HTTP_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            await File.WriteAllTextAsync(Path.Combine(artifacts, "report.html"), html);
        }
    }

    [Fact]
    public async Task Login_rate_limit_is_partitioned_by_client_address()
    {
        await using var factory = new ReviewWebFactory(Guid.NewGuid().ToString(), FakeCurrentUser.System());
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        for (var i = 0; i < 11; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
            request.Headers.Add("X-Review-IP", "192.0.2.10");
            using var response = await client.SendAsync(request);
            // Without an antiforgery token, accepted requests fail with 400 before account authentication.
            Assert.Equal(i < 10 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using var other = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        other.Headers.Add("X-Review-IP", "192.0.2.11");
        using var independent = await client.SendAsync(other);
        Assert.Equal(HttpStatusCode.BadRequest, independent.StatusCode);
    }

    [Fact]
    public async Task Upload_page_uses_configured_limit_and_loads_client_validation()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        await using var factory = new ReviewWebFactory(database, FakeCurrentUser.With(Roles.Patient, s.OrgA, s.PatientA1UserId));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Documents/Upload");
        Assert.Contains("data-max-bytes=\"2097152\"", html);
        Assert.Contains("data-error-target=\"file-error\"", html);
        Assert.Contains("/js/upload-check.js", html);
    }

    [Theory]
    [InlineData("phq9", true)]
    [InlineData("seguranca-clinica", false)]
    public async Task Safety_messages_preserve_phq9_guidance_without_misinterpreting_custom_questions(string key, bool selfHarm)
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        Guid responseId;
        using (var doctor = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId)))
        {
            if (!selfHarm)
            {
                doctor.Base.Db.ClinicQuestionnaires.Add(new ClinicQuestionnaire
                {
                    OrganizationId = s.OrgA, Key = key, Title = "Segurança", Description = "Acompanhamento",
                    Prompt = "Como está hoje?", QuestionsJson = "[{\"Text\":\"Você caiu hoje?\",\"Options\":[{\"Value\":0,\"Label\":\"Não\"},{\"Value\":1,\"Label\":\"Sim\"}]}]",
                    BandsJson = "[]", SafetyItemIndex = 0
                });
                await doctor.Base.Db.SaveChangesAsync();
            }
            var count = selfHarm ? 9 : 1;
            responseId = (await doctor.Questionnaires.SubmitAsync(s.PatientA1, key,
                Enumerable.Range(0, count).ToDictionary(i => i, _ => 1))).Id;
        }
        await using var factory = new ReviewWebFactory(database, FakeCurrentUser.With(Roles.Patient, s.OrgA, s.PatientA1UserId));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync($"/Questionnaires/Result/{responseId}");
        Assert.Contains("192", html);
        Assert.Equal(selfHarm, html.Contains("CVV", StringComparison.Ordinal));
        if (!selfHarm) Assert.Contains("item de seguran", html);
    }

    private static List<KeyValuePair<string, string>> Inputs(string html)
    {
        html = Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>", RegexOptions.IgnoreCase)
            .Single(form => form.Value.Contains("action=\"/Medications/Edit/", StringComparison.Ordinal)).Value;
        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match tag in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var attrs = Regex.Matches(tag.Value, "([\\w-]+)=\"([^\"]*)\"")
                .ToDictionary(x => x.Groups[1].Value, x => WebUtility.HtmlDecode(x.Groups[2].Value), StringComparer.OrdinalIgnoreCase);
            if (!attrs.TryGetValue("name", out var name)) continue;
            attrs.TryGetValue("type", out var type);
            if (type == "checkbox" && !Regex.IsMatch(tag.Value, "\\bchecked(?:=|\\s|>)")) continue;
            fields.Add(new(name, attrs.GetValueOrDefault("value", "")));
        }
        return fields;
    }

    private static void Replace(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        fields.RemoveAll(x => x.Key == name);
        fields.Add(new(name, value));
    }
}
