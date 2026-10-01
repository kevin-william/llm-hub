# 04 — Persistência transacional e outbox

> **Status:** Em andamento  
> **Progresso:** 85%  
> **Dependências:** 03  
> **Commit único:** `feat(persistence): persistir lifecycle e outbox`

## 1. Por que

Faz do PostgreSQL a fonte de verdade: gravação idempotente de mensagens e runs, sequência concorrente e outbox na mesma transação. O publisher é tratado separadamente para manter esta entrega reversível.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Outbox pattern

O Hub nunca deve fazer:

```text
COMMIT no banco
depois tentar publicar no Redis
```

sem uma outbox, pois uma queda entre os dois passos perderia o trabalho.

O correto:

1. salvar dados e outbox na mesma transação;
2. publisher lê a outbox;
3. publica no Redis;
4. marca como publicada;
5. consumidores deduplicam eventuais republicações.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Infrastructure/Persistence/*` | Criar DbContext, mapeamentos, repositórios e migrations |
| Produção | `src/LlmHub.Application/Commands/AcceptMessage/*` | Criar caso de uso transacional |
| Teste | `tests/LlmHub.IntegrationTests/Persistence/*` | Criar testes com PostgreSQL Testcontainers |
| Documentação | `docs/persistencia-e-outbox.md` | Criar invariantes, índices e procedimento de migration |

## 4. Como implementar

1. Mapear `principals`, `endpoints`, `channels`, `channel_participants`, `messages`, `runs`, `run_attempts`, `outbox_events`, `subscriptions`, `webhook_deliveries`, `agent_sessions` e `artifacts` somente até os campos já definidos; campos de funcionalidades posteriores podem ficar nulos.
2. Criar índices únicos para `client_message_id` por principal e sequência por canal; selecionar/atualizar de forma concorrente para preservar um run ativo por canal.
3. Implementar o comando que grava mensagem, decisão de rota, run `queued` e `run.queued` na outbox em uma transação. Reenvio com a mesma chave retorna o mesmo resultado lógico.

## 5. Testes no mesmo commit

- Dois reenvios concorrentes com a mesma chave produzem uma mensagem, um run e um evento.
- Falha antes do commit não deixa dados; falha simulada depois do commit preserva a outbox pendente.
- Migrations sobem em PostgreSQL limpo e índices rejeitam violações.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Persistence
dotnet ef database update --project src/LlmHub.Infrastructure --startup-project src/LlmHub.Api
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer PostgreSQL de desenvolvimento ou Testcontainers funcional.
- Fora de escopo: publicar Redis, endpoints HTTP e criptografia final de segredos (tarefa 16).

## 8. Critérios de aceitação

- [ ] Migrations e transação de aceitação implementadas
- [ ] Testes de idempotência e concorrência executados
- [ ] Persistência e outbox documentadas
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Modelo EF Core, aceitação transacional e migration inicial concluídos |
| Testes | Aprovados em banco efêmero e PostgreSQL Docker para idempotência; migrations aplicadas e conferidas; cenários concorrentes reais pendentes |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 90%
