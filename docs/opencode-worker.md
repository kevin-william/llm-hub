# Worker OpenCode

O processo registra `opencode-{nome-da-maquina}` no Hub, busca um run por vez, confirma e renova o lease a cada 15 segundos, traduz o conteúdo para `IAgentAdapter` e informa conclusão, cancelamento ou falha transitória usando o token do lease. `ConnectionStrings__Hub` é obrigatório; `OpenCodeWorker__WorkerId`, `OpenCodeWorker__Version`, `OpenCodeWorker__PollSeconds` e `OpenCodeWorker__HeartbeatSeconds` permitem ajustar a identidade e os intervalos.

O workspace ainda não contém uma versão-alvo do OpenCode. Por isso, `OpenCodeAdapter` reporta health indisponível e o processo não registra nem consome runs até que a prova de compatibilidade defina a integração concreta. O adapter não recebe credenciais Redis.

Antes de habilitar uma integração real, a prova deve registrar versão, método (SDK, HTTP ou CLI), criação ou retomada de sessão, prompt, resposta, cancelamento e limites.
