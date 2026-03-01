# Prompts utilizados para criação inicial do projeto

## Arquivo de refinamento do projeto
Como um arquiteto de software, crie um arquivo chamado refinamento.md na pasta refinamento com um refinamento técnico para o microsserviço "ingestion" descrito no arquivo historia.md.

Foque em:
- Arquitetura de projeto hexagonal para comunicação com serviços externos
- Passos lógicos para setup inicial e desenvolvimento limpo
- Projeto preparado para receber testes em uma próxima etapa


## Tasks
Como um especialista em arquitetura de software e desenvolvedor .net, gere uma quebra logica do refinamento do microserviço ingestion em até 10 tasks, cada uma representando uma etapa para concluir a construção base (sem incluir implementação de testes, apenas preparando para ser testável futuramente)

Para cada task:

Nome da task - curto e objetivo.

Objetivo - o que será entregue nesta etapa.

Principais entregas - bullet points com entregas concretas.

Prompt de execução - um texto no formato de instrução clara para o Copilot gerar o código dessa etapa, seguindo boas práticas de prompt (linguagem clara, detalhamento suficiente, contexto de tecnologias utilizadas, design esperado e passos estruturados para a entrega do objetivo).

O prompt deve incluir orientações específicas para boas práticas, como:

Organização de código e pastas
Uso correto das técnologias (.net, dapper, redis, postgresql, docker, rabbitmq, etc)

Padrões de nomenclatura.

Preocupações com desacoplamento e manutenibilidade

Preparação para testes futuros (injeção de dependências, separação de camadas)

Salve cada task como um arquivo .md individual dentro da pasta refinamento/, noemados sequencialmente como task_1.md, task_2.md, task_3.md e assim por diante

Certifique-se de que as Tasks sigam uma ordem lógica de implementação, da configuração inicial do projeto até a finalização da base, usando como refência a historia.md e o refinamento.md ja abertos no editor.

## Execução da task_*.md

Implemente com base na task_*.md

## Criação do arquivo de resumo para contextualização de um novo chat.

Gere o arquivo resumo.md com um resumo técnico conciso do progresso atual deste projeto para que eu possa transferir o contexto para um novo chat. Inclua: 1. A stack tecnológica e padrões de arquitetura definidos; 2. O estado atual da implementação (o que já funciona); 3. As dependências críticas entre arquivos; 4. O próximo passo imediato que estávamos discutindo. Foque em definições técnicas, não em explicações.
