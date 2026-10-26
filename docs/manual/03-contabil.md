# Contábil

Menus do módulo Vorticis Contábil. Permissões: cada função depende do seu perfil (veja [Usuários e perfis](01-primeiros-passos.md#usuarios-perfis-e-empresas-por-usuario)).

## Plano de contas

**Para que serve:** a lista de contas da empresa.
**Onde fica:** *Cadastros › Plano de contas*.

### Escolher um plano
1. Se a empresa ainda não tem plano, clique em **Escolher plano**, selecione um modelo e **Usar este plano**. A troca só é possível enquanto não há lançamentos.
2. Para importar de uma planilha, veja [Importar plano de contas](#importar-plano-de-contas).

### Manter contas
1. **Incluir** e preencha o **código** (ex.: 1.1.01.004), a descrição, a natureza (devedora/credora) e se é **sintética** (grupo) ou **analítica** (recebe lançamentos).
2. O **código reduzido** das analíticas é gerado automaticamente (pode ser editado).
3. Controles: **Exige centro de custo**, **Exige participante** (e o grupo onde os participantes ficam) e **Ativa**.

**Dica:** filtre a lista por código, reduzido ou descrição. Em lançamentos, você pode digitar o reduzido em vez do código completo.

## Importar plano de contas

**Onde fica:** *Integrações › Importar › Plano de contas (planilha Excel/CSV)*.

1. **Procurar** o arquivo .xlsx ou .csv.
2. Em *Colunas*, diga qual coluna é o código, a descrição e, se houver, natureza, tipo (S/A) e saldo.
3. Para converter os códigos, marque *Converter os códigos para a máscara* e informe a máscara (ex.: `9.9.99.999`: 1101001 vira 1.1.01.001).
4. Confira a **pré-visualização** e clique em **Importar plano**. Só substitui o plano enquanto não houver lançamentos.
5. **Converter plano atual para a máscara** troca os códigos das contas existentes, mesmo com movimento, sem alterar lançamentos e hierarquia.

## Centros de custo

**Onde fica:** *Cadastros › Centros de custo*. Só é usado se a empresa ligou "Usar centros de custo" (aba Contabilidade); a máscara também é definida lá.

1. **Incluir**: código (obrigatório nos níveis 2 ou mais, conforme a máscara), descrição e superior.
2. Marque **Sintético** para grupos que não recebem lançamentos. **Ativo** controla o uso.
3. **Frota** (opcional): se a empresa usa o módulo Frota (por exemplo, no Vorticis Suite Logística), escolha a frota que este centro de custo representa. Os relatórios de custo por frota usam esse vínculo (veja [Frotas](12-frota.md#frotas)). Deixe **(nenhuma)** nos demais centros.

## Históricos padrão

**Para que serve:** textos prontos para os lançamentos.
**Onde fica:** *Cadastros › Históricos*.

1. **Incluir**; o código é sequencial. Digite o texto fixo, por exemplo `Pagamento da NF {DOCUMENTO} de {PARTICIPANTE}`.
2. No lançamento, digite o código do histórico: o texto entra e o cursor vai para o fim, para completar com o complemento livre.

**Variáveis disponíveis:** `{DOCUMENTO}`, `{PARTICIPANTE}`, `{DATA}`, `{MES}`, `{ANO}`, `{MES_ANTERIOR}`, `{COMPETENCIA}`, `{CONTA}`, `{VALOR}` e `{COMPLEMENTO}`. As que o sistema não consegue resolver ficam para você completar.

## Bloqueio de lançamentos

**Para que serve:** proteger os meses já conferidos ou entregues (ECD, balanço): nenhum lançamento com data até o dia informado pode ser incluído, alterado ou excluído.
**Onde fica:** *Cadastros › Bloqueio de lançamentos*.

1. Informe a data em **Bloquear lançamentos até** e clique em **Salvar**.
2. Para liberar, apague a data e salve.

A mesma data aparece na aba Contabilidade de *Cadastros › Empresas*. Para travar um mês inteiro com fechamento, use também o [Fechamento de períodos](#fechamento-de-periodos).

## Lançamentos

**Para que serve:** registrar os fatos contábeis (partidas dobradas: débitos = créditos).
**Onde fica:** *Lançamentos › Contábeis › Lançamento contábil* (atalho **Ctrl+L**).

A tela mostra os **lançamentos efetuados** do período, com os botões **Novo (Ctrl+N), Editar, Consultar, Excluir, Estornar** e Atualizar. Os estornados continuam na lista, marcados.

### Incluir um lançamento (pop-up)
1. Clique em **Novo (Ctrl+N)**. Abre o pop-up de digitação, com os campos **um embaixo do outro** (preenchimento na vertical), a lista *Partidas deste lançamento* logo abaixo e a lista *Contas analíticas* à direita. Tudo cabe na janela: nenhum campo fica escondido.
2. Informe a **data**.
3. Em **Conta a débito (D)** e **Conta a crédito (C)**, digite o **código reduzido** (no primeiro campo) ou o código/nome da conta (no segundo, com lista de sugestões), ou clique na conta em *Contas analíticas*. O **nome da conta** aparece ao lado, para conferir.
4. Informe o **valor** (formata como 1.500,00), o **documento** (NF, recibo, boleto), o **histórico padrão** (código) e/ou o **histórico**, o **centro de custo** e o **participante**, quando exigidos.
5. Para mais de uma partida, clique em **Nova partida (↓)** (ou seta para baixo): o formulário passa a mostrar a nova partida. Clique numa partida da lista para voltar a ela.
6. **Salvar (Ctrl+S)**.

### Teclado
| Tecla | Efeito |
|---|---|
| Enter / Tab | Vai ao próximo campo (de cima para baixo) |
| Shift+Enter | Volta ao campo anterior |
| **Enter no último campo** | Salva, se o lançamento já fecha; senão segue para a próxima partida |
| Ctrl+Enter | Salva |
| Ctrl+N / ↓ na última partida | Nova partida |
| ↑ em partida nova e vazia | Cancela a partida |
| Ctrl+Delete | Exclui a partida |

### Uma partida ou várias
- Uma partida com conta débito **e** conta crédito é um lançamento completo.
- Partidas com **uma conta só** somam um único lançamento que precisa fechar (débitos = créditos). Se faltar a contrapartida, o sistema pergunta e oferece incluir a linha.

### Editar um lançamento
Dê **duplo clique** (ou Enter) no lançamento da lista, ou clique em **Editar**. O que acontece ao salvar depende da configuração **Usar estorno de lançamentos** da empresa (aba Contabilidade do cadastro da empresa):
- **Ligada (padrão):** o original é **estornado na data dele** e o corrigido é gravado, tudo numa única operação.
- **Desligada:** não há estorno. O próprio lançamento é alterado, **com o mesmo número**, e a alteração fica registrada na *Auditoria*.

**Consultar** abre só para leitura. Só lançamentos digitados à mão podem ser editados.

### Excluir um lançamento
1. Selecione o lançamento na lista e clique em **Excluir**.
2. O sistema pergunta: "Deseja realmente excluir o lançamento nº ... de ..., valor R$ ...?". Confirme para excluir.

O lançamento e suas partidas são removidos, os saldos das contas são atualizados e a exclusão fica registrada na *Auditoria* (o registro do que foi excluído permanece). Se o lançamento faz par com um estorno, os dois são excluídos juntos. A numeração dos demais lançamentos **não muda**; para eliminar a lacuna use *Lançamentos › Utilitários › Reorganizar lançamentos*.

O sistema **não exclui** (e explica o motivo na tela) quando:
- o período do lançamento está **fechado** (reabra-o em *Fechamento de períodos*) ou o exercício está **encerrado**;
- já existe **ECD gerada** para o ano;
- a data está antes da data de bloqueio da empresa, ou a partida está **conciliada** (desfaça a conciliação antes);
- o lançamento veio de outro módulo (**Fiscal**, **Folha**, **Imobilizado** ou **Encerramento do exercício**): desfaça a operação no módulo de origem, por exemplo em *Cálculos › Folha › Contabilização* ou *Documentos fiscais › Contabilizar notas fiscais*.

### Estornar
Disponível quando a empresa usa estorno de lançamentos (configuração acima).
1. Selecione o lançamento na lista.
2. Informe a **data** do estorno e o **motivo** (obrigatório).
3. Clique em **Estornar**. É criado um lançamento inverso; o original continua na lista, marcado como estornado.

### Regras e erros comuns
- "Período fechado": reabra a competência em *Fechamento de períodos*.
- "Data bloqueada": a empresa tem uma data em *Cadastros › Bloqueio de lançamentos*.
- Débitos diferentes de créditos: o lançamento não é salvo.
- Conta sintética não recebe lançamento; use a analítica.
- Para remover um lançamento já gravado use **Excluir** (veja acima); com estorno ligado, também é possível estornar.
- Se a empresa exige o nº do documento, informe-o em todas as partidas.

## Saldos iniciais

**Para que serve:** carregar o saldo de cada conta ao implantar o sistema.
**Onde fica:** *Lançamentos › Contábeis › Saldos iniciais (implantação)*.

1. Escolha o **exercício**.
2. Digite o saldo de cada conta analítica e marque **D** ou **C** (use *Mostrar só contas preenchidas* para conferir).
3. Clique em **Lançar saldos iniciais**. A soma dos devedores precisa ser igual à dos credores.

## Importações

Todas ficam em *Integrações › Importar*. Em todas, **Pré-visualizar** valida cada lançamento (contas analíticas, período aberto, partidas dobradas) e **Importar** grava tudo numa única transação: se um lançamento falhar, nenhum é gravado.

### Planilhas (layouts personalizados)
1. **1. Planilha**: escolha o .xlsx ou .csv e clique em **Carregar** (mostra até 200 linhas; o cabeçalho de cada coluna indica o papel definido).
2. **2. Layout**: escolha um salvo ou clique em **Novo**, dê um nome e diga se *cada linha é um lançamento* (débito e crédito juntos) ou *uma partida* (conta + D/C, ou colunas de valor a débito e a crédito, agrupadas pelo nº do lançamento).
3. **3. Colunas**: indique qual coluna é data, conta débito/crédito (ou conta fixa), valor, histórico, documento e centro de custo. Informe a máscara das contas se quiser converter códigos (11010001 vira 1.1.01.0001).
4. **Salvar layout** (vale para todas as empresas), **Pré-visualizar** e **Importar**.

### Extratos bancários (OFX e PDF)
**Onde fica:** *Integrações › Importar › Extratos bancários (OFX e PDF)*.

1. **Carregue o arquivo primeiro:** clique em **Procurar...** e escolha o extrato. Pode ser o **OFX** do internet banking ou o extrato em **PDF**, **TXT** ou **CSV**. O arquivo é lido na hora (o botão **Carregar** lê de novo).
2. **OFX:** os movimentos aparecem direto na aba **Movimentos**; siga para o passo 4.
3. **PDF, TXT ou CSV — layout personalizado conforme o documento:** aparece o quadro **2. Layout do extrato**, já com um layout **sugerido pelo próprio documento**. Abra a aba **Documento carregado** para ver cada linha do arquivo separada em colunas (Coluna 1, Coluna 2...) e, na coluna *Movimento*, as linhas que viraram movimento.
   - Ajuste, olhando o documento: **colunas separadas por** (espaços para PDF/TXT, ponto e vírgula para CSV), **coluna da data** e o **formato da data** (para datas sem ano, informe o **Ano**), **coluna do valor** (0 = automático: com valor e saldo na mesma linha, fica o valor), **da descrição** (0 = junta o texto das outras colunas) e **do documento** (0 = sem documento).
   - **Ignorar linhas com**: palavras que descartam a linha, separadas por vírgula (ex.: SALDO, TOTAL).
   - **Inverter sinal**: para extratos que mostram as saídas como positivas (ex.: fatura de cartão).
   - Cada ajuste é aplicado na hora: confira a aba **Movimentos**. Valores com **D** ou sinal de menos são saídas; com **C** ou sem sinal, entradas.
   - Dê um **Nome** (ex.: o nome do banco) e clique em **Salvar layout**. Da próxima vez, carregue o extrato desse banco e escolha o layout em **Layout salvo**. **Novo** volta à sugestão pelo documento; **Excluir layout** apaga o layout escolhido. Os layouts valem para todas as empresas.
4. Informe a **conta do banco** e as contrapartidas padrão de **entradas** e **saídas**; use *Preencher vazias* e ajuste linha a linha.
5. **Pré-visualizar** e **Importar**. Movimentos já importados não são duplicados (no OFX, pela identificação do banco; no PDF/TXT/CSV, pela data, valor, descrição e conta do banco); se o lançamento for estornado, pode ser importado de novo.

**Atenção:** PDF escaneado (imagem) não tem texto para ler; peça ao banco o OFX ou o PDF gerado pelo internet banking.

### Lançamentos contábeis (layout padrão)
Use **Gerar planilha modelo (.xlsx)**, preencha (`LANCAMENTO;DATA;CONTA;DC;VALOR;HISTORICO;CENTRO_CUSTO;DOCUMENTO`), escolha o arquivo, **Pré-visualizar** e **Importar**.

### Plano referencial da RFB
**Onde fica:** *Integrações › Importar › Plano referencial da RFB (tabela oficial)* (administrador).
1. Escolha o **tipo de plano** (ex.: PJ em geral – Lucro Real) e o **ano**.
2. Escolha o arquivo oficial (.txt ou .csv, com cabeçalho contendo ao menos `CODIGO` e `DESCRICAO`), obtido no portal do SPED, e clique em **Importar tabela**. Reimportar atualiza sem perder vínculos.

### Vínculo ao plano referencial
**Onde fica:** *Integrações › Importar › Vínculo ao plano referencial (RFB)*.
1. Escolha o **exercício**, **Carregar** e **Usar este plano**.
2. Vincule cada conta analítica a uma referencial de mesma natureza: selecione a conta, busque a referencial e clique em **Vincular** (ou **Remover vínculo**).
3. **Sugerir vínculos** propõe pela semelhança das descrições (confira antes de **Aceitar sugestões**); **Copiar do ano anterior** reaproveita.
4. Marque *Mostrar só pendências* para ver as contas com movimento sem vínculo, que **impedem a ECD e a ECF**.

## Exportar lançamentos

**Onde fica:** *Integrações › Exportar › Lançamentos contábeis*.
Escolha o período, marque (ou não) *Incluir estornados e estornos*, clique em **Gerar** e exporte em Excel, PDF ou CSV.

## Contabilização em lote

**Para que serve:** escolher *quando* os documentos dos outros módulos (notas fiscais, baixas de duplicatas e folha de pagamento) viram lançamentos contábeis. Com o tipo de contabilização **On-line** (o padrão, como sempre foi), cada nota, baixa ou folha gera o lançamento na hora. Com **Em lote**, os módulos só deixam uma *pendência de contabilização*, e você gera os lançamentos de uma vez, depois de conferir.

**Como ligar:** em *Cadastros › Parâmetros* (aba **Contábil** no Vorticis Suite Logística), grupo *Tipo de contabilização*, escolha **Em lote** e clique em **Salvar**. A mudança vale para a empresa ativa e não altera os lançamentos que já existem. Para voltar ao normal, escolha **On-line**.

**Onde fica:** *Integrações › Exportar › Contabilização em lote* (o item só aparece quando a empresa está em lote).

**Como usar:**
1. Escolha a **origem** (Fiscal, baixas de duplicatas, Folha de pagamento, Frota ou todas) e, se quiser, o período (*De* e *Até*). Clique em **Consultar**.
2. A tela mostra um resumo por mês e origem e a lista das pendências, com data, descrição e valor.
3. Marque as pendências (ou **Marcar todas**) e clique em **Ver prévia** para conferir as partidas do lançamento sem gravar nada.
4. Clique em **Contabilizar selecionadas**. O sistema gera os lançamentos pelas mesmas regras da contabilização on-line (regras de contabilização das notas, contas dos Parâmetros do Fiscal, regras da folha). As pendências que não puderem ser contabilizadas (por exemplo, sem regra ou período fechado) continuam na lista, com o motivo, e não impedem as outras.
5. Cada vez que você contabiliza, é criado um **lote**. Em *Lotes já contabilizados*, selecione um lote e clique em **Desfazer lote** para estornar os lançamentos dele: as pendências voltam para a lista e podem ser contabilizadas de novo.

**O que gera pendência no modo Em lote:** a gravação ou importação de notas fiscais, a baixa de duplicatas (a baixa é registrada e o saldo da duplicata diminui na hora; só o lançamento contábil espera) o botão **Contabilizar** da folha e das provisões da folha e o pagamento de diárias da Frota (débito na despesa de diárias, crédito na conta a pagar ou caixa informadas em *Cadastros › Parâmetros › Frota*). As provisões de tributos (Simples, IRPJ/CSLL, PIS/COFINS) continuam sendo lançadas na hora. Se a baixa for estornada antes de entrar em um lote, a pendência dela é cancelada sozinha.

**Dica:** o programa que processa cada origem precisa estar instalado. No Vorticis Suite Logística todas as origens estão disponíveis.

## Conciliação automática

**Para que serve:** conferir, numa conta (fornecedores, clientes, bancos), o que já foi pago ou recebido.
**Onde fica:** *Lançamentos › Conciliações › Conciliação automática*.

1. Informe a conta (ou reduzido; vazio = todas) e o período; **Consultar**.
2. **Conciliar automaticamente (nº do documento)** concilia as partidas de débito e crédito do mesmo documento que se anulam (ex.: a NF na compra e o pagamento da mesma NF).
3. Ou marque partidas manualmente e clique em **Conciliar selecionadas**. **Desfazer conciliação** reabre.
4. *Mostrar conciliadas* e *Somente com documento* filtram a lista. O que fica em aberto compõe o saldo da conta.

## Fechamento de períodos

**Para que serve:** impedir alterações em meses já conferidos.
**Onde fica:** *Lançamentos › Fechamentos › Fechamento de períodos*.

1. **Carregar** a lista de competências.
2. Selecione o mês e clique em **Fechar competência** (em ordem) ou **Reabrir competência**.

## Reorganizar lançamentos

*Lançamentos › Contábeis › Reorganizar lançamentos*: refaz a numeração sequencial dos lançamentos (útil depois de exclusões). Clique em **Reorganizar numeração**. Exige permissão de fechar períodos.

## Reorganizar saldos

*Lançamentos › Fechamentos › Reorganizar saldos*: recalcula os saldos mensais a partir das partidas. Clique em **Recalcular saldos**; a tela mostra, por conta e mês, o saldo antes e depois. Use se a conferência apontar divergências (veja *Diferenças de saldos*).

## Encerramento do exercício

**Para que serve:** zerar as contas de resultado contra a apuração e transferir o lucro ou prejuízo ao patrimônio líquido, em 31/12.
**Onde fica:** *Lançamentos › Fechamentos › Encerramento do exercício*.

1. Escolha o **exercício**.
2. Confira a **conta de apuração do resultado** e as contas de **lucros** e **prejuízos acumulados** (vêm da aba Contabilidade da empresa).
3. **Calcular prévia** para ver os valores.
4. **Encerrar exercício**. Os lançamentos saem como tipo "E" (exigido pela ECD).
5. Para ajustar algo depois, use **Desfazer o encerramento**, informe o motivo (obrigatório) e confirme: os dois lançamentos são estornados.

## Imobilizado

### Regras de depreciação (grupos)
**Onde fica:** *Imobilizado › Regras de depreciação (grupos)*. Já vêm cadastradas as taxas fiscais da RFB (edificações 4%, máquinas e móveis 10%, veículos de passageiros e computadores 20%, veículos de carga 25%, software 20%; terrenos não depreciam) com contas sugeridas. Edite taxa, vida útil, contas e marque *Deprecia* / *Intangível (amortização)*, ou crie novos grupos.

### Cadastro de bens
**Onde fica:** *Imobilizado › Cadastro de bens*.
1. **Incluir** e escolha o **grupo** (traz taxa e contas).
2. Informe plaqueta, local, nota fiscal e fornecedor, datas de aquisição e de início de uso.
3. Informe **custo**, **valor residual**, **vida útil (meses)** e **método** (linear ou soma dos dígitos), conforme o CPC 27.
4. **Turnos de uso**: 2 turnos acelera a depreciação fiscal em 1,5; 3 turnos em 2,0.
   - **Depreciar a partir da data de aquisição, proporcional aos dias** (opcional): a depreciação começa no dia da aquisição, e não no mês do início de uso. No primeiro mês entram só os dias desde a aquisição (inclusive). Exemplo: bem comprado em 21/04, quota de R$ 1.000,00 → em abril deprecia 10 de 30 dias = R$ 333,33. A parte que faltou é lançada no mês seguinte ao fim da vida útil. Na baixa, o mês da baixa deprecia os dias até a véspera. Sem a opção, vale a regra normal: quota cheia a partir do mês do início de uso, e o mês da baixa não deprecia.
5. Se o bem já vinha depreciado, preencha **Já depreciado** (depreciação acumulada antes do Vorticis).
6. Confirme as contas do bem, da depreciação acumulada e da despesa. **Salvar**.
7. Para dar baixa: informe a **data** e o **motivo** (venda, perda, doação...).

### Cálculo e lançamento da depreciação
**Onde fica:** *Imobilizado › Cálculo e lançamento da depreciação*.
1. Escolha a **competência** e clique em **Calcular**: aparecem a quota contábil e a fiscal e a diferença a controlar no LALUR.
2. **Lançar na contabilidade** gera o lançamento (despesa contra depreciação acumulada) no último dia do mês.
3. **Estornar competência** desfaz o mês mais recente.

## Consultas

Respostas rápidas, sem gerar relatório.

### Saldos de contas
*Consultas › Saldos de contas*: informe a data, clique em **Consultar** e filtre por código ou nome. Mostra saldo e movimento no ano.

### Lançamentos (consulta)
*Consultas › Lançamentos*: informe o período e filtre por conta, texto do histórico, valor ou número do lançamento.

### Diferenças de saldos
*Consultas › Diferenças de saldos*: informe o período e **Analisar**. Mostra o saldo inicial, o final e a diferença de cada conta, com alertas (débitos diferentes de créditos, saldo contrário à natureza, divergência entre partidas e saldos mensais). Se houver divergência interna, use *Reorganizar saldos*.

## Relatórios contábeis

Todos aparecem na tela e têm os botões **PDF**, **Excel (.xlsx)** e **CSV**. Exportar exige a permissão *Exportar relatórios*. Nos PDFs há as opções **Assinatura do sócio/responsável** e **Assinatura do contador**, que incluem o bloco de assinaturas ao final com os dados dos cadastros de sócios e contadores.

### Livro Diário
*Relatórios › Contábeis › Livro Diário*: informe o período e **Gerar**.

### Livro Razão
*Relatórios › Contábeis › Livro Razão*: clique numa conta da lista *Contas analíticas* (ou digite o reduzido) e **Gerar** para ver o razão do período com saldo corrente.

### Balancete de verificação
*Relatórios › Contábeis › Balancete de verificação* (atalho **Ctrl+B**): saldo anterior, débitos, créditos e saldo final. Opções: *Incluir contas zeradas* e *Detalhar participantes* (uma linha por cliente/fornecedor abaixo da conta).

### Balanço Patrimonial
*Relatórios › Contábeis › Balanço Patrimonial*: escolha a **data** do balanço e o **nível** de detalhe e clique em **Gerar**. Antes do encerramento, o resultado do período aparece no patrimônio líquido para o balanço fechar. Use os botões de exportação para PDF, Excel ou CSV.

### Demonstração do Resultado (DRE)
*Relatórios › Contábeis › Demonstração do Resultado (DRE)*: informe o **período** e o **nível** de detalhe e clique em **Gerar**. A DRE ignora os lançamentos de encerramento. Exporte em PDF, Excel ou CSV. O Livro Contábil Completo continua incluindo o Balanço e a DRE.

### DMPL
*Relatórios › Contábeis › DMPL*: mutações do patrimônio líquido (CPC 26) por componente (capital, reservas, lucros ou prejuízos acumulados). Informe o período e **Gerar**. O saldo final confere com o balanço.

### DLPA
*Relatórios › Contábeis › DLPA*: demonstração de lucros ou prejuízos acumulados (Lei 6.404, art. 186). Informe o período e **Gerar**.

## Relatórios gerenciais

O menu só aparece quando o parâmetro **Contabilidade gerencial** está ligado em *Cadastros › Parâmetros* (grupo *Centros de custo e contabilidade gerencial*). Ao ligar ou desligar e salvar, o menu muda na hora.

*Relatórios › Gerenciais* reúne relatórios para acompanhar custos, receitas e metas por centro de custo. Todos aparecem na tela e têm os botões **PDF**, **Excel (.xlsx)** e **CSV**, com as mesmas opções de assinatura dos relatórios contábeis (veja [Assinaturas](#assinaturas) e [PDF assinado](#pdf-assinado)). Os lançamentos de encerramento do exercício não entram. Os centros de custo vêm do cadastro [Centros de custo](#centros-de-custo): cada centro sintético mostra a soma dos centros analíticos abaixo dele.

### Contabilização por centro de custo
*Relatórios › Gerenciais › Contabilização por centro de custo*: mostra, para cada centro de custo, os débitos, créditos e o saldo do período e, abaixo, as contas com movimento.
1. Informe o período (datas inicial e final).
2. Se quiser, escolha um **centro** (o relatório mostra ele e os centros abaixo dele), digite uma **conta ou grupo** (por exemplo, 4.2 mostra só as contas que começam com 4.2) e o **nível do centro** (1 mostra só os centros de topo, com as contas somadas; 0 mostra todos).
3. Marque **Detalhar lançamentos** para ver cada lançamento (data, número e histórico) abaixo da conta.
4. Clique em **Gerar**. Partidas sem centro de custo aparecem em *Sem centro de custo*.

### Orçamento
*Relatórios › Gerenciais › Orçamento*: onde se digita o orçamento do ano. Cada linha é uma conta de resultado (receita, custo ou despesa), um centro de custo (opcional) e o valor de cada mês, de Jan a Dez. Quem não tem a permissão *Manter o orçamento* só consulta.
1. Informe o **ano** e clique em **Abrir orçamento do ano**.
2. Clique em **Adicionar linha** e digite o código da conta (a descrição aparece sozinha), o centro e os valores dos 12 meses. **Remover linha** apaga a linha selecionada.
3. Para partir do que já existe, informe o **reajuste (%)** (por exemplo, 5 para mais 5%; negativo reduz) e use **Copiar orçamento do ano anterior** ou **Copiar realizado do ano anterior**. A grade é preenchida para você conferir e ajustar; nada é gravado ainda.
4. Para importar de uma planilha, escolha o arquivo (.xlsx ou .csv) em **Procurar...** e clique em **Importar planilha**. A primeira linha tem os títulos: *Conta*, *Centro de custo* (opcional) e *Jan* a *Dez* (ou, em vez dos meses, as colunas *Mês* de 1 a 12 e *Valor*). Linhas repetidas da mesma conta e centro são somadas. Se alguma linha tiver erro, o sistema informa o número da linha e não importa nada.
5. Clique em **Salvar orçamento**. Ele substitui o orçamento anterior do mesmo ano.

Digite os valores como positivos, na natureza da conta: a receita e a despesa orçadas são números positivos.

### Orçado x Realizado
*Relatórios › Gerenciais › Orçado x Realizado*: compara o orçamento com o que foi lançado na contabilidade.
1. Informe o **ano** e o **mês inicial e final** (1 a 12).
2. Se quiser, filtre por **centro** ou por **conta ou grupo**. Marque **Somar os centros** para ver uma linha por conta, sem separar por centro.
3. Marque **Mês a mês** para ver uma linha por mês com o acumulado; sem essa opção, o relatório mostra o total dos meses escolhidos.
4. Clique em **Gerar**. As colunas são *Orçado*, *Realizado*, *Diferença* (realizado − orçado) e *%* (realizado ÷ orçado). No total do período aparecem ainda as receitas, os custos e despesas e o resultado.

### Relação de custos
*Relatórios › Gerenciais › Relação de custos*: lista os custos e despesas (contas de resultado de natureza devedora, inclusive deduções da receita) do período, com a **participação %** de cada linha no total.
1. Informe o período e, se quiser, centro, conta ou grupo e nível.
2. Em **agrupamento**, escolha *Por centro de custo (e contas)* ou *Por conta (e centros)*.
3. Em **comparação**, escolha *Período anterior (mesma duração)*, *Mesmo período do ano anterior* ou *Sem comparação*. Com comparação, o relatório mostra o valor do outro período, a variação e a variação %.
4. Clique em **Gerar**.

### Receita por centro de custo
*Relatórios › Gerenciais › Receita por centro de custo*: receita de cada centro mês a mês (até 24 meses), com o **% do total**, os custos e despesas do centro, a **margem** (receita − custos − despesas) e a **margem %** (margem ÷ receita). Informe o período, o centro e o nível (opcionais) e clique em **Gerar**.

## Relatórios de depreciação

**Onde fica:** *Relatórios › Depreciação*. Todos aparecem na tela e exportam em PDF, Excel e CSV. Os valores são os da última depreciação lançada de cada bem (*Imobilizado › Cálculo e lançamento da depreciação*); a depreciação anterior à implantação entra no acumulado.

- **Relação de bens**: código, descrição, grupo, plaqueta, localização, nota fiscal, datas de aquisição e de início do uso, valor e situação (em uso ou baixado).
- **Valor depreciado**: para cada bem, o valor, a depreciação anterior à implantação, a lançada no sistema, a acumulada, o **% depreciado** e o último mês lançado.
- **Valor residual**: valor do bem, depreciação acumulada, **valor contábil líquido**, valor residual, **saldo a depreciar** e vida útil.
- **Resumo por grupo**: quantidade de bens, valor, depreciação acumulada e valor contábil líquido de cada grupo (regra de depreciação), com a taxa anual.

Escolha o grupo (ou *Todos os grupos*), marque **Incluir bens baixados** se quiser vê-los e clique em **Gerar**.

## Livro Contábil Completo

**Para que serve:** gerar em um único PDF o livro com termos, demonstrações e assinaturas, seguindo a ITG 2000.
**Onde fica:** *Relatórios › Contábeis › Livro Contábil Completo*.

1. Informe o **período** e o **número do livro**.
2. Marque o que entra: termo de abertura, Diário, Razão, Balancete, Balanço, DRE, DMPL, DLPA e termo de encerramento.
3. Marque as **assinaturas** (sócio/responsável e contador). O representante legal e o contador vêm dos cadastros; confira nome, CPF e CRC.
4. O **NIRE** vem do cadastro da empresa (somente leitura; aparece aviso se estiver vazio). Os dados de **arquivamento dos atos** são opcionais.
5. Para assinar o PDF com certificado digital, marque **Assinar digitalmente o PDF** (veja [PDF assinado](#pdf-assinado)).
6. Clique em **Gerar Livro Contábil Completo (PDF)**.

As folhas são numeradas, cada parte começa em página nova e o total de folhas do termo é calculado depois da montagem.

## Termos de abertura e encerramento

**Onde fica:** *Relatórios › Contábeis › Termos (abertura e encerramento)*.
1. Informe o período, o livro e o número do livro.
2. O **total de folhas** é opcional: em branco, o termo sai com linha para preencher à mão.
3. Confira representante legal (nome/CPF) e contador (nome/CRC), NIRE e arquivamento.
4. Clique em **Gerar Termo de Abertura (PDF)** ou **Gerar Termo de Encerramento (PDF)**; cada um sai em seu PDF.

## Assinaturas

Os blocos de assinatura dos PDFs usam o **representante legal** (cadastro de Sócios) e o **contador** (cadastro de Contadores, o signatário ECD/ECF ou o responsável técnico). Se faltar algum dado, cadastre em *Cadastros › Empresas › Sócios* e *Contadores*.

## PDF assinado

Nos livros e termos, marque **Assinar digitalmente o PDF** para aplicar a assinatura digital com o certificado padrão (veja [Certificados digitais](08-configuracoes.md#certificados-digitais)). O PDF é assinado (formato PAdES) com o certificado padrão do **contador** ou, na falta dele, do **responsável**. Se a assinatura foi pedida e não há certificado padrão utilizável, o sistema mostra o aviso na tela e **não gera** o PDF: cadastre o certificado ou desmarque a opção.
