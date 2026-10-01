# Eventos e webhooks

O Hub publica `message.created` quando um worker conclui um run e grava a mensagem de resposta. O evento nasce na outbox com um `eventId` estável. O dispatcher encontra inscrições verificadas e não expiradas do mesmo canal e evento, cria uma delivery persistida e marca a outbox como processada.

O corpo enviado ao callback é pequeno e contém `eventId`, `eventName`, `occurredAt`, `cursor` e `data`. O cursor é composto pelo instante do evento e por seu identificador; ele permite que um consumidor registre a última entrega observada e peça uma retomada em uma futura interface de replay.

Cada delivery segue `pending → sending → accepted`. Qualquer resposta `2xx` a aceita. Respostas que não são `2xx` entram em `retry_wait` com atraso exponencial, conservando tanto o `eventId` quanto o corpo do evento. Após cinco tentativas, a delivery fica em `dead`. Uma resposta `410 Gone` marca a delivery como `dead` e desativa a inscrição, pois o destino informou que ela não existe mais.

O worker de entrega é habilitado com `Workers:EnableWebhookDispatcher=true`; ele lê o PostgreSQL periodicamente e envia apenas deliveries pendentes ou cujo retry venceu. Os testes de integração cobrem filtro por evento, criação idempotente de delivery, cursor, retry com corpo estável, aceite e desativação por `410`.

A etapa atual exige HTTPS na criação da inscrição. Cada delivery usa os cabeçalhos Standard Webhooks `webhook-id`, `webhook-timestamp` e `webhook-signature`; a assinatura é HMAC-SHA256 de `eventId.timestamp.payload`, e o timestamp não muda em retries. O cliente não usa proxy nem segue redirects; antes de enviar, resolve o destino e rejeita loopback, endereços privados, link-local e metadata services.

O segredo nunca é persistido em texto simples. A API requer `Webhook__SecretEncryptionKey`, uma chave aleatória de 32 bytes codificada em Base64, e usa AES-GCM com nonce aleatório para cada inscrição. Sem essa configuração, a criação de inscrição responde `503`, em vez de aceitar um segredo sem proteção. Gere uma chave por ambiente com `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))` no PowerShell e armazene-a no cofre de segredos do ambiente. A rotação exige manter uma chave anterior para decriptar inscrições existentes; esse fluxo ainda não foi implementado.

Antes de ativar uma inscrição, o Hub envia `POST` ao callback com o corpo `{ "type": "hub.webhook.challenge", "challenge": "..." }` e o cabeçalho `webhook-challenge`. O callback deve responder `2xx` e repetir exatamente o desafio nesse cabeçalho. O corpo de cada delivery é limitado a 256 KiB em UTF-8. Quando excede esse limite, a delivery fica registrada como `dead` com `payload_too_large` e nenhum callback é feito.
