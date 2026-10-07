# Arquitetura

Solução em camadas com dependências apontando para dentro:

`Web → Application ← Infrastructure`, ambos → `Domain`.

- **Domain**: entidades, enums, regras (ex.: transições de `Appointment`, validação de CPF). Sem dependências externas.
- **Application**: DTOs, interfaces (repositórios, `ICurrentUser`, `IClock`), serviços/casos de uso, `AccessGuard`. Não conhece EF Core.
- **Infrastructure**: `NeuroCareDbContext` (Identity + Fluent API + filtros de tenant), repositórios, seed, relógio.
- **Web**: MVC/Razor, API, Identity, pipeline de segurança. Controllers só falam com serviços.

Decisões:
1. **Tenant por linha** (`OrganizationId`) com filtro global do EF Core, guarda no `SaveChanges` e checagem em serviço (defesa em profundidade).
2. **Um usuário = uma organização** na Etapa 1 (`ApplicationUser.OrganizationId`). Evolução: tabela de vínculo usuário↔organização.
3. **Administrator** da plataforma não tem organização e, portanto, não vê dados clínicos.
4. **Datas em UTC** no banco; conversão para America/Sao_Paulo via `IClock`.
5. DTOs de aplicação também servem de modelo de formulário nesta etapa (simplificação consciente).
6. Migrations versionadas: criação inicial do esquema e atualização incremental do histórico de questionários; ver README.

## Etapa 5 — extensões

### Notificações
- `ReminderWorker` executa em background e usa `IgnoreQueryFilters()` somente para varrer tenants; cada envio mantém `OrganizationId` e destinatário explícitos.
- `NotificationDispatch` deduplica ocorrências com envio registrado; SMTP e persistência não são atômicos (ver docs/ALTERACOES.md).
- O worker processa lotes ordenados, filtra organização/paciente ativos, valida o tenant do usuário destinatário e exige a versão atual do consentimento quando configurado.
- Alertas de segurança de questionário são enviados ao médico responsável após a resposta ser persistida; falha SMTP não desfaz o registro clínico.
- E-mails não carregam respostas do questionário nem detalhes clínicos além do mínimo necessário.

### Questionários por clínica
- `ClinicQuestionnaire` armazena definição JSON por organização.
- A chave é imutável após criação para preservar o vínculo com respostas históricas.
- Cada resposta nova registra a definição JSON utilizada em `DefinitionSnapshotJson`. Resultados, histórico, dashboard, timeline e relatório consultam essa cópia antes da definição atual.
- Ao editar uma definição, respostas legadas sem cópia recebem a definição anterior em uma única gravação com a alteração, respeitando o tenant atual. Não é possível recuperar versões perdidas antes desta entrega.
- Instrumentos internos do catálogo e instrumentos da clínica são resolvidos pelo mesmo `IQuestionnaireDefinitionProvider`.
- Questionários inativos deixam de aceitar novas respostas, mas permanecem resolvíveis no histórico.

### LGPD
- Consentimentos são versionados por finalidade.
- Exportação do titular é montada por `ILgpdExportRepository`, preservando a separação Application/Infrastructure.
