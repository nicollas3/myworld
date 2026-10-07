using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuroCare.Application;
using NeuroCare.Domain;

namespace NeuroCare.Infrastructure;

/// <summary>
/// Processa lembretes sem depender de uma sessão web. Consultas ignoram filtros globais de tenant,
/// mas cada envio preserva OrganizationId e nunca mistura dados entre organizações.
/// </summary>
public class ReminderWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Notifications:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Notifications:WorkerIntervalSeconds", 60), 30, 3600));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try { await ProcessAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Falha no processamento de lembretes do NeuroCare."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task ProcessAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuroCareDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var nowUtc = clock.UtcNow;
        var nowLocal = clock.ToLocal(nowUtc);

        await ProcessAppointmentsAsync(db, email, clock, nowUtc, ct);
        await ProcessMedicationsAsync(db, email, nowLocal, ct);
    }

    private async Task ProcessAppointmentsAsync(NeuroCareDbContext db, IEmailSender email, IClock clock, DateTime nowUtc, CancellationToken ct)
    {
        var leadHours = Math.Clamp(config.GetValue("Notifications:AppointmentLeadHours", 24), 1, 168);
        var to = nowUtc.AddHours(leadHours);
        var query = db.Appointments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
                        && a.StartsAtUtc > nowUtc && a.StartsAtUtc <= to
                        && db.Organizations.Any(o => o.Id == a.OrganizationId && o.Active))
            .OrderBy(a => a.StartsAtUtc).ThenBy(a => a.Id);
        const int batchSize = 1000;
        for (var offset = 0; ; offset += batchSize)
        {
            var appointments = await query.Skip(offset).Take(batchSize).ToListAsync(ct);
            if (appointments.Count == 0) break;
            foreach (var a in appointments)
            {
                var patient = await db.Patients.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == a.PatientId && p.OrganizationId == a.OrganizationId && p.Status == PatientStatus.Active, ct);
                if (patient?.UserId is null) continue;
                if (config.GetValue("Notifications:RequireConsent", true) && !await HasNotificationConsentAsync(db, patient, ct)) continue;
                var recipient = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == patient.UserId && u.OrganizationId == patient.OrganizationId && u.Active && u.EmailConfirmed, ct);
                if (string.IsNullOrWhiteSpace(recipient?.Email)) continue;
                var occurrence = a.StartsAtUtc.ToString("yyyyMMddHHmm");
                var template = EmailTemplates.AppointmentReminder(patient.DisplayName, clock.ToLocal(a.StartsAtUtc));
                await SendOnceAsync(db, email, a.OrganizationId, NotificationKind.AppointmentReminder, a.Id,
                    recipient.Id, occurrence, recipient.Email, template, ct);
            }
            if (appointments.Count < batchSize) break;
        }
    }

    private async Task ProcessMedicationsAsync(NeuroCareDbContext db, IEmailSender email, DateTime nowLocal, CancellationToken ct)
    {
        var query = db.Medications.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Status == MedicationStatus.Active && m.ReminderEnabled && m.ReminderTimes != null
                        && db.Organizations.Any(o => o.Id == m.OrganizationId && o.Active))
            .OrderBy(m => m.Id);
        const int batchSize = 1000;
        for (var offset = 0; ; offset += batchSize)
        {
            var meds = await query.Skip(offset).Take(batchSize).ToListAsync(ct);
            if (meds.Count == 0) break;
            foreach (var med in meds)
            {
                if (med.StartDate.Date > nowLocal.Date || (med.EndDate.HasValue && med.EndDate.Value.Date < nowLocal.Date)) continue;
                var due = ParseTimes(med.ReminderTimes!).Any(t => Math.Abs((nowLocal.TimeOfDay - t).TotalMinutes) <= 2);
                if (!due) continue;
                var patient = await db.Patients.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == med.PatientId && p.OrganizationId == med.OrganizationId && p.Status == PatientStatus.Active, ct);
                if (patient?.UserId is null) continue;
                if (config.GetValue("Notifications:RequireConsent", true) && !await HasNotificationConsentAsync(db, patient, ct)) continue;
                var recipient = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == patient.UserId && u.OrganizationId == patient.OrganizationId && u.Active && u.EmailConfirmed, ct);
                if (string.IsNullOrWhiteSpace(recipient?.Email)) continue;
                var matched = ParseTimes(med.ReminderTimes!).OrderBy(t => Math.Abs((nowLocal.TimeOfDay - t).TotalMinutes)).First();
                var occurrence = $"{nowLocal:yyyyMMdd}-{matched:hh\\:mm}";
                var template = EmailTemplates.MedicationReminder(patient.DisplayName, med.Name, med.Dosage);
                await SendOnceAsync(db, email, med.OrganizationId, NotificationKind.MedicationReminder, med.Id,
                    recipient.Id, occurrence, recipient.Email, template, ct);
            }
            if (meds.Count < batchSize) break;
        }
    }

    private static Task<bool> HasNotificationConsentAsync(NeuroCareDbContext db, Patient patient, CancellationToken ct)
    {
        var purpose = ConsentPurposeCatalog.Find("notifications-email")!;
        return db.ConsentRecords.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.OrganizationId == patient.OrganizationId && x.PatientId == patient.Id &&
            x.PurposeKey == purpose.Key && x.Version == purpose.Version && x.Status == ConsentStatus.Granted, ct);
    }

    private static IEnumerable<TimeSpan> ParseTimes(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => TimeSpan.TryParseExact(x, @"hh\:mm", null, out var t) ? (TimeSpan?)t : null)
        .Where(x => x.HasValue).Select(x => x!.Value);

    private async Task SendOnceAsync(NeuroCareDbContext db, IEmailSender sender, Guid orgId, NotificationKind kind,
        Guid referenceId, Guid recipientUserId, string occurrenceKey, string to, (string Subject, string Body) content, CancellationToken ct)
    {
        var dispatch = await db.NotificationDispatches.IgnoreQueryFilters().FirstOrDefaultAsync(x =>
            x.OrganizationId == orgId && x.Kind == kind && x.ReferenceId == referenceId &&
            x.RecipientUserId == recipientUserId && x.OccurrenceKey == occurrenceKey, ct);
        if (dispatch?.Status == NotificationStatus.Sent) return;
        if (dispatch is null)
        {
            dispatch = new NotificationDispatch
            {
                OrganizationId = orgId, Kind = kind, ReferenceId = referenceId,
                RecipientUserId = recipientUserId, OccurrenceKey = occurrenceKey, Status = NotificationStatus.Pending
            };
            await db.NotificationDispatches.AddAsync(dispatch, ct);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            await sender.SendAsync(to, content.Subject, content.Body, ct);
            dispatch.Status = NotificationStatus.Sent;
            dispatch.SentAtUtc = DateTime.UtcNow;
            dispatch.ErrorCode = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dispatch.Status = NotificationStatus.Failed;
            dispatch.ErrorCode = ex.GetType().Name;
            logger.LogError(ex, "Falha ao enviar {Kind} para usuário {RecipientUserId}", kind, recipientUserId);
        }
        await db.SaveChangesAsync(ct);
    }
}
