# Vorticis Suite Logística

O **Vorticis Suite Logística** é o programa da transportadora. Ele reúne **seis módulos na mesma janela**, cada um com os seus menus, a sua cor e o seu ícone. Você entra uma vez só (o login vale para todos os módulos) e passa de um módulo para o outro com um clique. As telas são as mesmas dos módulos do Vorticis, com as mesmas regras e o mesmo banco de dados.

| Módulo | Ícone e cor | O que tem | Detalhes no manual |
|---|---|---|---|
| **Frotas** | caminhão, azul | **Transportadoras** (só neste módulo) e os cadastros de veículos, motoristas, frotas, rotas, documentos e vencimentos; *Operação*: abastecimentos, viagens, diárias e pedágio; relatórios e gráficos da frota | [Frota](12-frota.md) |
| **Transporte** | rota com marcador, verde-água | O TMS: tabelas de frete e formas de pagamento; *Operação*: cotações de frete, ordens de coleta, entregas e ocorrências, [roteirizador](14-tms.md#roteirizador) e [CIOT](14-tms.md#ciot-operacoes-de-transporte); *Faturamento*: todo o faturamento da transportadora (CT-e e MDF-e, com a transmissão à SEFAZ em breve; faturas de frete, pedidos de venda, notas fiscais de saída; NF-e e NFS-e em breve); relatórios e gráficos do transporte e do faturamento | [TMS](14-tms.md) |
| **Manutenção** | chave de boca, laranja | Planos de manutenção e peças e serviços; *Serviços*: ordens de serviço e autorização de serviços; relatórios da manutenção | [Frota](12-frota.md#ordens-de-servico) |
| **Financeiro** | carteira, verde | Contas bancárias e formas de pagamento; *Contas*: contas a pagar, contas a receber, [consórcios e financiamentos](15-financiamentos.md) e conciliação bancária; *Imobilizado*: [bens financiados e consorciados](15-financiamentos.md#bens-financiados-e-consorciados), cadastro de bens e depreciação; *Monitores*: monitor de cobranças; relatórios e gráficos do financeiro | [Gerencial](10-gerencial.md) |
| **Gerencial** | gráfico de barras, rosa | Produtos e [regras fiscais](05-fiscal.md#regras-fiscais); *Compras*: pedidos de compra, notas fiscais de entrada e importação de XML; *Estoque*; *Monitores*: monitor de notas fiscais e [monitor de multas de trânsito](12-frota.md#monitor-de-multas-de-transito); *Painel do empresário* e relatório de compras | [Gerencial](10-gerencial.md) |
| **Contabilidade** | calculadora, índigo | Plano de contas, históricos, centros de custo e filiais; lançamentos, fechamentos, imobilizado, consultas, integrações (SPED) e os relatórios e gráficos contábeis | [Contábil](03-contabil.md) |

Todos os módulos têm, em volta dos seus menus, os menus comuns: **Cadastros** (com clientes, fornecedores, os cadastros do módulo (os que aceitam planilha têm o botão **Importar de planilha (Excel)** no alto da janela do cadastro) e os **Parâmetros**), **Configurações** e **Ajuda**. O cadastro **Transportadoras** fica só no módulo **Frotas** (*Cadastros › Transportadoras*); nos demais módulos a transportadora é só escolhida no alto da tela, e a escolhida continua a mesma ao trocar de módulo.

## Instalação e primeiro acesso

1. Rode o instalador **Setup-VorticisSuiteLogistica**. Ele instala o programa e configura o banco de dados sozinho.
2. Abra o atalho **Vorticis Suite Logística** (menu Iniciar ou área de trabalho) e entre com o seu usuário. Os seis módulos entram juntos.
3. No módulo **Frotas**, cadastre a transportadora em *Cadastros › Transportadoras*. Ela já fica ativa em todos os módulos. Envie também o **logo** da transportadora (aba *Dados cadastrais*, [Logo da empresa](02-cadastros-comuns.md#logo-da-empresa-sai-em-todos-os-relatorios)): ele sai em todos os relatórios em PDF.
4. Escolha a transportadora no alto da tela e siga a ordem dos cadastros do capítulo [Frota](12-frota.md#ordem-dos-cadastros).

O Vorticis Suite Logística é instalado numa pasta própria (*Vorticis Logistica*) e usa o mesmo banco de dados do Vorticis no computador. Quem já tinha o programa com o nome antigo só precisa rodar o instalador novo: ele atualiza a instalação, troca os atalhos e mantém os dados.

## Trocar de módulo

- Clique no ícone ou no nome do módulo na navegação (à esquerda ou no alto, conforme o layout escolhido).
- Ou use o teclado: **Ctrl+1** (Frotas), **Ctrl+2** (Transporte), **Ctrl+3** (Manutenção), **Ctrl+4** (Financeiro), **Ctrl+5** (Gerencial) e **Ctrl+6** (Contabilidade).
- **Ctrl+K** abre a busca de funções: digite parte do nome (sem acento, ex.: *viagens*, *balancete*) e clique no resultado para ir direto à tela, em qualquer módulo.
- A cor da barra de menus e o ícone grande do módulo, ao fundo da tela, mostram em que módulo você está. As funções abertas de cada módulo continuam abertas quando você vai para outro; um número vermelho no ícone mostra quantas estão abertas.

Os atalhos de teclado de cada módulo continuam valendo dentro dele (ex.: **Ctrl+B** abastecimentos e **Ctrl+P** painel da frota, no Frota; **Ctrl+O** ordens de serviço, na Manutenção; **Ctrl+L** lançamento contábil, na Contabilidade). A lista completa fica em *Ajuda › Sobre*.

## Layouts da Suite

**Para que serve:** escolher como a navegação entre os módulos aparece na tela.

**Onde fica:** no botão **Layout** (ícone de quatro quadros), presente em todos os layouts, e em *Configurações › Layout da Suite*. A escolha fica gravada neste computador.

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
- Para deixá-la **sempre à mostra**, clique em **Fixar** (ícone de alfinete) na própria navegação; para voltar a recolher, clique em **Soltar**. A opção também está no cartão de layouts (*Fixar a navegação deste layout*) e em *Configurações › Layout da Suite*. A navegação à esquerda e a do alto guardam cada uma a sua escolha.

## Gráficos

**Para que serve:** ver de relance como andam a frota, o transporte, o financeiro e o resultado, mês a mês.

**Onde fica:** *Relatórios › Gráficos* de cada módulo (Frotas, Transporte, Financeiro e Contabilidade):

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

## Parâmetros

**Para que serve:** reunir, numa janela só, os parâmetros das áreas do módulo e as suas preferências de menu.

**Onde fica:** *Cadastros › Parâmetros* de cada módulo. A janela tem uma **aba** para cada área do módulo e a aba **Menus**:

| Módulo | Abas |
|---|---|
| **Frotas**, **Transporte** e **Manutenção** | Frota: avisos de vencimento, alerta de consumo, limite de autorização das ordens de serviço, fator rodoviário das rotas, valores das diárias e contas da contabilização das diárias. |
| **Transporte** (aba Faturamento) | Contas de clientes e fornecedores, juros, multa, descontos e históricos das baixas. |
| **Financeiro** e **Gerencial** | Os parâmetros do financeiro e do fiscal usados nas contas a pagar e a receber. |
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

As permissões são as dos perfis de acesso (*Configurações › Usuários*, aba *Perfis de acesso*): cada tela continua exigindo a função da sua área (ex.: Frota › Manutenção, Financeiro › Contas a pagar, Contábil › Lançamentos). Assim, o motorista-chefe pode trabalhar só nos módulos Frota e Manutenção e o financeiro só no módulo Financeiro, no mesmo programa; os módulos sem nenhuma função liberada mostram as telas bloqueadas.

## O que vem a seguir

- **Transmissão à SEFAZ:** CT-e e MDF-e autorizados direto pelo programa (DACTE e DAMDFE, cancelamento, carta de correção e encerramento) e NFS-e pelo Padrão Nacional. Hoje o [TMS](14-tms.md) registra a chave de acesso do documento autorizado no seu emissor.
- **Integração contábil do TMS:** as faturas de frete e o ICMS dos conhecimentos virando lançamentos contábeis; as notas de compra da manutenção virando conta a pagar e despesa do veículo.
