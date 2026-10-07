using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuroCare.Application;
using NeuroCare.Domain;
using NeuroCare.Infrastructure;

namespace NeuroCare.Tests;

public class ReminderWorkerTests
{
    private static ServiceProvider Services(string database, FakeEmailSender email, FakeClock clock) => new ServiceCollection()
        .AddSingleton<ICurrentUser>(FakeCurrentUser.System())
        .AddSingleton<IClock>(clock).AddSingleton<IEmailSender>(email)
        .AddDbContext<NeuroCareDbContext>(o => o.UseInMemoryDatabase(database))
        .BuildServiceProvider();

    private static ReminderWorker Worker(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Notifications:RequireConsent"] = "true" }).Build(), NullLogger<ReminderWorker>.Instance);

    private static async Task AddRecipientAndConsent(TestEnv env, Scenario s, string mode)
    {
        env.Db.Users.Add(new ApplicationUser
        {
            Id = s.PatientA1UserId, OrganizationId = mode == "wrong-user-organization" ? s.OrgB : s.OrgA,
            FullName = "Paciente de teste", UserName = "patient@example.test", Email = "patient@example.test",
            EmailConfirmed = true, Active = mode != "inactive-user"
        });
        var purpose = ConsentPurposeCatalog.Find("notifications-email")!;
        env.Db.ConsentRecords.Add(new ConsentRecord
        {
            OrganizationId = s.OrgA, PatientId = s.PatientA1, PurposeKey = purpose.Key,
            Version = mode == "old-consent" ? "0.1" : purpose.Version,
            Status = mode == "revoked-consent" ? ConsentStatus.Revoked : ConsentStatus.Granted,
            GrantedAtUtc = env.Clock.UtcNow.AddDays(-1), PurposeText = purpose.Text
        });
        if (mode == "inactive-organization")
            (await env.Db.Organizations.SingleAsync(x => x.Id == s.OrgA)).Active = false;
        if (mode == "inactive-patient")
            (await env.Db.Patients.IgnoreQueryFilters().SingleAsync(x => x.Id == s.PatientA1)).Status = PatientStatus.Inactive;
        await env.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("eligible", 2)]
    [InlineData("inactive-organization", 0)]
    [InlineData("inactive-patient", 0)]
    [InlineData("inactive-user", 0)]
    [InlineData("wrong-user-organization", 0)]
    [InlineData("old-consent", 0)]
    [InlineData("revoked-consent", 0)]
    public async Task Reminders_require_active_matching_recipient_and_current_consent(string mode, int expected)
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        var clock = new FakeClock();
        using (var env = new TestEnv(database, FakeCurrentUser.System()))
        {
            await AddRecipientAndConsent(env, s, mode);
            env.Db.Appointments.Add(new Appointment
            {
                OrganizationId = s.OrgA, PatientId = s.PatientA1, DoctorId = s.DoctorA,
                StartsAtUtc = clock.UtcNow.AddHours(1)
            });
            env.Db.Medications.Add(new Medication
            {
                OrganizationId = s.OrgA, PatientId = s.PatientA1, Name = "Medicação teste", Dosage = "100 mg",
                Frequency = "1x", StartDate = clock.ToLocal(clock.UtcNow).Date,
                ReminderEnabled = true, ReminderTimes = "12:00"
            });
            await env.Db.SaveChangesAsync();
        }
        var email = new FakeEmailSender();
        using var services = Services(database, email, clock);
        using var worker = Worker(services);
        await worker.ProcessAsync();
        Assert.Equal(expected, email.Sent.Count);
        await worker.ProcessAsync();
        Assert.Equal(expected, email.Sent.Count); // already sent occurrences must not repeat
        using var inspect = new TestEnv(database, FakeCurrentUser.System());
        var dispatches = await inspect.Db.NotificationDispatches.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(expected, dispatches.Count);
        Assert.All(dispatches, x => { Assert.Equal(s.OrgA, x.OrganizationId); Assert.Equal(NotificationStatus.Sent, x.Status); });
    }

    [Fact]
    public async Task Appointment_scan_reaches_records_beyond_the_previous_global_limit()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        var clock = new FakeClock();
        using (var env = new TestEnv(database, FakeCurrentUser.System()))
        {
            await AddRecipientAndConsent(env, s, "eligible");
            for (var i = 0; i < 1001; i++)
                env.Db.Appointments.Add(new Appointment
                {
                    OrganizationId = s.OrgA, PatientId = s.PatientA2, DoctorId = s.DoctorA,
                    StartsAtUtc = clock.UtcNow.AddMinutes(1)
                });
            env.Db.Appointments.Add(new Appointment
            {
                OrganizationId = s.OrgA, PatientId = s.PatientA1, DoctorId = s.DoctorA,
                StartsAtUtc = clock.UtcNow.AddHours(1)
            });
            await env.Db.SaveChangesAsync();
        }
        var email = new FakeEmailSender();
        using var services = Services(database, email, clock);
        using var worker = Worker(services);
        await worker.ProcessAsync();
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Medication_scan_reaches_records_beyond_the_previous_global_limit()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        var clock = new FakeClock();
        using (var env = new TestEnv(database, FakeCurrentUser.System()))
        {
            await AddRecipientAndConsent(env, s, "eligible");
            for (var i = 0; i < 5001; i++)
                env.Db.Medications.Add(new Medication
                {
                    Id = Guid.Parse($"00000000-0000-0000-0000-{i:x12}"),
                    OrganizationId = s.OrgA, PatientId = s.PatientA1, Name = "Não vencida", Dosage = "100 mg",
                    Frequency = "1x", StartDate = clock.ToLocal(clock.UtcNow).Date,
                    ReminderEnabled = true, ReminderTimes = "13:00"
                });
            env.Db.Medications.Add(new Medication
            {
                Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                OrganizationId = s.OrgA, PatientId = s.PatientA1, Name = "Medicação ao final", Dosage = "100 mg",
                Frequency = "1x", StartDate = clock.ToLocal(clock.UtcNow).Date,
                ReminderEnabled = true, ReminderTimes = "12:00"
            });
            await env.Db.SaveChangesAsync();
        }
        var email = new FakeEmailSender();
        using var services = Services(database, email, clock);
        using var worker = Worker(services);
        await worker.ProcessAsync();
        Assert.Contains("Medicação ao final", System.Net.WebUtility.HtmlDecode(Assert.Single(email.Sent).Body));
    }
}
