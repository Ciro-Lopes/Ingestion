# Projeto para ingestão de dados de dados
# **ingestion**

## Contexto da demanda

Criação de um microsserviço para receber, armazenar e repassar dados.

## Objetivo do projeto
Microsserviço **ingestion** que recebe dados através de mensageria, ter controle de versionamento do dado que está recebendo comparando um campo de atualização que vem no proprio dado com um armazenado via cache distribuido, armazenar esse dado em uma base de dados e se comunicar com outro microsserviço através de mensageria.


## Requisitos iniciais

* Deverá ouvir multiplas filas ao mesmo tempo;
* Deverá ter fluxo para filas de mensagens mortas;
* Deverá de tempos em tempos verificar em um cache distribuído informações sobre o consumo da fila:
  * Quantidade de mensagens por vez;
  * Se o consumo deve estar ligado ou desligado;
  * Quantidade de consumidor rodando em paralelo para a fila específica;
* Deverá realizar um mapeamento interno separando dados importantes da mensagem que serão persistidos em base de dados e deverá ter um campo json para armazenar a mensagem completa recebida e outroa para os metadados;
* Deverá validar em cache distribuído se o dado que será persistido está em sua versão mais atualizada comparando um campo específico de data e hora que contém na mensagem;
* Deverá realizar persistência em base de dados (deverá ser realizada em lote);
* Deverá atualizar o cache distribuído sobre o dado armazenado enviando seu id e a data e hora da mensagem que gerou a persistência;
* Deverá gerar uma postagem em mensageria para o próximo microsserviço;
* Deverá ter cobertura de testes unitários;


## Fluxos do sistema

* trade

Para cada fluxo terá os seguintes itens:
* Fila para consumo;
* Configurações de consumo;
* Tabela no banco de dados para insert/update;
* Envio para o próximo microsserviço;


## Mapeamento das entidades

* trade
  * id (composto pelos campos (id, reference_date, type))
  * quantity
  * reference_date
  * type
  * status
  * created_at
  * updated_at
  
## Detalhes adicionais

* Técnologias utilizadas:
  * RabbitMq para mensageria;
  * Redis para cache distribuído;
  * PostgreSQL para base de dados;
  * .Net para a criação do microsserviço;
  * Dapper para comunicação com banco de dados;
  * Docker e docker compose para subir o ambiente;
