# Validação da entrega atualizada

- SDK .NET 10.0.401; dependências efetivamente restauradas em `resolved-package-versions.json`.
- Restore dos cinco projetos concluído.
- Build Debug: zero avisos e zero erros.
- xUnit: **130 testes executados, 130 aprovados, zero falhas e zero ignorados**. A versão original tinha 105 testes; foram acrescentados 25 casos.
- Testes HTTP com TestServer verificam formulário de medicação com antiforgery, navegação/LGPD do paciente, limite por IP, limite configurado do upload, textos de alerta e página de relatório.
- Testes de serviços verificam histórico de definições, preenchimento legado isolado por tenant, restrição profissional, inatividade, integração de títulos e faixas contínuas.
- Testes do worker verificam organização/paciente/usuário ativos, organização do destinatário, consentimento atual/revogado, deduplicação de envios concluídos e processamento além dos antigos limites de 1.000 consultas e 5.000 medicamentos.
- JavaScript: os cinco scripts próprios passaram em `node --check`.
- DOM com jsdom: login alterna a senha mantendo SVGs; redefinição continua funcional; alvos vazios/ausentes não geram erro; upload avisa excesso de tamanho e sinaliza envio.
- `dotnet tool restore` concluiu a instalação local de EF 10.0.12.
- `dotnet ef migrations has-pending-model-changes` confirmou modelo compatível com a última migration.
- Script SQL incremental gerado pelo EF e conferido: uma coluna anulável, sem remoção de dados, e registro em `__EFMigrationsHistory`.

## Limites da validação

EF InMemory não valida constraints e transações reais do SQL Server. A imagem do SQL Server não pôde ser baixada devido a bloqueio de rede; nenhum banco real recebeu a migration. SMTP usa fakes nos testes e não enviou e-mails reais. A deduplicação não elimina a janela entre aceitação SMTP e persistência de Sent, detalhada em `docs/ALTERACOES.md`.

O teste HTTP verifica que o relatório renderiza. Uma tentativa de gerar PDF com Chromium não concluiu neste ambiente; regras de impressão foram aplicadas, mas paginação visual e PDFs extensos ainda devem ser conferidos no navegador do usuário. Os testes DOM usam fragmentos reais das views e não equivalem a teste visual completo.

## Reexecutar

Na raiz da solução:

```bash
dotnet restore NeuroCare.sln
dotnet build NeuroCare.sln --no-restore
dotnet test NeuroCare.sln --no-build --no-restore
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/NeuroCare.Infrastructure --startup-project src/NeuroCare.Web --no-build
```

Opcionalmente, com Node.js:

```bash
cd analise/browser-check
npm install
npm run check
```

`neurocare-atualizado.trx` registra a execução atual. `neurocare-tests.trx` e `mapa-versao-original.md` registram a análise anterior às correções. `alteracoes.patch` e `arquivos-alterados.md` permitem revisar as diferenças.
