# 08 — Recuperação, retry e worker fake

> **Status:** Concluída com validação de infraestrutura pendente  
> **Progresso:** 90%  
> **Dependências:** 07  
> **Commit único:** `feat(workers): recuperar leases e tratar retries`

## 1. Por que

Fecha o primeiro ciclo executável com recuperação automática e um worker determinístico. O worker fake prova a semântica antes de integrar o OpenCode real.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Fase 2 — Redis e workers simulados

Implementar:

- publisher outbox → Redis;
- claims e leases;
- heartbeat;
- retry;
- dead letter;
- worker fake que responde automaticamente.

Critério de saída: reiniciar Hub e worker durante um run não perde a mensagem nem produz duas respostas aceitas.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Workers/{ExpiredLeaseReaper,RetryScheduler}.cs` | Criar |
| Produção | `src/LlmHub.Workers/FakeWorker/*` | Criar worker de desenvolvimento |
| Teste | `tests/LlmHub.EndToEndTests/WorkerRecoveryTests.cs` | Criar cenários de queda |
| Documentação | `docs/retries-dead-letter.md` | Criar política de tentativa e operação |

## 4. Como implementar

1. Reaper encontra leases vencidos de forma segura, registra tentativa e retorna apenas falhas transient a `retry_wait`, aplicando atraso configurável e limite de tentativas.
2. Ao esgotar tentativas, mover para `dead_letter` com causa auditável; falhas permanent, policy e cancelled não são reexecutadas automaticamente.
3. Implementar FakeWorker pelo mesmo HTTP público: ele faz claim, heartbeat e completa uma resposta determinística idempotente.

## 5. Testes no mesmo commit

- Queda do worker seguida de expiração reatribui o run e aceita uma única resposta final.
- Duplicatas de stream/complete não criam resposta duplicada.
- Falha transitória respeita retry; permanente e esgotada chegam a estado terminal esperado.

## 6. Validação

```bash
dotnet test tests/LlmHub.EndToEndTests --filter FullyQualifiedName~WorkerRecovery
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Workers
```

## 7. Portões e fora de escopo

- Requer PostgreSQL e Redis de teste; os cenários devem simular reinício sem depender de timing frágil.
- Fora de escopo: execução OpenCode e entrega de webhook.

## 8. Critérios de aceitação

- [ ] Recovery, retry e dead letter implementados
- [ ] Worker fake e testes de reinício executados
- [ ] Política operacional documentada
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Reaper, retry, dead letter e executor fake implementados |
| Testes | Recuperação de lease aprovada em banco efêmero; reinício Redis/PostgreSQL pendente |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 90%
