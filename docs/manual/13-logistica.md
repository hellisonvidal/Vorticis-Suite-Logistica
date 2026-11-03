# Vorticis Suite Logística

O **Vorticis Suite Logística** é o programa da transportadora. Ele reúne **sete módulos na mesma janela**, cada um com os seus menus, a sua cor e o seu ícone. Você entra uma vez só (o login vale para todos os módulos) e passa de um módulo para o outro com um clique. As telas são as mesmas dos módulos do Vorticis, com as mesmas regras e o mesmo banco de dados.

| Módulo | Ícone e cor | O que tem | Detalhes no manual |
|---|---|---|---|
| **Frotas** | caminhão, azul | **Transportadoras** (só neste módulo) e os cadastros de veículos, motoristas, frotas, documentos e vencimentos; *Operação*: abastecimentos, viagens, diárias e pedágio; *Manutenção*: ordens de serviço, autorização de serviços, planos de manutenção e peças e serviços; *Monitores*: [monitor de multas de trânsito](12-frota.md#monitor-de-multas-de-transito); relatórios e gráficos da frota e da manutenção | [Frota](12-frota.md) |
| **Transporte** | rota com marcador, verde-água | O TMS: tabelas de frete e **rotas**; *Operação*: cotações de frete, ordens de coleta, entregas e ocorrências, [roteirizador](14-tms.md#roteirizador) e [CIOT](14-tms.md#ciot-operacoes-de-transporte); relatórios e gráficos do transporte | [TMS](14-tms.md) |
| **Faturamento** | nota fiscal, laranja | Produtos e serviços, [regras fiscais](05-fiscal.md#regras-fiscais) e formas de pagamento; *Faturamento*: CT-e e MDF-e, faturas de frete, pedidos de venda, notas fiscais de saída e [NFS-e](05-fiscal.md#nfs-e-notas-fiscais-de-servico) (a transmissão à SEFAZ e ao Emissor Nacional, em breve); relatórios de conhecimentos, faturas, vendas e recebimentos | [TMS](14-tms.md#faturas-de-frete) |
| **Compras** | carrinho, dourado | Produtos e serviços, [regras fiscais](05-fiscal.md#regras-fiscais) e formas de pagamento; *Compras*: pedidos de compra, notas fiscais de entrada, importação de XML, notas de compra da manutenção e o submenu *Estoque*; *Financeiro*: contas a pagar; relatórios de compras, de contas a pagar e de pagamentos | [Gerencial](10-gerencial.md) |
| **Financeiro** | carteira, verde | Contas bancárias e formas de pagamento; *Contas*: contas a receber, [consórcios e financiamentos](15-financiamentos.md) e conciliação bancária; *Imobilizado*: [bens financiados e consorciados](15-financiamentos.md#bens-financiados-e-consorciados), cadastro de bens e depreciação; *Monitores*: monitor de cobranças; relatórios, gráficos e o [BI do financeiro](#bi-indicadores) | [Gerencial](10-gerencial.md) |
| **Gestão** | gráfico de barras, rosa | *Painel do empresário*; *Monitores*: notas fiscais e cobranças; *Relatórios* de **todos os módulos, por categoria** (Frotas, Manutenção, Transporte, Faturamento, Compras, Financeiro e Contabilidade), os gráficos de cada área e o [BI](#bi-indicadores) | [Gestão](#gestao-relatorios-de-todos-os-modulos) |
| **Contabilidade** | calculadora, índigo | Plano de contas, históricos, centros de custo e filiais; lançamentos, fechamentos, imobilizado, consultas, integrações (SPED) e os relatórios e gráficos contábeis | [Contábil](03-contabil.md) |

Todos os módulos têm, em volta dos seus menus, os menus comuns: **Cadastros** (com clientes, fornecedores, os cadastros do módulo (os que aceitam planilha têm o botão **Importar de planilha (Excel)** no alto da janela do cadastro) e os **Parâmetros**), **Configurações** e **Ajuda**. O cadastro **Transportadoras** fica só no módulo **Frotas** (*Cadastros › Transportadoras*); nos demais módulos a transportadora é só escolhida no alto da tela, e a escolhida continua a mesma ao trocar de módulo.

## Instalação e primeiro acesso

1. Rode o instalador **Setup-VorticisSuiteLogistica**. Ele instala o programa e configura o banco de dados sozinho.
2. Abra o atalho **Vorticis Suite Logística** (menu Iniciar ou área de trabalho) e entre com o seu usuário. A tela de acesso mostra só o nome **Vorticis Suite Logística**: o login vale para os sete módulos.
3. No módulo **Frotas**, cadastre a transportadora em *Cadastros › Transportadoras*. Ela já fica ativa em todos os módulos. Envie também o **logo** da transportadora (aba *Dados cadastrais*, [Logo da empresa](02-cadastros-comuns.md#logo-da-empresa-sai-em-todos-os-relatorios)): ele sai em todos os relatórios em PDF.
4. Escolha a transportadora no alto da tela e siga a ordem dos cadastros do capítulo [Frota](12-frota.md#ordem-dos-cadastros).

O Vorticis Suite Logística é instalado numa pasta própria (*Vorticis Logistica*) e usa o mesmo banco de dados do Vorticis no computador. Quem já tinha o programa com o nome antigo só precisa rodar o instalador novo: ele atualiza a instalação, troca os atalhos e mantém os dados.

## Trocar de módulo

- Clique no ícone ou no nome do módulo na navegação (à esquerda ou no alto, conforme o layout escolhido).
- Ou use o teclado: **Ctrl+1** (Frotas), **Ctrl+2** (Transporte), **Ctrl+3** (Faturamento), **Ctrl+4** (Compras), **Ctrl+5** (Financeiro), **Ctrl+6** (Gestão) e **Ctrl+7** (Contabilidade).
- **Ctrl+K** abre a busca de funções: digite parte do nome (sem acento, ex.: *viagens*, *balancete*) e clique no resultado para ir direto à tela, em qualquer módulo.
- A cor da barra de menus e o ícone grande do módulo, ao fundo da tela, mostram em que módulo você está. As funções abertas de cada módulo continuam abertas quando você vai para outro; um número vermelho no ícone mostra quantas estão abertas.

Os atalhos de teclado de cada módulo continuam valendo dentro dele (ex.: **Ctrl+B** abastecimentos e **Ctrl+P** painel da frota, no Frota; **Ctrl+O** ordens de serviço, no Frotas; **Ctrl+L** lançamento contábil, na Contabilidade). A lista completa fica em *Ajuda › Sobre*.

## Layouts da Suite

**Para que serve:** escolher como a navegação entre os módulos aparece na tela.

**Onde fica:** no botão **Layout** (ícone de quatro quadros), presente em todos os layouts. A escolha fica gravada neste computador. No alto da navegação aparece só o nome **VORTICIS Suite Logística**.

| Layout | Como é |
|---|---|
| **A – Abas no topo** | Uma aba por módulo no alto da janela, como num navegador; a aba do módulo ativo fica branca, com a cor dele. |
| **B – Barra lateral** (padrão) | Coluna escura à esquerda com o ícone de cada módulo; o módulo ativo fica com o ícone colorido e uma marca ao lado. |
| **C – Árvore com busca** | Painel branco à esquerda com todos os módulos e as suas funções em árvore, e a busca de funções no alto. |
| **D – Painel inicial** | Tela inicial com um cartão por módulo (ícone, descrição, as funções principais e o botão **Abrir módulo**); no alto, **Início** volta ao painel e a lista **Módulo** troca de módulo. |
| **E – Faixa de comando** | Barra escura no alto com os módulos em "pílulas" coloridas e a busca de funções no meio. |

**Como escolher:** clique em **Layout** e escolha um dos cinco cartões (cada um mostra uma prévia do layout). A tela muda na hora.

### Fixar ou ocultar a navegação

A navegação de cada layout pode ficar **recolhida** para deixar mais espaço para as telas:

- **À esquerda (B e C):** recolhida, vira uma faixa fina com as cores dos módulos na beirada esquerda da janela; ela abre quando o mouse se aproxima e recolhe sozinha quando ele sai.
- **No alto (A, D e E):** recolhida, vira uma faixa fina colorida no alto da janela; ela abre quando o mouse chega ao alto e recolhe quando ele sai.
- Para deixá-la **sempre à mostra**, clique em **Fixar** (ícone de alfinete) na própria navegação; para voltar a recolher, clique em **Soltar**. A opção também está no cartão de layouts (*Fixar a navegação deste layout*). A navegação à esquerda e a do alto guardam cada uma a sua escolha.

## Gráficos

**Para que serve:** ver de relance como andam a frota, o transporte, o financeiro e o resultado, mês a mês.

**Onde fica:** *Relatórios › Gráficos* de cada módulo (Frotas, Transporte, Financeiro e Contabilidade) e, todos juntos, em *Relatórios* do módulo **Gestão**:

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
| **Contabilidade** | Resultado contábil por mês | Lucro (barra para cima) ou prejuízo (barra para baixo) da DRE de cada mês |

**Como usar:** escolha o **Período** (o padrão são os últimos 12 meses) e clique em **Gerar**. Passe o mouse sobre o gráfico para ver os valores de cada mês ou item; ao lado fica a **tabela** com os mesmos números (e o total). Cada cor é sempre a mesma série (a legenda fica em cima); os dados respeitam as permissões do seu perfil.

### BI (indicadores)

**Para que serve:** acompanhar a saúde financeira da transportadora com indicadores prontos, sem montar planilhas.

**Onde fica:** *Relatórios › BI – indicadores* do módulo **Financeiro** e do módulo **Gestão**.

| Indicador | O que mostra |
|---|---|
| **Fluxo de caixa realizado por mês** | O que foi **recebido** e **pago** de fato em cada mês (as baixas das contas, com juros e multa e sem os descontos) e o **saldo do mês** |
| **Contas em aberto por faixa de atraso** | O saldo a receber e a pagar *a vencer*, vencido *de 1 a 30*, *31 a 60*, *61 a 90* e *acima de 90 dias*, na data final do período (mostra a inadimplência dos clientes e os atrasos com fornecedores) |
| **Projeção de caixa (contas em aberto)** | O que ainda falta receber e pagar, por mês de vencimento, e o **saldo acumulado** mês a mês |
| **Maiores clientes (a receber no período)** | Os 10 clientes com mais contas a receber no período; os demais somados em *Outros* |
| **Maiores fornecedores (a pagar no período)** | Os 10 fornecedores com mais contas a pagar no período |

**Como usar:** igual aos gráficos: escolha o **Período**, clique em **Gerar** e confira os números na tabela ao lado.

## Gestão: relatórios de todos os módulos

**Para que serve:** reunir, num lugar só, os relatórios que o empresário e o gestor acompanham, sem precisar trocar de módulo.

**Onde fica:** módulo **Gestão**, menu *Relatórios*. Os relatórios estão separados por **categoria**: *Frotas*, *Manutenção*, *Transporte*, *Faturamento*, *Compras*, *Financeiro* e *Contabilidade* (balancete, balanço e DRE). Depois vêm os **Gráficos** de cada área e o **BI – indicadores**. O módulo também tem o *Painel do empresário* e os monitores de notas fiscais e de cobranças.

Os relatórios são os mesmos dos outros módulos (mesmos filtros, PDF e Excel) e respeitam as permissões do seu perfil.

## Ligação de cada módulo com a Contabilidade

**Para que serve:** conferir, de dentro de cada módulo, como o que você lança vira contabilidade.

**Onde fica:** menu **Contabilidade** dos módulos Frotas, Transporte, Faturamento, Compras e Financeiro:

- **Plano de contas**, **Consulta de lançamentos** e **Saldos de contas**: as mesmas telas do módulo Contabilidade, para conferir as contas e os lançamentos gerados (abastecimentos, diárias, ordens de serviço, contratos, notas fiscais, baixas de contas).
- No **Faturamento** e em **Compras**: **Regras de contabilização** e **Contabilizar notas fiscais** (as notas de saída e de entrada viram lançamentos pelas regras da empresa).
- **Contabilização em lote**: aparece quando a empresa usa o tipo de contabilização *Em lote* (*Contabilidade › Cadastros › Parâmetros*); veja [Contabilização em lote](03-contabil.md#contabilizacao-em-lote).

## Parâmetros

**Para que serve:** reunir, numa janela só, os parâmetros das áreas do módulo e as suas preferências de menu.

**Onde fica:** *Cadastros › Parâmetros* de cada módulo. A janela tem uma **aba** para cada área do módulo e a aba **Menus**:

| Módulo | Abas |
|---|---|
| **Frotas** e **Transporte** | Frota: avisos de vencimento, alerta de consumo, limite de autorização das ordens de serviço, fator rodoviário das rotas, valores das diárias e contas da contabilização das diárias. |
| **Faturamento** | Aba Faturamento: contas de clientes e fornecedores, juros, multa, descontos e históricos das baixas; aba CT-e e faturas de frete: os parâmetros da frota e do transporte. |
| **Compras**, **Financeiro** e **Gestão** | Os parâmetros do financeiro e do fiscal usados nas compras e nas contas a pagar e a receber. |
| **Contabilidade** | Regras de lançamento, centros de custo, encerramento e o **Tipo de contabilização** (On-line ou Em lote, veja [Contabilização em lote](03-contabil.md#contabilizacao-em-lote)). |
| Todos | **Menus**: escolha dos itens de menu do módulo que você quer ver (veja abaixo). |

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

As permissões são as dos perfis de acesso (*Configurações › Usuários*, aba *Perfis de acesso*): cada tela continua exigindo a função da sua área (ex.: Frota › Manutenção, Financeiro › Contas a pagar, Contábil › Lançamentos). Assim, o motorista-chefe pode trabalhar só no módulo Frotas, o comprador só em Compras e o financeiro só no módulo Financeiro, no mesmo programa; os módulos sem nenhuma função liberada mostram as telas bloqueadas.

## O que vem a seguir

- **Transmissão à SEFAZ:** CT-e e MDF-e autorizados direto pelo programa (DACTE e DAMDFE, cancelamento, carta de correção e encerramento) e NFS-e pelo Padrão Nacional. Hoje o [TMS](14-tms.md) registra a chave de acesso do documento autorizado no seu emissor.
- **Integração contábil do TMS:** as faturas de frete e o ICMS dos conhecimentos virando lançamentos contábeis; as notas de compra da manutenção virando conta a pagar e despesa do veículo.
