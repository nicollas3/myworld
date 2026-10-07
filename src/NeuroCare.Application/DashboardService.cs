using NeuroCare.Domain;

namespace NeuroCare.Application;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}

public class DashboardService(
    IPatientRepository patients, IDoctorRepository doctors, IAppointmentRepository appointments,
    IQuestionnaireResponseRepository questionnaires, AccessGuard guard, IClock clock,
    IQuestionnaireDefinitionProvider definitions) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        guard.RequireClinicalStaff();

        var nowUtc = clock.UtcNow;
        var todayLocal = clock.ToLocal(nowUtc).Date;
        var dayStart = clock.ToUtc(todayLocal);
        var dayEnd = dayStart.AddDays(1);
        var since30 = nowUtc.AddDays(-30);

        // Sequencial de propósito: DbContext não é thread-safe.
        var today = await appointments.ListAsync(dayStart, dayEnd, null, null, ct);
        var counts = await appointments.CountByStatusAsync(since30, nowUtc, ct);
        var activePatients = await patients.CountByStatusAsync(PatientStatus.Active, ct);
        var activeDoctors = await doctors.CountActiveAsync(ct);
        var upcoming = await appointments.CountUpcomingAsync(nowUtc, ct);
        var withoutFollowUp = await patients.CountWithoutFollowUpAsync(nowUtc.AddDays(-90), ct);
        var recent = await patients.RecentAsync(5, ct);

        // Alertas de segurança de questionários: somente médicos veem (ClinicAdmin não acessa dados clínicos).
        IReadOnlyList<SafetyAlertDto> alerts = [];
        if (guard.IsDoctor)
        {
            var flagged = await questionnaires.ListSafetyFlagsAsync(nowUtc.AddDays(-30), 5, ct);
            var definitionMap = (await definitions.ListAsync(true, ct))
                .ToDictionary(x => x.Definition.Key, x => x.Definition, StringComparer.OrdinalIgnoreCase);
            alerts = flagged.Select(r =>
            {
                definitionMap.TryGetValue(r.QuestionnaireKey, out var currentDefinition);
                var definition = QuestionnaireHistory.Snapshot(r) ?? currentDefinition;
                return new SafetyAlertDto(r.Id, r.PatientId, r.Patient?.DisplayName ?? "",
                    definition?.Title ?? r.QuestionnaireKey, clock.ToLocal(r.AnsweredAtUtc));
            }).ToList();
        }

        return new DashboardDto(
            activePatients, activeDoctors, today.Count, upcoming, withoutFollowUp,
            counts.GetValueOrDefault(AppointmentStatus.NoShow),
            counts.GetValueOrDefault(AppointmentStatus.Cancelled),
            today.Select(a => a.ToDto(clock)).ToList(),
            recent.Select(p => p.ToListDto(todayLocal)).ToList(),
            counts,
            alerts);
    }
}
