# Operação, quotas e retenção

O Hub configura as quotas por `Hub__Quotas__MaxMessageBytes`, `Hub__Quotas__MaxAttachments`, `Hub__Quotas__MaxActiveRunsPerPrincipal`, `Hub__Quotas__MaxChannelsPerPrincipal` e `Hub__Quotas__MaxActiveSubscriptionsPerPrincipal`. Os valores padrão são 64 KiB, 16 anexos, 10 runs ativos, 100 canais e 20 callbacks ativos por principal. Rejeições retornam `MESSAGE_QUOTA_EXCEEDED`, `ATTACHMENT_QUOTA_EXCEEDED`, `RUN_QUOTA_EXCEEDED`, `CHANNEL_QUOTA_EXCEEDED` ou `CALLBACK_QUOTA_EXCEEDED` antes de criar trabalho ou chamar o callback. Cada recusa deixa uma auditoria sem conteúdo da mensagem, segredo ou token.

Com `Workers__EnableRetention=true`, o worker executa a limpeza a cada hora. `Retention__AuditDays` controla auditorias e `Retention__WebhookDeliveryDays` controla deliveries aceitas ou mortas. Deliveries pendentes nunca são removidas por esse job.

Backups, rotação de credenciais e testes de caos ainda exigem a política operacional de cada ambiente.
