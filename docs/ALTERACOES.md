# Alterações desta entrega

## Login

O botão de exibir senha agora aponta para `#Password`. O JavaScript ignora alvos vazios/ausentes e mantém os ícones SVG da tela de login ao alternar a visibilidade, o estado acessível e o texto de ajuda. A redefinição de senha mantém seu comportamento.

## Medicamentos

O formulário compartilhado de criação/edição expõe `ReminderEnabled` e `ReminderTimes` com validação. Uma edição de dose mantém os horários enviados pelo formulário; o usuário pode desabilitar explicitamente o lembrete. A listagem informa os horários quando habilitados. O envio continua sujeito às configurações e ao consentimento.

## Questionários e histórico

`QuestionnaireResponse.DefinitionSnapshotJson` registra a definição usada na resposta. Resultados e históricos usam essa versão, incluindo título, opções, pontuação máxima e restrição profissional. Dashboard, timeline e relatório resolvem também instrumentos da clínica. A exportação LGPD inclui a cópia histórica.

Antes de editar uma definição da clínica, o serviço preenche respostas legadas ainda sem cópia com a definição anterior, dentro do tenant atual e no mesmo SaveChanges da alteração. Questionários inativos continuam disponíveis para histórico, mas não para novas respostas. Alterações anteriores à atualização, sem definição histórica salva, não podem ser recuperadas automaticamente.

A migration `20261007023438_PreserveQuestionnaireHistory` adiciona apenas a coluna anulável à tabela de respostas. Atualize o banco conforme o README antes de executar a aplicação atualizada. O ZIP contém o script incremental gerado pelo EF para revisão.

## Navegação e instruções

O menu do paciente inclui quedas, questionários, documentos e LGPD. O menu profissional inclui as definições de questionários da clínica; os módulos por paciente permanecem acessíveis pelo cadastro do paciente. Placeholders “em breve” foram removidos. README, arquitetura, banco, segurança e roadmap foram alinhados ao código. O manifesto local permite restaurar `dotnet-ef`.

## Upload e impressão

A tela de upload carrega a validação de tamanho no cliente e usa o limite efetivo de `IFileStorage`. A validação de conteúdo/tamanho no servidor permanece obrigatória. O CSS de impressão oculta os menus e controles, reduz margens e orienta que títulos, linhas de tabela e seções do relatório permaneçam agrupados. A paginação final ainda depende do navegador e do conteúdo.

## Faixas, alertas e limite de acesso

Faixas de pontuação devem cobrir continuamente o intervalo, sem lacunas nem sobreposição. O rate limit de dez requisições por minuto é separado pelo IP da conexão; login, recuperação e redefinição compartilham a cota de cada IP. Atrás de proxy, a implantação deve configurar encaminhamento de IP apenas de proxies confiáveis; o código não confia diretamente em headers enviados pelo cliente.

Alertas de instrumentos personalizados utilizam mensagem genérica sobre o item de segurança. A orientação de crise do PHQ-9 para o paciente, incluindo CVV e SAMU, permanece disponível. O dashboard descreve o envio de alertas e lembretes sem afirmar ausência de contato automático.

## Lembretes

O worker consulta organizações e pacientes ativos, confirma que o usuário pertence à organização do paciente e exige consentimento concedido na versão atual do catálogo quando configurado. Consultas e medicamentos são percorridos em lotes ordenados de 1.000, sem o corte global anterior. Envios já registrados como Sent continuam deduplicados.

SMTP e a gravação de Sent não participam de uma transação distribuída: uma falha após aceitação do e-mail e antes da gravação pode produzir duplicata em nova tentativa. A eliminação dessa janela requer um provedor com idempotência de entrega e política operacional de reconciliação; este ZIP não promete entrega exatamente uma vez. Essa limitação foi registrada sem substituir falhas de envio por sucesso ou descartar lembretes silenciosamente.

## Verificação

Foram adicionados testes de preservação histórica, isolamento entre organizações no preenchimento legado, inatividade, restrição profissional, títulos nas demais telas e edição de lembretes. Testes HTTP exercitam o formulário Razor com antiforgery o menu do paciente, o limite por IP, o upload configurado e os textos de alerta. Testes do worker verificam consentimento, inatividade, destinatário, deduplicação e paginação além dos limites antigos. O script DOM verifica a alternância de senha usando o JavaScript e o botão reais das views.

Consulte `analise/validacao-atualizada.md` na entrega para os resultados executados. SQL Server, SMTP reais e paginação visual da impressão não foram validados neste ambiente. A análise original é um registro anterior às correções e mantém outras limitações para trabalho futuro; as limitações remanescentes de produção estão descritas acima e no README.
