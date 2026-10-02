# Plano executável — Hub assíncrono de mensagens e execuções

Plano-fonte: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

## Regra de atomicidade

Cada tarefa deve caber em um único commit convencional e entregar comportamento utilizável, testes automatizados e documentação diretamente afetada. Nenhuma tarefa separa código de produção dos testes desse código. As duas provas de risco são exceções deliberadas: mantêm o comportamento isolado e documentam o resultado reproduzível antes de acoplar o restante do Hub.

## Ordem e progresso

| # | Tarefa | Depende de | Commit | Estado |
|---|---|---|---|---|
| 01 | [Base da solução](01-base-solucao.md) | — | `chore(bootstrap): criar estrutura modular do hub` | Concluída |
| 02 | [Prova MCP Events](02-prova-mcp-events.md) | 01 | `feat(events): validar assinatura e retomada MCP` | Em andamento — validação com ChatGPT pendente |
| 03 | [Domínio e contratos](03-dominio-contratos.md) | 01 | `feat(domain): modelar canais mensagens e runs` | Concluída |
| 04 | [Persistência e outbox](04-persistencia-outbox.md) | 03 | `feat(persistence): persistir lifecycle e outbox` | Em andamento — idempotência e concorrência PostgreSQL validadas |
| 05 | [API HTTP principal](05-api-http-principal.md) | 04 | `feat(api): expor canais mensagens e runs` | Concluída |
| 06 | [Outbox para Redis](06-redis-outbox.md) | 04 | `feat(queue): publicar outbox em redis streams` | Em andamento |
| 07 | [Gateway e leases de worker](07-gateway-workers.md) | 05, 06 | `feat(workers): adicionar claims leases e heartbeats` | Em andamento — claim concorrente PostgreSQL validado |
| 08 | [Retry, recuperação e worker fake](08-resiliencia-worker-fake.md) | 07 | `feat(workers): recuperar leases e tratar retries` | Em andamento — reinício Redis/PostgreSQL validado |
| 09 | [Compatibilidade do OpenCode](09-prova-opencode.md) | 01 | `docs(opencode): registrar contrato da integração alvo` | Em andamento |
| 10 | [Worker OpenCode](10-worker-opencode.md) | 07, 09 | `feat(opencode): executar runs por adapter local` | Em andamento — adapter concreto depende da versão-alvo |
| 11 | [Artefatos e sessões](11-artefatos-sessoes.md) | 04, 10 | `feat(artifacts): persistir resultados e sessões de agentes` | Em andamento — storage S3/MinIO e upload de worker implementados |
| 12 | [Inscrições e entrega de eventos](12-inscricoes-webhooks.md) | 02, 04 | `feat(events): entregar notificações assinadas` | Em andamento — validação MCP externa pendente |
| 13 | [Servidor e ferramentas MCP](13-ferramentas-mcp.md) | 05, 12 | `feat(mcp): expor ferramentas e eventos do hub` | Em andamento — inscrições persistentes implementadas; validação externa pendente |
| 14 | [Fluxo ponta a ponta](14-fluxo-ponta-a-ponta.md) | 10, 11, 12, 13 | `test(e2e): cobrir fluxo chatgpt opencode chatgpt` | Em andamento — fluxo controlado validado |
| 15 | [Autorização e isolamento](15-autorizacao-tenancy.md) | 13 | `feat(security): aplicar oauth escopos, tenancy e identidade de worker` | Em andamento — isolamento por participação, tenant e identidade de worker implementados |
| 16 | [Proteções de webhook](16-seguranca-webhook.md) | 12 | `feat(security): endurecer callbacks e segredos` | Em andamento — rotação de chaves e proteções locais implementadas |
| 17 | [Observabilidade e auditoria](17-observabilidade-auditoria.md) | 14 | `feat(observability): rastrear operações e decisões` | Em andamento — correlação e auditoria implementadas |
| 18 | [Operação e confiabilidade](18-operacao-confiabilidade.md) | 15, 16, 17 | `feat(operations): aplicar quotas retenção e recuperação` | Em andamento — quotas, retenção e recuperação controlada implementadas |
| 19 | [SDK e roteamento generalizado](19-sdk-adapters-roteamento.md) | 18 | `feat(adapters): generalizar workers e roteamento` | Em andamento — Echo, destino persistido e seleção determinística por capacidades implementados |

## Cadeia de dependências

`01 → {02, 03, 09}`; `03 → 04 → {05, 06, 11}`; `{05, 06} → 07 → {08, 10}`; `{02, 04} → 12`; `{05, 12} → 13`; `{10, 11, 12, 13} → 14`; `14 → 17`; `{13, 12} → {15, 16}`; `{15, 16, 17} → 18 → 19`.

## Portões externos

- A prova MCP Events depende de um ambiente ChatGPT/plugin que aceite MCP 2.0 e callbacks HTTPS públicos.
- A integração do OpenCode depende de uma versão-alvo instalada e de sua interface efetivamente suportada.
- PostgreSQL, Redis e um armazenamento compatível com S3/MinIO são dependências de integração.
- OAuth/OIDC, chaves de criptografia e a política de retenção exigem decisões operacionais antes da produção.

## Progresso agregado

**3 de 19 tarefas estão integralmente concluídas; as demais têm implementação parcial ou aguardam validações externas.** A suíte atual soma 85 testes aprovados (11 unitários, 68 de integração e 6 ponta a ponta). PostgreSQL, Redis e MinIO locais estão disponíveis para as validações de integração já automatizadas.
