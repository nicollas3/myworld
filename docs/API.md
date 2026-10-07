# API (somente leitura)

Autenticação: cookie do Identity. Falhas de autenticação/autorização devolvem status 401/403 para `/api`, sem redirecionar para HTML; o cookie não gera corpo JSON nessas respostas. JWT para mobile está no roadmap.

- `GET /api/patients?term=` — perfil `ClinicalStaff`; retorna `PatientListItemDto[]`
- `GET /api/patients/{id}` — `PatientDetailsDto` (CPF mascarado)
- `GET /api/appointments?from=&to=&doctorId=` — clínicos veem a organização; paciente vê apenas as próprias

Erros: `ProblemDetails` (404/403) e `ValidationProblemDetails` (400). A API nunca retorna entidades do EF.
Endpoints de escrita e APIs dos demais módulos permanecem futuros. Os módulos clínicos já incluídos nesta entrega são acessados pelos controllers MVC; sua existência não implica uma API pública correspondente.
