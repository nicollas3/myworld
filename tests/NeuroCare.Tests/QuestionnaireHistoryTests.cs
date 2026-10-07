using Microsoft.EntityFrameworkCore;
using NeuroCare.Application;
using NeuroCare.Domain;
using NeuroCare.Infrastructure;

namespace NeuroCare.Tests;

public class QuestionnaireHistoryTests
{
    private static SaveClinicQuestionnaireDto Form(string title = "Sono original") => new()
    {
        Key = "sono-clinica", Title = title, Description = "Acompanhamento do sono",
        Items = [new() { Text = "Pergunta original", Options = "0=Não;1=Sim" }],
        Bands = [new() { Min = 0, Max = 1, Label = "Faixa original" }],
        SafetyItemNumber = 1
    };

    private static ClinicQuestionnaireService Definitions(ClinicalEnv env)
    {
        var db = env.Base.Db;
        var audit = new AuditService(new AuditLogRepository(db), env.Base.User, env.Base.Clock, db);
        return new(new ClinicQuestionnaireRepository(db), db, new AccessGuard(env.Base.User), audit,
            new QuestionnaireResponseRepository(db));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task Score_bands_require_continuous_non_overlapping_coverage(int secondMin, bool valid)
    {
        var database = Guid.NewGuid().ToString();
        var scenario = await TestEnv.SeedAsync(database);
        using var env = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, scenario.OrgA, scenario.DoctorAUserId));
        var form = Form();
        form.Items[0].Options = "0=Nunca;1=Às vezes;2=Sempre";
        form.Bands = [new() { Min = 0, Max = 0, Label = "Baixa" }, new() { Min = secondMin, Max = 2, Label = "Alta" }];
        if (valid)
            Assert.NotEqual(Guid.Empty, await Definitions(env).CreateAsync(form));
        else
        {
            await Assert.ThrowsAsync<RequestValidationException>(() => Definitions(env).CreateAsync(form));
            Assert.Empty(await env.Base.Db.ClinicQuestionnaires.ToListAsync());
        }
    }

    [Fact]
    public async Task Editing_definition_preserves_old_answers_titles_and_maximum_in_all_views()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        using var env = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId));
        var definitions = Definitions(env);
        var id = await definitions.CreateAsync(Form());
        var old = await env.Questionnaires.SubmitAsync(s.PatientA1, "sono-clinica", new Dictionary<int, int> { [0] = 1 });

        var changed = Form("Sono revisado");
        changed.Items = [new() { Text = "Pergunta diferente", Options = "0=Nunca;2=Frequentemente" }];
        changed.Bands = [new() { Min = 0, Max = 2, Label = "Nova faixa" }];
        await definitions.UpdateAsync(id, changed);
        env.Base.Db.ChangeTracker.Clear();

        var result = await env.Questionnaires.GetResultAsync(old.Id);
        Assert.Equal("Sono original", result.Title);
        Assert.Equal(1, result.MaxScore);
        Assert.Equal("Pergunta original", Assert.Single(result.Answers).Question);
        Assert.Equal("Sim", Assert.Single(result.Answers).Answer);
        Assert.Equal("Faixa original", result.BandLabel);
        Assert.Equal("Sono original", Assert.Single(await env.Questionnaires.ListResponsesAsync(s.PatientA1, null, 365)).Title);
        Assert.Equal("Sono original", Assert.Single((await env.Reports.GetPatientSummaryAsync(s.PatientA1, 90)).Questionnaires).Title);
        Assert.Contains(await env.Timeline.GetAsync(s.PatientA1), x => x.Title == "Questionário: Sono original");
        Assert.Equal("Sono original", Assert.Single((await env.Dashboard.GetAsync()).SafetyAlerts).QuestionnaireTitle);

        var next = await env.Questionnaires.SubmitAsync(s.PatientA1, "sono-clinica", new Dictionary<int, int> { [0] = 2 });
        Assert.Equal("Sono revisado", next.Title);
        Assert.Equal(2, next.MaxScore);
        Assert.Equal("Frequentemente", Assert.Single(next.Answers).Answer);
        Assert.Equal("Sono original", (await env.Questionnaires.GetResultAsync(old.Id)).Title);
    }

    [Fact]
    public async Task Editing_backfills_legacy_responses_only_in_its_organization()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        Guid id, ownResponse;
        using (var doctor = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId)))
        {
            id = await Definitions(doctor).CreateAsync(Form());
            ownResponse = (await doctor.Questionnaires.SubmitAsync(s.PatientA1, "sono-clinica", new Dictionary<int, int> { [0] = 1 })).Id;
        }
        Guid otherResponse;
        using (var system = new TestEnv(database, FakeCurrentUser.System()))
        {
            var own = await system.Db.QuestionnaireResponses.IgnoreQueryFilters().SingleAsync(x => x.Id == ownResponse);
            own.DefinitionSnapshotJson = null;
            var other = new QuestionnaireResponse
            {
                OrganizationId = s.OrgB, PatientId = s.PatientB1, QuestionnaireKey = "sono-clinica",
                AnswersJson = "[1]", TotalScore = 1, BandLabel = "Outra organização", AnsweredAtUtc = system.Clock.UtcNow
            };
            otherResponse = other.Id;
            system.Db.QuestionnaireResponses.Add(other);
            await system.Db.SaveChangesAsync();
        }
        using (var doctor = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId)))
        {
            await Definitions(doctor).UpdateAsync(id, Form("Título modificado"));
            Assert.Equal("Sono original", (await doctor.Questionnaires.GetResultAsync(ownResponse)).Title);
            await Definitions(doctor).UpdateAsync(id, Form("Outra edição"));
            Assert.Equal("Sono original", (await doctor.Questionnaires.GetResultAsync(ownResponse)).Title);
        }
        using var inspect = new TestEnv(database, FakeCurrentUser.System());
        Assert.NotNull((await inspect.Db.QuestionnaireResponses.IgnoreQueryFilters().SingleAsync(x => x.Id == ownResponse)).DefinitionSnapshotJson);
        Assert.Null((await inspect.Db.QuestionnaireResponses.IgnoreQueryFilters().SingleAsync(x => x.Id == otherResponse)).DefinitionSnapshotJson);
    }

    [Fact]
    public async Task Inactive_definition_keeps_history_but_rejects_new_responses()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        using var env = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId));
        var service = Definitions(env);
        var id = await service.CreateAsync(Form());
        var response = await env.Questionnaires.SubmitAsync(s.PatientA1, "sono-clinica", new Dictionary<int, int> { [0] = 0 });
        var inactive = Form(); inactive.Active = false;
        await service.UpdateAsync(id, inactive);
        Assert.Equal("Sono original", (await env.Questionnaires.GetResultAsync(response.Id)).Title);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Questionnaires.SubmitAsync(s.PatientA1, "sono-clinica", new Dictionary<int, int> { [0] = 0 }));
    }

    [Fact]
    public async Task Legacy_built_in_response_remains_readable_without_snapshot()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        using var env = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Patient, s.OrgA, s.PatientA1UserId));
        var result = await env.Questionnaires.SubmitAsync(null, "gad7", Enumerable.Range(0, 7).ToDictionary(x => x, _ => 1));
        var stored = await env.Base.Db.QuestionnaireResponses.SingleAsync();
        stored.DefinitionSnapshotJson = null;
        await env.Base.Db.SaveChangesAsync();
        var legacy = await env.Questionnaires.GetResultAsync(result.Id);
        Assert.Equal(QuestionnaireCatalog.Gad7.Title, legacy.Title);
        Assert.Equal(7, legacy.Answers.Count);
        Assert.Equal(21, legacy.MaxScore);
    }

    [Fact]
    public async Task Snapshot_preserves_professional_access_restriction_after_definition_changes()
    {
        var database = Guid.NewGuid().ToString();
        var s = await TestEnv.SeedAsync(database);
        Guid responseId;
        using (var doctor = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Doctor, s.OrgA, s.DoctorAUserId)))
        {
            var form = Form(); form.ClinicianOnly = true;
            var id = await Definitions(doctor).CreateAsync(form);
            responseId = (await doctor.Questionnaires.SubmitAsync(s.PatientA1, form.Key, new Dictionary<int, int> { [0] = 1 })).Id;
            form.ClinicianOnly = false;
            await Definitions(doctor).UpdateAsync(id, form);
        }
        using var patient = new ClinicalEnv(database, FakeCurrentUser.With(Roles.Patient, s.OrgA, s.PatientA1UserId));
        await Assert.ThrowsAsync<ForbiddenException>(() => patient.Questionnaires.GetResultAsync(responseId));
    }
}
