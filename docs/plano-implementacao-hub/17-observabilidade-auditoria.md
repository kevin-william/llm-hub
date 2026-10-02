# 17 — Observabilidade e auditoria de decisões

> **Status:** Em andamento  
> **Progresso:** 70%  
> **Dependências:** 14  
> **Commit único:** `feat(observability): rastrear operações e decisões`

## 1. Por que

Oferece rastreabilidade do fluxo já integrado: uma falha pode ser ligada a canal, run, tentativa e entrega sem gravar por padrão prompts ou segredos sensíveis.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
Todos os componentes devem propagar:

```text
trace_id
channel_id
message_id
run_id
attempt_id
event_id
delivery_id
```
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Infrastructure/Observability/*` | Criar OpenTelemetry, métricas e redaction |
| Produção | `src/LlmHub.Application/Audit/*` | Criar eventos de auditoria de rota, claim e entrega |
| Teste | `tests/LlmHub.IntegrationTests/Observability/*` | Criar testes de propagação e redaction |
| Documentação | `docs/observabilidade.md` | Criar catálogo de métricas, alertas e campos |

## 4. Como implementar

1. Propagar todos os identificadores listados em traces, logs estruturados, outbox, worker e dispatcher.
2. Publicar métricas mínimas do plano para fila, duração, leases, retry, dead letter, subscriptions e webhooks.
3. Registrar auditoria de decisão de roteamento, mudanças de estado e entrega; aplicar redaction para prompts, tokens e chaves.

## 5. Testes no mesmo commit

- Uma solicitação ponta a ponta preserva correlação nos spans/logs/auditoria.
- Métricas são incrementadas para conclusão, retry e lease expirado.
- Logs não expõem segredo, token ou conteúdo de prompt configurado como sensível.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Observability
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Escolher exporter, retenção e destino de alertas por ambiente.
- Fora de escopo: SLOs finais, dashboards de produção e testes de carga da tarefa 18.

## 8. Critérios de aceitação

- [ ] Correlação, métricas e auditoria implementadas
- [ ] Testes de propagação e redaction executados
- [ ] Alertas e métricas documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | ActivitySource, Meter e auditoria persistida para rota, claim, conclusão, lease, entrega e recusa de quota implementados; claims incluem `attempt_id` |
| Testes | Correlação de `attempt_id`, auditoria de quota e redaction de prompt cobertas |
| Documentação | Catálogo de campos e métricas criado em `docs/observabilidade.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 70%. Exporter, retenção, dashboards e alertas por ambiente ainda dependem de decisão operacional.
