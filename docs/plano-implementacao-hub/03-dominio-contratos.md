# 03 — Domínio e contratos do Hub

> **Status:** Concluída  
> **Progresso:** 100%  
> **Dependências:** 01  
> **Commit único:** `feat(domain): modelar canais mensagens e runs`

## 1. Por que

Define as invariantes puras do produto antes de persistência ou transporte: canais seriais, mensagens imutáveis, correlação, limite de saltos e máquina de estados dos runs.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
#### Run

Execução criada para processar uma mensagem.

Estados:

```text
accepted
  → queued
  → leased
  → running
  → succeeded | failed | cancelled | timed_out
```

Estados internos adicionais:

```text
retry_wait
dead_letter
```
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Domain/{Channels,Messages,Runs,Endpoints}/*` | Criar agregados, value objects e transições |
| Produção | `src/LlmHub.Contracts/*` | Criar DTOs versionados e erros públicos |
| Teste | `tests/LlmHub.UnitTests/Domain/*` | Criar testes de invariantes |
| Documentação | `docs/decisoes/dominio-hub.md` | Criar glossário e diagrama de estados |

## 4. Como implementar

1. Modelar Endpoint, Channel, Message e Run com identificadores tipados, participantes, endereço do destinatário, sequência por canal e os campos de correlação indicados no plano.
2. Fazer Message imutável e concentrar transições válidas de Run em métodos do domínio; uma transição inválida retorna erro de domínio explícito.
3. Aplicar padrão serial com no máximo um run ativo por canal e `maxHops` padrão oito; não escolher roteamento por LLM nesta fase.

## 5. Testes no mesmo commit

- Aceitar a sequência normal e rejeitar cada transição de estado inválida.
- Impedir segundo run ativo, sequência repetida, hop acima do limite e alteração de mensagem.
- Cobrir raiz, resposta e causação de uma conversa.

## 6. Validação

```bash
dotnet test tests/LlmHub.UnitTests --filter FullyQualifiedName~Domain
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- A forma externa dos DTOs deve permanecer compatível com as APIs planejadas; ids concretos são decisão de implementação documentada.
- Fora de escopo: EF Core, HTTP, Redis, MCP, autenticação e execução de worker.

## 8. Critérios de aceitação

- [ ] Invariantes de canal, mensagem e run implementadas
- [ ] Testes de domínio executados
- [ ] Glossário e estados documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Concluída |
| Testes | Aprovados: 8 testes de domínio |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 100%
