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

## Worker OpenCode

O worker é um processo separado. Ele usa a API HTTP pública do Hub para registrar-se, pedir trabalho, renovar o lease e concluir o run; não acessa PostgreSQL nem Redis. Inicie um servidor OpenCode local e depois o worker:

```powershell
opencode serve --hostname 127.0.0.1 --port 4096
$env:HubWorker__BaseUrl = 'http://localhost:5000/'
$env:OpenCode__BaseUrl = 'http://127.0.0.1:4096/'
\.tools\dotnet\dotnet.exe run --project src/LlmHub.OpenCodeWorker
```

Em ambiente com OIDC, defina `HubWorker__BearerToken` com um token de serviço que contenha `hub.admin`, `worker_id` igual a `OpenCodeWorker__WorkerId` e um claim de tenant. Se o servidor OpenCode exigir senha, defina `OpenCode__Username` e `OpenCode__Password`. O modelo e suas credenciais são configurados no próprio OpenCode; o Hub apenas envia o trabalho à sessão.

O Hub é independente de modelos: qualquer agente que seja cliente MCP pode usar as ferramentas expostas em `/mcp`. Para receber trabalho assíncrono, o agente executa um worker que se registra com seu próprio `adapter` e traduz cada run para a integração do modelo. O claim HTTP suporta long polling por até 30 segundos; o worker padrão usa 25 segundos, sem polling pelo LLM.

A API aplica as migrations pendentes quando `Database__ApplyMigrations=true`, configuração adequada para o ambiente local. Em produção, execute-as em uma etapa controlada de implantação ou mantenha essa configuração habilitada somente em uma instância responsável pela atualização do schema.
