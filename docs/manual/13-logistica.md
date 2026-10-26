# Vorticis Suite Logística

O **Vorticis Suite Logística** (ícone verde-petróleo) é o programa da transportadora: **um programa só, com um menu só**, que reúne o controle da frota e da manutenção, o **TMS** (cotações, conhecimentos, manifestos, entregas e faturas de frete), o financeiro e a contabilidade (esta no menu **Gerencial**). As telas são as mesmas dos módulos do Vorticis (mesmas regras e o mesmo banco de dados), organizadas para o dia a dia da transportadora. Não é preciso trocar de módulo.

## Instalação e primeiro acesso

1. Rode o instalador **Setup-VorticisSuiteLogistica**. Ele instala o programa e configura o banco de dados sozinho.
2. Abra o atalho **Vorticis Suite Logística** (menu Iniciar ou área de trabalho) e entre com o seu usuário.
3. Cadastre a transportadora em *Cadastros › Transportadoras*. Ela já fica ativa em todas as áreas do programa (frota, financeiro e contábil).
4. Escolha a transportadora no topo da tela e siga a ordem dos cadastros do capítulo [Frota](12-frota.md#ordem-dos-cadastros).

O Vorticis Suite Logística é instalado numa pasta própria (*Vorticis Logistica*) e usa o mesmo banco de dados do Vorticis no computador. Quem já tinha o programa com o nome antigo (Vorticis Logística) só precisa rodar o instalador novo: ele atualiza a instalação, troca os atalhos para o nome **Vorticis Suite Logística** e mantém os dados.

## Menus do programa

| Menu | O que tem | Detalhes no manual |
|---|---|---|
| **Cadastros** | Transportadoras (com a aba **Contadores**: o contador e o escritório contábil ficam só ali), clientes, fornecedores, veículos, motoristas, **frotas**, **rotas**, **tabelas de frete**, documentos e vencimentos; os cadastros do Gerencial (produtos, naturezas de entrada, formas de pagamento, contas bancárias) e do contábil (plano de contas, históricos...) em submenus; em cada grupo (a lista principal e os submenus *Gerencial* e *Contábil*), o item **Importar de planilha (Excel)** com os cadastros daquele grupo; os **Parâmetros** | [Frota](12-frota.md), [Contábil](03-contabil.md) |
| **Operações** | Abastecimentos e viagens; **Diárias**, submenu **Coletas** (*Ordens de coleta*) e **Entregas e ocorrências**; submenu **Pedágio** (importação das passagens e nota fiscal da operadora); submenus **Compras** (pedidos de compra, notas fiscais de entrada e importação de XML), **Manutenção** (planos de manutenção, peças e serviços, ordens de serviço e autorização de serviços; as notas das oficinas e das peças são lançadas em *Operações › Compras › Notas fiscais de entrada*), **Estoque** e **Monitores** | [Frota](12-frota.md#abastecimentos), [Manutenção](12-frota.md#ordens-de-servico), [Diárias](12-frota.md#diarias), [Coletas](12-frota.md#ordens-de-coleta), [Entregas](14-tms.md#entregas-e-ocorrencias), [Pedágio](12-frota.md#pedagio) |
| **Emissão** | As funções de faturamento: **Pedidos de venda** e **Notas fiscais de saída** (a emissão de NF-e na SEFAZ, em breve); o TMS: **Cotações de frete**, **Conhecimentos de transporte (CT-e)**, **Manifestos de carga (MDF-e)** e **Faturas de frete**; a transmissão de CT-e e MDF-e à SEFAZ e a NFS-e (em breve) | [Gerencial](10-gerencial.md), [TMS](14-tms.md) |
| **Financeiro** | Contas a pagar, contas a receber e conciliação bancária | [Gerencial](10-gerencial.md) |
| **Contábil** | Lançamentos, fechamentos, imobilizado, consultas e integrações da contabilidade (os relatórios ficam em *Relatórios › Contábil*) | [Contábil](03-contabil.md) |
| **Relatórios** | Todos os relatórios do programa, em submenus: **Frota e operação**, **Manutenção**, **Transporte (TMS)**, **Gerencial**, **Contábil** e **Gráficos** | [Frota](12-frota.md#relatorios-da-frota) |

Neste programa **não** há o menu Fiscal, o calendário de obrigações nem o *Painel do empresário*: use o menu **Financeiro** para as contas a pagar e a receber. A tela inicial também não mostra o quadro de próximas obrigações.

Os atalhos de teclado do Frota valem aqui (**Ctrl+B** abastecimentos, **Ctrl+P** painel da frota, **Ctrl+O** ordens de serviço), além dos do Contábil e do Gerencial que não repetem tecla (ex.: **Ctrl+L** lançamento contábil, **Ctrl+I** importação de XML). A lista completa fica em *Ajuda › Sobre*.

## Gráficos

**Para que serve:** ver de relance como andam a frota, o transporte, o financeiro e o resultado, mês a mês.

**Onde fica:** *Relatórios › Gráficos*, em submenus por área:

| Área | Gráfico | O que mostra |
|---|---|---|
| **Frota** | Custos da frota por mês | Combustível, manutenção, despesas de viagem e pedágio, lado a lado em cada mês |
| | Consumo médio por veículo (km/l) | A média de cada veículo pelo tanque cheio, do mais econômico ao menos |
| | Pedágio por veículo | O gasto de pedágio de cada placa |
| **Transporte** | Frete emitido por mês | Total dos CT-e emitidos e o ICMS de cada mês |
| | Entregas no prazo e com atraso | Quantos CT-e foram entregues dentro e fora da previsão, por mês da entrega |
| | Frete por cliente | Os clientes (tomadores) que mais geram frete; os demais somados em *Outros* |
| **Financeiro** | Contas a pagar e a receber por mês | Valor das duplicatas pelo mês de vencimento |
| | Saldo em aberto por mês de vencimento | O que ainda falta receber e pagar |
| **Contábil** | Resultado contábil por mês | Lucro (barra para cima) ou prejuízo (barra para baixo) da DRE de cada mês |

**Como usar:** escolha o **Período** (o padrão são os últimos 12 meses) e clique em **Gerar**. Passe o mouse sobre o gráfico para ver os valores de cada mês ou item; ao lado fica a **tabela** com os mesmos números (e o total). Cada cor é sempre a mesma série (a legenda fica em cima); os dados respeitam as permissões do seu perfil.

## Parâmetros

**Para que serve:** reunir, numa janela só, os parâmetros de todas as áreas do programa e as suas preferências de menu.

**Onde fica:** *Cadastros › Parâmetros*. A janela tem uma **aba** para cada área:

| Aba | O que tem |
|---|---|
| **Frota** | Avisos de vencimento, alerta de consumo, limite de autorização das ordens de serviço, fator rodoviário das rotas e valores das diárias e contas da contabilização das diárias. |
| **Gerencial** | Os parâmetros do financeiro e do fiscal usados nas contas a pagar e a receber (contas de fornecedores e clientes, juros, multa, descontos e históricos das baixas). |
| **Contábil** | Regras de lançamento, centros de custo, encerramento e o **Tipo de contabilização** (On-line ou Em lote, veja [Contabilização em lote](03-contabil.md#contabilizacao-em-lote)). |
| **Menus** | Escolha dos itens de menu que você quer ver (veja abaixo). |

**Como usar:** clique na aba, altere os campos e clique em **Salvar** (cada aba salva os seus próprios campos; *Descartar alterações* volta aos valores gravados). Os campos e a gravação são os mesmos de cada módulo, como descrito em [Parâmetros](02-cadastros-comuns.md#parametros).

### Menus: esconder o que você não usa

**Para que serve:** deixar o menu só com o que você usa no dia a dia.

**Como usar:**
1. Abra *Cadastros › Parâmetros* e clique na aba **Menus**. Aparece a lista dos menus e itens, em árvore, com uma caixa de marcação em cada um.
2. **Desmarque** o que não quer ver. Desmarcar um menu esconde também tudo o que está dentro dele; marcar um item mostra de novo o menu que o contém. **Mostrar todos** marca tudo.
3. Clique em **Salvar**. O menu muda na hora.

**O que acontece com o que foi escondido:** o item some do menu e a função fica **bloqueada**: o atalho de teclado e qualquer outra forma de abrir a tela mostram, na barra de situação, o aviso de que a função está oculta nas suas preferências. Para voltar a usá-la, marque o item de novo nesta aba.

**Itens essenciais:** *Parâmetros*, *Sair (trocar de usuário)* e todo o menu *Ajuda* (e os menus que os contêm) não podem ser desmarcados.

**Cada usuário escolhe o seu.** Quem é administrador vê a opção **Aplicar para todos os usuários**: ao salvar com ela marcada, os itens desmarcados ficam escondidos para todos (os outros usuários não conseguem mostrá-los de novo; aparecem como *oculto pelo administrador*). Sem a opção marcada, a escolha vale só para você.

## Quem pode usar cada área

As permissões são as dos perfis de acesso (*Configurações › Usuários*, aba *Perfis de acesso*): cada tela continua exigindo a função da sua área (ex.: Frota › Manutenção, Financeiro › Contas a pagar, Contábil › Lançamentos). Assim, o motorista-chefe pode ver só a frota e o financeiro só o seu menu, no mesmo programa.

## O que vem a seguir

- **Transmissão à SEFAZ:** CT-e e MDF-e autorizados direto pelo programa (DACTE e DAMDFE, cancelamento, carta de correção e encerramento) e NFS-e pelo Padrão Nacional. Hoje o [TMS](14-tms.md) registra a chave de acesso do documento autorizado no seu emissor.
- **Integração contábil do TMS:** as faturas de frete e o ICMS dos conhecimentos virando lançamentos contábeis; as notas de compra da manutenção virando conta a pagar e despesa do veículo.
