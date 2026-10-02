# 15 — OAuth, escopos e isolamento por tenant

> **Status:** Em andamento  
> **Progresso:** 80%  
> **Dependências:** 13  
> **Commit único:** `feat(security): aplicar oauth escopos e tenancy`

## 1. Por que

Substitui o principal de desenvolvimento por identidade verificável e impede que operações, canais, mensagens e artefatos atravessem tenants.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Autenticação

- OAuth 2.1/OIDC para clientes MCP como ChatGPT;
- tokens de serviço curtos e rotacionáveis para workers;
- escopos separados: leitura, envio, cancelamento e administração;
- isolamento por tenant desde o modelo de dados.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Api/Authentication/*`, `Authorization/*` | Criar validação OIDC, políticas e identidade de worker |
| Produção | `src/LlmHub.Infrastructure/Persistence/*` | Aplicar tenant em consultas e migrations necessárias |
| Teste | `tests/LlmHub.IntegrationTests/Security/AuthorizationTests.cs` | Criar testes de escopo e isolamento |
| Documentação | `docs/autenticacao-autorizacao.md` | Criar fluxo, escopos e rotação |

## 4. Como implementar

1. Integrar OAuth 2.1/OIDC para MCP/API e tokens de serviço curtos para worker; validar issuer, audience, expiração e tenant.
2. Definir políticas `read`, `send`, `cancel` e `admin`, aplicadas às APIs, tools MCP e worker endpoints.
3. Fazer tenant parte obrigatória das chaves de acesso/repositorios e filtrar subscriptions, artefatos e deliveries pelo principal autorizado.

## 5. Testes no mesmo commit

- Tokens válidos recebem apenas operações de seu escopo; token ausente, expirado ou audience inválida falha.
- Tentativa cruzada de ler/enviar/cancelar entre tenants retorna não autorizado/não encontrado sem vazar dados.
- Worker não pode fazer claim/complete fora do seu tenant/registro.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Authorization
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer provedor OIDC, audiences, issuer e política de rotação aprovados para cada ambiente.
- Fora de escopo: criptografia de callbacks, quotas e auditoria detalhada.

## 8. Critérios de aceitação

- [ ] OAuth, escopos e tenancy aplicados
- [ ] Testes de autorização e acesso cruzado executados
- [ ] Fluxos de identidade documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Isolamento por participação na API HTTP e MCP, validação JWT/OIDC configurável, principal por `sub`, tenant persistido em canal/endpoint/worker, scopes por rota e vínculo `worker_id`→registro/run implementados; emissão rotacionável de tokens pendente |
| Testes | Leitura e cancelamento cruzados, inclusive via MCP, escopos, acesso entre tenants e isolamento de worker por registro/run cobertos |
| Documentação | Configuração OIDC e scopes documentados em `docs/autenticacao-autorizacao.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 80%. Faltam configurar o provedor OIDC por ambiente e a emissão rotacionável de tokens de serviço para workers.
