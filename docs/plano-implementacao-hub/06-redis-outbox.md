# 06 — Publisher de outbox para Redis Streams

> **Status:** Em andamento  
> **Progresso:** 75%  
> **Dependências:** 04  
> **Commit único:** `feat(queue): publicar outbox em redis streams`

## 1. Por que

Transforma eventos persistidos em trabalho transportado sem tornar Redis uma fonte de verdade. O contrato de fila fica isolado para permitir troca futura de broker.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Redis Streams como transporte

Streams sugeridos:

```text
hub:runs:opencode
hub:runs:<outro-adapter>
hub:webhook-deliveries
```

Usar consumer groups, acknowledgements e recuperação de mensagens pendentes.

Não usar Redis Pub/Sub para jobs importantes: mensagens são perdidas quando consumidores estão offline.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Contracts/Queues/IRunQueue.cs` | Criar contrato |
| Produção | `src/LlmHub.Infrastructure/Redis/{RedisRunQueue,OutboxPublisher}.cs` | Criar implementação e worker |
| Teste | `tests/LlmHub.IntegrationTests/Redis/*` | Criar testes com Redis Testcontainers |
| Documentação | `docs/redis-streams.md` | Criar topologia, chaves e recuperação |

## 4. Como implementar

1. Definir `IRunQueue` sem tipos do StackExchange.Redis e publicar por adapter em `hub:runs:<adapter>`.
2. Criar BackgroundService que busca eventos pendentes, publica conteúdo mínimo com `event_id`/`run_id` e só então marca publicação; republicação preserva `event_id`.
3. Configurar consumer groups, acknowledgements e observabilidade da idade da outbox. Não permitir que consumidor assuma entrega exatamente uma vez.

## 5. Testes no mesmo commit

- Evento pendente é publicado no stream correto e marcado somente após XADD bem-sucedido.
- Interrupção após XADD republica o mesmo evento; consumidor deduplica pela identidade.
- Consumer group reconhece mensagem e recupera pendência conforme contrato.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Redis
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer Redis com Streams habilitado.
- Fora de escopo: claim HTTP, retries de execução e dispatcher de webhook.

## 8. Critérios de aceitação

- [ ] Fila abstraída e publisher implementado
- [ ] Testes de publicação e republicação executados
- [ ] Topologia Redis documentada
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Contrato, Redis Streams e publisher configurável concluídos |
| Testes | Aprovados com fila fake e Redis Streams real para publicação no stream do adapter; consumer groups e recuperação de pendências pendentes |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 85%. Ainda faltam consumer groups, acknowledgements e recuperação de mensagens pendentes.
