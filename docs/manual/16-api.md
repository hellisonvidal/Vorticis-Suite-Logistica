# Integração por API

O Vorticis Suite Logística pode **conversar com outros sistemas** pela API: um rastreador, o portal do cartão de combustível, o sistema do cliente ou uma ferramenta de BI consultam veículos, abastecimentos, conhecimentos e contas, e podem enviar abastecimentos, sem digitação.

## Configurar a integração por API

**Para que serve:** liberar o acesso de um sistema externo aos dados da transportadora, com segurança.

**Onde fica:** *Configurações › Integração por API* (em qualquer módulo da Suite). Só **administradores** abrem esta tela.

**Como usar:**
1. Escolha a transportadora no alto da tela: as chaves valem só para ela.
2. Em **Chaves de acesso**, dê um **nome** à chave (o sistema que vai usá-la, ex.: *Rastreador*) e marque **Pode gravar** se o sistema for enviar dados (ex.: abastecimentos). Clique em **Gerar chave**.
3. A chave aparece **uma vez só**, num quadro amarelo (começa com `vtx_`). Copie e entregue ao responsável pelo outro sistema. Se perder, gere outra.
4. Em **Servidor da API**, confira a **Porta** (padrão 5180) e clique em **Ligar a API**. A situação passa a mostrar o endereço, ex.: `http://localhost:5180/api/v1`.
5. Para cortar o acesso de um sistema, selecione a chave e clique em **Revogar a selecionada**: ela deixa de valer na hora. A lista mostra quando cada chave foi usada pela última vez.

**Regras e cuidados:**
- A chave age **com as permissões do usuário que a gerou**: o que o perfil dele não permite, a API também não faz.
- Chave sem **Pode gravar** só consulta; tentar gravar com ela dá o erro 403.
- A API funciona **enquanto o programa está aberto** neste computador e atende só pelo endereço `localhost`. Para outro computador acessar, peça ao seu técnico para publicar a porta (por exemplo, com um proxy ou túnel seguro).
- O banco guarda só uma impressão (hash) da chave: nem o administrador consegue vê-la de novo.

## Funções da API

Toda chamada leva o cabeçalho `Authorization: Bearer` seguido da chave. As respostas são em **JSON**. Datas no formato **aaaa-mm-dd**; sem período, valem os últimos 30 dias. A lista completa também aparece na tela e em `GET /api/v1`.

| Método | Caminho | O que faz |
|---|---|---|
| GET | `/api/v1/veiculos` | Veículos cadastrados |
| GET | `/api/v1/motoristas` | Motoristas cadastrados |
| GET | `/api/v1/abastecimentos?inicio=2026-10-01&fim=2026-10-31` | Abastecimentos do período |
| POST | `/api/v1/abastecimentos` | Grava um abastecimento (exige chave com gravação) |
| GET | `/api/v1/ctes?inicio=...&fim=...&situacao=EMITIDO` | Conhecimentos de transporte (CT-e) do período |
| GET | `/api/v1/contas-receber?inicio=...&fim=...&abertas=true` | Contas a receber com vencimento no período |
| GET | `/api/v1/contas-pagar?inicio=...&fim=...&abertas=true` | Contas a pagar com vencimento no período |

**Exemplo de envio de abastecimento** (corpo do POST, em JSON): `{"placa": "ABC1D23", "data": "2026-10-05", "km": 152300, "litros": 410.5, "valor": 2470.30, "combustivel": "DIESEL_S10", "posto": "Posto Rota 116"}`. O combustível é opcional (vale o do veículo). A resposta traz o número do abastecimento gravado; os erros vêm numa lista `erros`, com as mesmas mensagens das telas.

**Códigos de resposta:** 200 (consulta feita), 201 (gravado), 400 (JSON inválido), 401 (chave ausente, errada ou revogada), 403 (sem permissão ou chave só de leitura), 404 (rota ou veículo não encontrado), 422 (dados recusados pelas regras do sistema).
