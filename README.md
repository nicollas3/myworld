# NeuroCare

Aplicação SaaS multi-tenant em C#/.NET 10, ASP.NET Core MVC/Razor, Identity, EF Core e SQL Server. Esta entrega reúne os módulos até a Etapa 5 e as correções descritas em [docs/ALTERACOES.md](docs/ALTERACOES.md).

## Estrutura

- `src/NeuroCare.Domain`: entidades e regras de domínio.
- `src/NeuroCare.Application`: serviços, DTOs e contratos.
- `src/NeuroCare.Infrastructure`: EF Core, repositórios, migrations, armazenamento e e-mail.
- `src/NeuroCare.Web`: páginas MVC/Razor, APIs e worker de lembretes.
- `tests/NeuroCare.Tests`: testes xUnit com EF InMemory e testes HTTP com TestServer.

## Preparação

Requisitos: SDK .NET 10 e SQL Server acessível. Na raiz da solução:

```bash
dotnet restore NeuroCare.sln
dotnet build NeuroCare.sln --no-restore
dotnet test NeuroCare.sln --no-build --no-restore
dotnet tool restore
```

O manifesto `.config/dotnet-tools.json` instala `dotnet-ef` 10.0.12. As referências Microsoft do projeto usam a faixa 10.0.*; mantenha a ferramenta compatível com a versão EF restaurada ao atualizar dependências.

Configure a conexão real por User Secrets ou pela variável `ConnectionStrings__DefaultConnection`. Não versione senhas, `.env` ou arquivos de configuração com credenciais reais. A conexão integrada de `appsettings.Development.json` deve ser adaptada ao seu ambiente, especialmente em Linux. Para o `dotnet-ef`, também é possível definir `NEUROCARE_CONNECTION` (somente design-time).

## Banco e atualização desta entrega

```bash
dotnet ef database update --project src/NeuroCare.Infrastructure --startup-project src/NeuroCare.Web
```

As migrations incluídas são:

1. `20261002232309_Etapa5NotificationsQuestionnairesLgpd`: criação inicial de todo o esquema, com 23 tabelas.
2. `20261007023438_PreserveQuestionnaireHistory`: adiciona `QuestionnaireResponses.DefinitionSnapshotJson`, anulável, sem apagar registros.

Se o banco já registra a primeira migration em `__EFMigrationsHistory`, aplique apenas a atualização pendente pelo comando acima. Um banco criado anteriormente por `EnsureCreated` exige avaliação e baseline compatível antes de usar migrations; não aplique a migration inicial sobre tabelas existentes nem apague dados para contornar conflitos. Faça backup antes de atualizar um banco em uso.

`Database:AutoInitialize=true` chama `MigrateAsync` na inicialização. Use atualização controlada e mantenha essa opção desabilitada em produção. A entrega também contém o script SQL incremental em `analise/migration-preserve-questionnaire-history.sql`, quando distribuída no ZIP; ele parte da migration inicial já aplicada.

Respostas novas guardam a definição do instrumento utilizada. Antes de editar um questionário da clínica, respostas antigas ainda sem essa cópia recebem a definição anterior, dentro da mesma gravação. Mudanças realizadas antes desta atualização não podem ser reconstruídas automaticamente.

## Execução local

```bash
dotnet run --project src/NeuroCare.Web --launch-profile https
```

Acesse `https://localhost:7180` ou `http://localhost:5180`. Para HTTPS local, configure o certificado de desenvolvimento do .NET conforme seu sistema.

O Compose é uma alternativa para desenvolvimento local:

```bash
cp .env.example .env
# Preencha MSSQL_SA_PASSWORD e SEED_DEMO_PASSWORD no .env.
docker compose up --build
```

A aplicação fica em `http://localhost:8080`. O Compose habilita ambiente Development, seed fictício e log de e-mails. Ajuste a implantação antes de uso real. Os volumes persistem banco e documentos.

## Configurações

