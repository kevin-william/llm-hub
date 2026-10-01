# 11 — Artefatos e sessões persistentes

> **Status:** Em andamento  
> **Progresso:** 60%  
> **Dependências:** 04, 10  
> **Commit único:** `feat(artifacts): persistir resultados e sessões de agentes`

## 1. Por que

Permite que resultados grandes e arquivos não sobrecarreguem eventos, e torna a associação entre conversa e sessão OpenCode recuperável após reinícios.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
O OpenCode Worker:

1. pede um trabalho ao Hub;
2. recebe um lease temporário;
3. cria ou recupera a sessão OpenCode relacionada ao canal;
4. envia o prompt;
5. mantém heartbeats;
6. coleta resposta e artefatos;
7. comunica a conclusão ao Hub.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Infrastructure/Artifacts/*` | Criar storage S3/MinIO e metadados |
| Produção | `src/LlmHub.Application/Agents/*` | Criar associação canal-sessão e conclusão com artefatos |
| Teste | `tests/LlmHub.IntegrationTests/Artifacts/*` | Criar testes MinIO/Testcontainers |
| Documentação | `docs/artefatos-e-sessoes.md` | Criar referências, limites e recuperação |

## 4. Como implementar

1. Guardar bytes em storage compatível com S3/MinIO e persistir apenas metadados, checksum, tamanho, tipo e autorização no PostgreSQL.
2. Persistir a associação `channel_id → sessão do agente` e usá-la para recuperação; a conclusão grava resposta, artefatos, estado e outbox na mesma transação.
3. Expor referências de artefatos ao worker e leitura autorizada pela API; conteúdo grande é buscado fora de webhooks.

## 5. Testes no mesmo commit

- Resposta com artefato persiste metadados, conteúdo e associação de sessão.
- Reinício recupera a sessão do canal e não duplica artefatos por complete idempotente.
- Limites de tipo/tamanho e checksum inválido são rejeitados.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Artifacts
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer MinIO/S3 de teste e política inicial de tamanho.
- Fora de escopo: URLs temporárias públicas, retenção definitiva e antivírus; estes dependem da tarefa 18.

## 8. Critérios de aceitação

- [ ] Sessões e artefatos persistentes implementados
- [ ] Testes de recuperação e limites executados
- [ ] Armazenamento documentado
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Storage S3/MinIO com hash, checksum, tipos e limite de tamanho, sessão por canal/adapter e persistência transacional de metadados na conclusão implementados; leitura autorizada pela API pendente |
| Testes | Escrita/leitura real em MinIO, checksum inválido, recuperação da sessão e complete idempotente cobertos |
| Documentação | Configuração e limites do storage documentados |
| Validação do usuário | Pendente |

**Progresso geral:** 60%. Falta expor leitura autorizada pela API junto da camada de tenant.
