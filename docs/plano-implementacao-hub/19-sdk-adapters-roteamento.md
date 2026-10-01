# 19 — SDK de adapters e roteamento determinístico

> **Status:** Em andamento  
> **Progresso:** 60%  
> **Dependências:** 18  
> **Commit único:** `feat(adapters): generalizar workers e roteamento`

## 1. Por que

Prova que o núcleo não está acoplado ao OpenCode, adicionando um segundo adapter simples e deixando o roteamento inicial previsível e auditável.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Fase 6 — Generalização

Adicionar SDK para novos adapters:

```ts
register()
claim()
heartbeat()
complete()
fail()
```

Criar um segundo adapter simples para provar que o domínio não está acoplado ao OpenCode.

Somente depois adicionar roteamento por capacidade, como:
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Contracts/Adapters/*`, `src/LlmHub.Application/Routing/*` | Criar SDK e roteador determinístico |
| Produção | `src/LlmHub.Workers/Adapters/SampleAdapter/*` | Criar segundo adapter simples |
| Teste | `tests/LlmHub.IntegrationTests/Adapters/*` | Criar testes de contrato e roteamento |
| Documentação | `docs/adapter-sdk.md`, `docs/roteamento.md` | Criar guia de extensão e regras |

## 4. Como implementar

1. Publicar SDK/contratos para registro, claim, heartbeat, complete e fail sem depender de detalhes OpenCode ou Redis.
2. Implementar segundo adapter simples usando o mesmo gateway e executar o mesmo conjunto de testes de contrato do OpenCode adapter.
3. Roteamento escolhe endpoint por destino/capacidade permitida com decisão persistida e determinística; não introduzir roteador baseado em LLM.

## 5. Testes no mesmo commit

- Os dois adapters passam a mesma suíte de contrato e não vazam tipos específicos entre si.
- Roteamento por capacidade seleciona destino permitido e registra decisão; ambiguidade/indisponibilidade retorna erro controlado.
- Canal serial mantém a invariância de um run ativo mesmo para adapters distintos.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Adapters
dotnet test LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Definir capacidades e política de seleção antes de habilitar novos adapters externos.
- Fora de escopo: roteamento por LLM, automação autônoma e paralelismo implícito.

## 8. Critérios de aceitação

- [ ] SDK e segundo adapter implementados
- [ ] Testes de contrato e roteamento executados
- [ ] Guia de extensão documentado
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | SDK `IAgentAdapter`, adapter Echo e roteamento determinístico por destino implementados |
| Testes | Contrato do Echo e seleção de adapter no run cobertos |
| Documentação | Guia do SDK e regras de roteamento criados |
| Validação do usuário | Pendente |

**Progresso geral:** 60%. Faltam registro de capacidades por endpoint e a suíte de contrato compartilhada com uma integração OpenCode real.
