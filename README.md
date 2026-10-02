# LLM Hub

Hub assíncrono de mensagens e execuções para conectar agentes por canais persistentes. PostgreSQL guarda o estado, Redis Streams transporta trabalho e o Hub entrega respostas por eventos MCP e webhooks.

## Pré-requisitos

- .NET SDK 10.0.401, ou a cópia local em `.tools/dotnet`.
- Docker Desktop com o daemon iniciado para PostgreSQL, Redis e MinIO.

## Desenvolvimento

```powershell
Copy-Item .env.example .env
docker compose up -d
$env:Webhook__SecretEncryptionKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
.\.tools\dotnet\dotnet.exe restore LlmHub.sln
.\.tools\dotnet\dotnet.exe test LlmHub.sln
.\.tools\dotnet\dotnet.exe run --project src/LlmHub.Api
```

`GET /health` confirma que a API iniciou. PostgreSQL, Redis e o armazenamento S3 compatível local são expostos nas portas 5432, 6379, 9000 e 9001. A variável `Webhook__SecretEncryptionKey` é obrigatória para criar inscrições de webhook e deve vir de um cofre de segredos fora do ambiente de desenvolvimento.

A API aplica as migrations pendentes quando `Database__ApplyMigrations=true`, configuração adequada para o ambiente local. Em produção, execute-as em uma etapa controlada de implantação ou mantenha essa configuração habilitada somente em uma instância responsável pela atualização do schema.
