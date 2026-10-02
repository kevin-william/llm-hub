# 16 — Proteções de callback e segredo de webhook

> **Status:** Em andamento  
> **Progresso:** 95%  
> **Dependências:** 12  
> **Commit único:** `feat(security): endurecer callbacks e segredos`

## 1. Por que

Torna segura a entrega externa já funcional: protege segredos em repouso, bloqueia destinos perigosos e garante assinatura e tempo válidos sem alterar as regras de delivery.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Webhooks

Implementar:

- somente HTTPS;
- desafio de verificação antes de ativar;
- Standard Webhooks/HMAC;
- timestamp com tolerância limitada;
- `eventId` estável durante retries;
- bloqueio de redirects;
- bloqueio de IPs privados, loopback e metadata services;
- nova validação DNS antes de cada conexão;
- timeout curto;
- limite de payload;
- segredos criptografados.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Infrastructure/Webhooks/{CallbackPolicy,WebhookSigner}.cs` | Criar validação e assinatura |
| Produção | `src/LlmHub.Infrastructure/Secrets/*` | Criar proteção/rotação de segredos |
| Teste | `tests/LlmHub.IntegrationTests/Security/WebhookSecurityTests.cs` | Criar testes SSRF e assinatura |
| Documentação | `docs/seguranca-webhooks.md` | Criar limites e resposta a incidentes |

## 4. Como implementar

1. Validar HTTPS, desafio, URI, resolução DNS antes de cada conexão e IP final; bloquear redirects, loopback, faixas privadas e metadata services.
2. Assinar os bytes exatos com Standard Webhooks/HMAC, usar `eventId` imutável e rejeitar timestamps fora da janela configurada.
3. Criptografar segredo com chave gerenciada/configurada, limitar payload a 256 KiB e configurar cliente HTTP tipado com timeout curto, backoff e jitter.

## 5. Testes no mesmo commit

- Callback HTTP, redirect, localhost, IP privado e DNS rebinding são recusados.
- Assinatura válida é verificável; segredo/timestamp adulterado falha; retry conserva `eventId` e bytes.
- Payload acima do limite não é enviado e transita para resultado rastreável.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~WebhookSecurity
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Exige mecanismo de chave em cada ambiente e decisão sobre rota de rotação.
- Fora de escopo: autorização OAuth, retenção de logs e política global de anexos.

## 8. Critérios de aceitação

- [ ] Política SSRF, assinatura e proteção de segredo implementadas
- [ ] Testes de callback hostil executados
- [ ] Limites e resposta a incidentes documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Assinatura, timestamp, timeout, bloqueio de redirects/proxy, política SSRF/DNS, segredo criptografado com AES-GCM e rotação por chave primária/anterior, desafio de callback e limite de payload concluídos |
| Testes | Concluídos para assinatura, segredo cifrado/adulterado, troca de chave, desafio recusado, estabilidade entre retries, payload excessivo e destinos hostis |
| Documentação | Catálogo atualizado em `docs/eventos-e-webhooks.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 95%. Falta validar o contrato de desafio contra consumidores externos reais.
