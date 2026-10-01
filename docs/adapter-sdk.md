# SDK de adapters

Um adapter implementa `IAgentAdapter`: `ExecuteAsync`, `CancelAsync` e `HealthAsync`. O input contém run, canal, sessão recuperada e referências de artefatos; o resultado devolve conteúdo, sessão e referências de artefatos. O gateway mantém registro, claim, heartbeat, complete e fail fora do adapter.

`EchoAdapter` é a implementação de referência. Ela responde com `echo: {conteúdo}`, preserva sessão e artefatos e respeita cancelamento.
