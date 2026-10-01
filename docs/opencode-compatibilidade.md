# Compatibilidade OpenCode

O workspace não contém OpenCode. O contrato interno já está fixado em `IAgentAdapter`, com execução, cancelamento e health, mas a implementação concreta permanece desabilitada até que uma versão-alvo seja fornecida.

A prova pendente deve registrar versão, método de integração (SDK, HTTP ou CLI), criação ou retomada de sessão, envio de prompt, coleta de resposta, cancelamento e limites. O adapter não deve receber credenciais Redis.
