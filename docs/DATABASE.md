# Banco de dados (SQL Server)

Tabelas (Etapa 1): Organizations, Doctors, Patients, Appointments, AuditLogs + tabelas do Identity (AspNetUsers com `OrganizationId`, `FullName`, `Active`).

- Enums gravados como texto. `Patients.Cpf` (char 11, somente dígitos) é **único por organização**.
- FKs com `Restrict`. `Appointments`: CHECK de duração 5–240 min.
- Índices: `OrganizationId`, `(OrganizationId, Cpf)` único, `ResponsibleDoctorId`, `CreatedAt`, `PatientId`, `(DoctorId, StartsAtUtc)`, `(OrganizationId, StartsAtUtc)`, `AuditLogs(OccurredAt)`.
- `CreatedAt/UpdatedAt` preenchidos automaticamente no `SaveChanges`.
- Os módulos clínicos das etapas seguintes descritos abaixo já estão incluídos. Planos e assinaturas comerciais permanecem futuros.

## Etapa 3
Tabelas: ClinicalNotes (SOAP, status, SignedAtUtc), ClinicalNoteAddenda, Medications, SymptomRecords (CHECK intensidade 0–10), SeizureEvents (CHECK duração 1–86400 s).
Todas com `OrganizationId` indexado, `CreatedAt` indexado e índices `(PatientId, OccurredAtUtc)` para o histórico. FKs `Restrict` (prontuário não é apagado em cascata).

## Etapa 4 (parte 1)
FallEvents (circunstância, lesão, atendimento) e QuestionnaireResponses (chave do instrumento, respostas em JSON, pontuação, faixa, SafetyFlag).
Índice `(OrganizationId, SafetyFlag, AnsweredAtUtc)` apoia o alerta do dashboard. Definições dos instrumentos ficam em código (`QuestionnaireCatalog`).

## Etapa 4 (partes 2 e 3)
PatientDocuments: metadados (título, categoria, nome original sanitizado, tipo detectado, tamanho, SHA-256, `StorageKey` único, exclusão lógica). O binário fica no armazenamento de arquivos (`Storage:RootPath`), fora do banco.

## Etapa 5 e migrations

`ClinicQuestionnaires` guarda definições por organização; `ConsentRecords` guarda consentimentos versionados; `NotificationDispatches` registra envios/idempotência. A migration inicial `20261002232309_Etapa5NotificationsQuestionnairesLgpd` cria o esquema completo (23 tabelas, incluindo Identity).

A atualização `20261007023438_PreserveQuestionnaireHistory` adiciona `QuestionnaireResponses.DefinitionSnapshotJson` (`nvarchar(max)`, anulável). Respostas novas armazenam a definição utilizada. Antes de uma edição de questionário da clínica, respostas legadas sem cópia recebem a definição anterior no mesmo SaveChanges. O filtro de organização limita o preenchimento ao tenant atual. Respostas legadas de instrumentos internos continuam usando o catálogo quando não possuem cópia.

O startup utiliza `MigrateAsync` quando `Database:AutoInitialize=true`; não utiliza `EnsureCreated`. Bancos legados sem histórico de migrations exigem baseline planejado. Consulte o README antes de atualizar um banco existente.
