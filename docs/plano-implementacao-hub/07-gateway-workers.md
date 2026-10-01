# 07 — Gateway de workers, claims e leases

> **Status:** Em andamento  
> **Progresso:** 85%  
> **Dependências:** 05, 06  
> **Commit único:** `feat(workers): adicionar claims leases e heartbeats`

## 1. Por que

Entrega o contrato que deixa workers externos executarem runs sem receber credenciais do Redis, com fencing token que impede conclusão de um lease antigo.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Heartbeat

```text
POST /v1/runs/{runId}/heartbeat
```

Sugestão inicial:

- lease de 60 segundos;
- heartbeat a cada 15 segundos;
- resposta inclui `cancel_requested`.

### Completar

```text
POST /v1/runs/{runId}/complete
```

Requer o `lease_token`. Um token antigo não pode finalizar um run reatribuído a outro worker.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Api/Endpoints/WorkerEndpoints.cs` | Criar registro, claim, heartbeat, complete e fail |
| Produção | `src/LlmHub.Application/Workers/*` | Criar casos de uso e validação de token |
| Teste | `tests/LlmHub.IntegrationTests/Workers/*` | Criar testes concorrentes |
| Documentação | `docs/worker-contract.md` | Criar contrato HTTP e tempos |

## 4. Como implementar

1. Registrar worker por id, adapter, capacidades, versão e concorrência; manter credenciais de Redis exclusivamente no Hub.
2. Claim consulta runs enfileirados de modo concorrente, cria tentativa e lease de 60 segundos com token não reutilizável; o retorno inclui contexto, referências e deadline.
3. Heartbeat renova lease e informa cancelamento. Complete e fail exigem token atual, gravam transição e outbox transacional; classificar falhas conforme o plano.

## 5. Testes no mesmo commit

- Worker registrado obtém um claim e heartbeat estende o lease.
- Dois workers concorrentes não reivindicam o mesmo run; token expirado/antigo não conclui.
- Falhas transient, permanent, policy e cancelled possuem resultados de domínio distintos.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Workers
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Os intervalos são valores iniciais configuráveis; mudanças operacionais devem ser documentadas.
- Fora de escopo: reaper, retry, worker concreto OpenCode e autenticação de produção.

## 8. Critérios de aceitação

- [ ] Contrato worker e lease com fencing implementados
- [ ] Testes concorrentes executados
- [ ] Contrato e intervalos documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Registro, claims, leases, heartbeat, complete e fail implementados |
| Testes | Fencing aprovado em banco efêmero; concorrência PostgreSQL pendente |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 85%
