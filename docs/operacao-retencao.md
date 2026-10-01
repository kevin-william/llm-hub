# Operação, quotas e retenção

O Hub configura as quotas por `Hub__Quotas__MaxMessageBytes`, `Hub__Quotas__MaxAttachments` e `Hub__Quotas__MaxActiveRunsPerPrincipal`. Os valores padrão são 64 KiB, 16 anexos e 10 runs ativos por principal. Rejeições retornam `MESSAGE_QUOTA_EXCEEDED`, `ATTACHMENT_QUOTA_EXCEEDED` ou `RUN_QUOTA_EXCEEDED` antes de criar trabalho.

Retenção, backups, rotação de credenciais e testes de caos ainda exigem a política operacional de cada ambiente.
