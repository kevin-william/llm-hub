# SDK de adapters

Um adapter implementa `IAgentAdapter`: `ExecuteAsync`, `CancelAsync` e `HealthAsync`. O input contém run, canal, sessão recuperada e referências de artefatos; o resultado devolve conteúdo, sessão e referências de artefatos. O gateway mantém registro, claim, heartbeat, complete e fail fora do adapter. O registro de worker também publica um endpoint `agent:{adapter}/{worker_id}` com suas capacidades e estado `online`, visível em `list_endpoints`. O Hub preserva o destino escolhido na abertura do canal e o usa para decidir o adapter de cada run. Quando o chamador usa `agent:{adapter}` com `requiredCapabilities`, o Hub escolhe um desses endpoints por endereço e identificador.

`EchoAdapter` é a implementação de referência. Ela responde com `echo: {conteúdo}`, preserva sessão e artefatos e respeita cancelamento.