Variáveis de ambiente usam `__` no lugar de `:` (por exemplo, `Seed__Enabled`).

| Chave | Uso |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | Conexão do SQL Server. |
| `Database:AutoInitialize` | Aplicação de migrations no startup; padrão geral false. |
| `Seed:Enabled` / `Seed:DemoPassword` | Dados fictícios e senha das contas locais. |
| `App:PublicBaseUrl` | Origem dos links de e-mail; obrigatória fora de Development. |
| `Email:From` / `Email:Smtp:*` | Remetente e servidor SMTP. |
| `Email:UseLogSender` | Registra links de e-mail no log; habilite apenas localmente. |
| `Storage:RootPath` | Diretório dos documentos fora de `wwwroot`. |
| `Storage:MaxFileBytes` | Limite de arquivo; padrão 10 MiB. |
| `Notifications:Enabled` | Ativa o worker de lembretes. |
| `Notifications:WorkerIntervalSeconds` | Intervalo de varredura; padrão 60 s. |
| `Notifications:AppointmentLeadHours` | Antecedência dos lembretes de consulta; padrão 24 h. |
| `Notifications:RequireConsent` | Exige consentimento para lembretes; padrão true. |

O arquivo Development fornecido contém uma senha explicitamente fictícia de demonstração. Altere ou remova antes de usar dados reais. Contas locais: `admin@neurocare.local`, `clinica@neurocare.local`, `medico@neurocare.local`, `medico.b@neurocare.local` e `paciente@neurocare.local`. A senha é a configurada em `Seed:DemoPassword`.

## Módulos e acesso

- Administrator: administração da plataforma, sem organização e sem acesso clínico.
- ClinicAdmin: gestão da clínica, usuários, pacientes, consultas e definições de questionários; sem acesso ao prontuário.
- Doctor: acompanhamento clínico dos pacientes da organização.
- Patient: portal com dados próprios, registros e consentimentos.

Implementados: agenda, pacientes, evoluções SOAP e adendos, medicamentos com horários de lembrete editáveis, sintomas, crises, quedas, instrumentos internos e questionários por clínica, documentos, timeline, relatórios com impressão pelo navegador, alertas e lembretes por e-mail, consentimentos e exportação LGPD. Os módulos clínicos do profissional são acessados a partir do paciente; definições da clínica possuem item próprio no menu. O paciente acessa quedas, questionários, documentos e LGPD pelo menu.

O histórico de questionários preserva perguntas, opções, título, pontuação máxima e restrição de aplicação profissional da versão respondida. Os títulos também são usados em dashboard, timeline e relatório. A exportação LGPD inclui a definição histórica quando disponível.

## Validação e limites

Consulte [docs/ALTERACOES.md](docs/ALTERACOES.md) e, no ZIP, `analise/validacao-atualizada.md` e o resultado TRX. Os testes usam banco em memória e servidor HTTP de testes. A migration foi gerada pelo EF, seu script SQL foi conferido e o modelo verificado; não foi aplicada a um SQL Server real neste ambiente. Envio SMTP real e paginação visual da impressão também não foram validados. A deduplicação SMTP possui a janela de falha descrita em `docs/ALTERACOES.md`.

Antes de produção ainda são necessários, entre outros, revisão de implantação e segurança, estratégia de backups, 2FA, proteção das dependências CDN e avaliação de antivírus/criptografia do armazenamento. Assinaturas clínicas são bloqueios lógicos, não certificados digitais. Mobile, planos/assinaturas comerciais e IA assistiva permanecem no roadmap. Os instrumentos internos não substituem diagnóstico clínico; instrumentos sujeitos a licença devem ser avaliados antes de inclusão.

Mais detalhes: [arquitetura](docs/ARCHITECTURE.md), [banco](docs/DATABASE.md), [segurança](docs/SECURITY.md), [roadmap](docs/ROADMAP.md) e [APIs](docs/API.md).
