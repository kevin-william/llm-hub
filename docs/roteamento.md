# Roteamento determinístico

O destino de um canal deve usar `agent:{adapter}/{instância}`. O roteador aceita `opencode` e `echo` e grava o adapter escolhido no run antes de publicá-lo na outbox. Destinos sem adapter ou adapters desconhecidos retornam erro controlado; não há roteamento baseado em LLM.
