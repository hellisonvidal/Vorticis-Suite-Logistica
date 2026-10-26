# Frota

Módulo **Vorticis Frota** (ícone grafite): coordenação da frota e manutenção para transportadoras. A empresa escolhida no topo é a **transportadora** (cadastre-a em *Cadastros › Transportadoras* com o CNPJ e marque o módulo **Frota**). Emissão de CT-e/MDF-e e rastreamento por GPS ficam para uma próxima etapa.

## Onde o Frota está

O Frota é a base do **[Vorticis Suite Logística](13-logistica.md)**, o programa único da transportadora: ali, os menus **Operação** e **Manutenção** são os deste capítulo, e os relatórios da frota ficam no menu **Relatórios** (submenus *Frota e operação* e *Manutenção*), ao lado do financeiro e, no menu **Gerencial**, do contábil e da folha. O Vorticis Frota também pode ser aberto sozinho.

## Ordem dos cadastros

1. **A transportadora:** *Cadastros › Transportadoras*, com o módulo **Frota** marcado (e Gerencial, Contábil e Fiscal, se for usar). Escolha-a no topo da tela.
2. **Parâmetros:** *Cadastros › Parâmetros*, com a antecedência dos avisos e a tolerância do consumo (já vêm com 30 dias e 15%), o fator rodoviário das rotas e os valores das diárias.
3. **Frotas** (se você separa os veículos em grupos), **Veículos**, **Motoristas** (e ajudantes) e **Rotas**.
4. **Documentos e vencimentos** de cada veículo e motorista.
5. **Manutenção:** os **Planos de manutenção** (preventivas) e o catálogo de **Peças e serviços**.
6. No dia a dia: **Abastecimentos**, **Viagens**, **Diárias** e **Ordens de coleta** (menu Operação), **Ordens de serviço**, **Autorização de serviços** e **Notas de compra** (menu Manutenção); acompanhe tudo no **Painel da frota**.

## Veículos

**Onde fica:** *Cadastros › Veículos*.

1. **Novo veículo**: o código já vem sugerido. Informe a **Placa** (padrão antigo ABC-1234 ou Mercosul ABC1D23), o **RENAVAM** (o sistema confere o dígito), o **Tipo** (cavalo mecânico, truck, toco, carreta, bitrem, van, utilitário, reboque, semirreboque...), marca, modelo e anos.
2. Escolha o **Combustível**. Reboque e semirreboque ficam como **Não abastece**.
3. Informe o **Km atual** e a **Média esperada (km/l)**: é com ela que o sistema avisa quando o consumo cai.
4. Se a empresa tem [frotas](#frotas) cadastradas, escolha a **Frota** do veículo.
5. **Gravar**. Para tirar um veículo de uso, desmarque **Veículo ativo** (um veículo com abastecimentos, manutenções ou viagens não pode ser excluído).

O km atual é atualizado sozinho pelos abastecimentos, ordens de serviço e viagens.

## Motoristas

**Onde fica:** *Cadastros › Motoristas*.

1. **Novo motorista**: nome, **CPF** (conferido), número da **CNH**, **categoria** e **validade**, telefone.
2. **Gravar**. A coluna **Situação da CNH** mostra quantos dias faltam para vencer; a validade também aparece no **Painel da frota**.

O exame toxicológico e o curso MOPP são lançados em *Documentos e vencimentos*, escolhendo o motorista.

## Planos de manutenção

**Onde fica:** *Manutenção › Planos de manutenção*.

Cada plano é uma preventiva de um veículo: troca de óleo, filtros, revisão de freios, lubrificação...

1. **Novo plano**: escolha o **Veículo** e descreva o **Serviço**.
2. Informe **A cada (km)**, **A cada (dias)** ou os dois: vale o que vencer primeiro.
3. Informe a **Última vez** (km e/ou data) em que o serviço foi feito. Com intervalo em dias, a data é obrigatória.
4. **Gravar**. A coluna **Próxima** mostra o km e a data da próxima troca e a **Situação** fica **A VENCER** (a 10% do intervalo de km ou dentro da antecedência dos avisos) ou **VENCIDA**.

Ao concluir uma ordem de serviço **preventiva** deste plano (*Manutenção › Ordens de serviço*), a última vez é atualizada sozinha.

## Documentos e vencimentos

**Onde fica:** *Cadastros › Documentos e vencimentos*.

Licenciamento (CRLV), IPVA, seguro, ANTT/RNTRC, aferição do tacógrafo, AET, exame toxicológico, curso MOPP e outros.

1. **Novo documento**: escolha o **Documento**, depois o **Veículo** ou o **motorista** (um ou outro; sem nenhum dos dois, o documento é da própria transportadora, como o RNTRC da empresa).
2. Informe o **Número**, o **Vencimento** e o valor, se quiser.
3. **Gravar**. Quando renovar, selecione o documento e altere o vencimento.

A coluna **Situação** mostra "vence em X dia(s)" dentro da antecedência dos avisos, ou "vencido há X dia(s)".

## Abastecimentos

**Onde fica:** *Operação › Abastecimentos* (atalho **Ctrl+B**).

1. Escolha o **Veículo** (o combustível do veículo já vem escolhido) e o **Motorista**.
2. Informe a **Data**, o **Km do hodômetro**, os **Litros**, o **Valor total** e o posto.
3. Deixe marcado **Completou o tanque** quando encheu o tanque. Abastecimento parcial: desmarque.
4. **Gravar**. A mensagem mostra a média desde o último tanque cheio e avisa se ficou abaixo do esperado.

Como a média é calculada: entre dois abastecimentos de **tanque cheio**, o km rodado dividido pelos litros colocados (os parciais do meio entram na soma). **Arla 32** é lançado como combustível próprio e não entra na média. O sistema recusa km menor que o do abastecimento anterior e combustível que não combina com o veículo.

Na lista, a coluna **Média** traz ⚠ quando a média ficou abaixo da esperada do veículo menos a tolerância dos *Parâmetros* (pode indicar vazamento, desvio de combustível ou necessidade de manutenção).

## Peças e serviços

**Onde fica:** *Manutenção › Peças e serviços*.

Catálogo das peças e serviços que a frota costuma usar (filtros, óleo, pastilhas, pneus, alinhamento, mão de obra...).

1. **Novo item**: o código já vem sugerido. Escolha **Peça** ou **Serviço**, escreva a **Descrição**, a **Unidade** (UN, L, H...) e o **Valor de referência**.
2. **Gravar**. Desmarque **Item ativo** para tirar o item da lista das ordens sem perder o histórico.

Na ordem de serviço, ao escolher um item do catálogo, a descrição e o valor de referência já vêm preenchidos (e podem ser alterados).

## Ordens de serviço

**Onde fica:** *Manutenção › Ordens de serviço* (atalho **Ctrl+O**).

Cada ordem passa por estas situações: **Aberta** → **Aguardando autorização** → **Autorizada** (ou **Reprovada**) → **Concluída**. Pode ser **Cancelada** a qualquer momento antes de concluir.

1. **Nova ordem**: escolha o **Veículo**. Se for uma preventiva, escolha o **Plano atendido** (o tipo vira **Preventiva** e o serviço vem do plano). Sem plano, é **Corretiva**.
2. Informe a **abertura** e a **previsão** de entrega, o **Km do veículo**, o **Serviço**, a **Oficina** e o **Solicitante**.
3. Em **Peças e serviços**, escolha um item do catálogo ou deixe "(digitar)" e escreva a descrição; informe a quantidade e o valor unitário e clique em **Adicionar**. Repita para cada peça e serviço. Para tirar um item, selecione-o e clique em **Remover item**. O total de peças e de serviços é a soma dos itens (sem itens, digite os dois valores).
4. **Gravar**: o número da ordem é automático.
5. Clique em **Pedir autorização**. Se o valor estiver dentro do limite dos *Parâmetros*, a ordem já fica **Autorizada**; acima dele, fica **Aguardando autorização** até alguém decidir em *Manutenção › Autorização de serviços*.
6. Com a ordem autorizada e o serviço feito, escolha a data em **Conclusão** e clique em **Concluir**. A preventiva concluída renova o plano a partir deste km e desta data.

Depois de enviada para autorização, a ordem não pode mais ser alterada. Para mudar o orçamento, clique em **Reabrir**: ela volta a ficar aberta e precisa de nova autorização. A ordem **reprovada** pode ser alterada e enviada de novo. Só se exclui uma ordem aberta ou cancelada; a que já passou pela autorização é **Cancelada**, para o histórico ficar completo.

Embaixo da ordem aparecem as **notas de compra** ligadas a ela, com o total.

## Autorização de serviços

**Onde fica:** *Manutenção › Autorização de serviços*. Precisa da função **Autorizar ou reprovar ordens de serviço de manutenção** (grupo Frota) no perfil do usuário (*Configurações › Usuários*, aba *Perfis de acesso*).

1. A lista mostra as ordens **aguardando autorização**, das mais antigas para as mais novas, com o total.
2. Clique na ordem: aparecem as peças e os serviços, a oficina e quem pediu.
3. Clique em **Autorizar**, ou escreva o motivo em **Observação** e clique em **Reprovar** (sem motivo, o sistema não deixa reprovar).

Embaixo ficam as decisões dos últimos 30 dias (quem decidiu, quando e o valor). As ordens autorizadas sozinhas pelo limite aparecem como **Autorizada (automática)**, pelo usuário **Sistema**. O **Painel da frota** também avisa quando há ordens aguardando autorização.

## Notas de compra

**Onde fica:** no programa Frota, *Manutenção › Notas de compra*. No Vorticis Suite Logística esta tela não fica no menu: as notas das oficinas e das peças são lançadas como notas fiscais de entrada, em *Operações › Compras › Notas fiscais de entrada*.

1. **Nova nota**: escolha a **Ordem de serviço** atendida. A oficina da ordem vem como fornecedor, e embaixo aparece o valor da ordem para conferir com a nota.
2. Informe o **Fornecedor**, o **CNPJ/CPF**, o **Número** e a **série**, a **Emissão** e, se tiver, a **Chave de acesso** de 44 dígitos (o sistema confere o dígito e se a chave é do CNPJ informado).
3. Separe o valor de **Peças** e de **Serviços** e clique em **Gravar**. Se a nota passar o valor da ordem, o sistema avisa.

A mesma nota (fornecedor, número e série) não pode ser lançada duas vezes. Nota sem ordem de serviço é aceita, mas aparece destacada no relatório de notas.

## Viagens

**Onde fica:** *Operação › Viagens*.

1. **Nova viagem**: veículo, motorista, cliente, **origem** e **destino**, data e **km de saída** (vem o km atual do veículo).
2. Informe o **Valor do frete** e as despesas: **pedágio**, **diárias** e outras.
3. Ao chegar, informe a **chegada** e o **km de chegada** e grave como **Concluída**. Viagem **Cancelada** não entra nos relatórios.

O combustível da viagem não é digitado aqui: o relatório *Resultado das viagens* soma os abastecimentos do veículo entre a saída e a chegada.

## Frotas

**Para que serve:** separar os veículos em grupos (frota própria, de agregados ou de terceiros) para ver os custos de cada grupo.

**Onde fica:** *Cadastros › Frotas*.

1. **Nova frota**: o código já vem sugerido. Informe a **descrição**, o **tipo** (própria, agregada ou terceiros) e o **responsável**.
2. Se quiser, escolha o **centro de custo padrão** da frota: as diárias dessa frota usam esse centro quando você não escolhe outro.
3. **Gravar**. Para parar de usar uma frota, desmarque **Frota ativa**. Excluir uma frota não apaga os veículos: eles ficam sem frota.

**Vincular veículos:** em *Cadastros › Veículos*, escolha a **Frota** do veículo. A coluna *Veículos* da lista de frotas mostra quantos há em cada uma.

**Centro de custo da frota:** em *Cadastros › Centros de custo* (programa Contábil/Gerencial) há o campo opcional **Frota**: escolha a frota que o centro representa. Os relatórios [Custos por frota](#relatorios-da-frota) e a frota das diárias usam esse vínculo.

## Rotas

**Para que serve:** cadastrar os percursos que a transportadora faz, com a distância, o tempo e os pedágios estimados.

**Onde fica:** *Cadastros › Rotas*.

1. **Nova rota**: informe o código e a **descrição**.
2. Monte o percurso: escolha a **UF** e o **município** e clique em **Adicionar**, na ordem do percurso. A primeira cidade é a **origem**, a última é o **destino** e as do meio são as **paradas**. Use **Subir**, **Descer** e **Remover** para ajustar.
3. A **distância** aparece sozinha: é a distância em linha reta entre cidades seguidas, multiplicada pelo **fator rodoviário** (padrão 1,25, em *Cadastros › Parâmetros › Frota*). O **tempo estimado** sai da velocidade média (padrão 60 km/h).
4. Para uma distância mais fiel, clique em **Buscar distância rodoviária**: o sistema consulta um serviço de rotas gratuito na internet (OpenStreetMap). Sem internet, aparece um aviso e a estimativa continua valendo.
5. Você também pode **digitar a distância** (km): ela passa a valer como informada por você e não muda mais quando você altera as paradas. **Recalcular estimativa** volta para o cálculo automático.
6. Informe os **pedágios** estimados e clique em **Gravar**.

A lista de municípios e as coordenadas vêm do IBGE (base kelvins/municipios-brasileiros, licença MIT), já dentro do programa.

## Diárias

**Para que serve:** controlar as diárias pagas a motoristas e ajudantes, com adiantamento e prestação de contas.

**Onde fica:** *Operação › Diárias*. O motorista ou ajudante precisa estar em *Cadastros › Motoristas*.

1. Antes, em *Cadastros › Parâmetros › Frota*, cadastre o **valor de cada tipo de diária** (pernoite, alimentação, refeição, hospedagem e outra).
2. **Nova diária**: escolha a **data**, o **colaborador**, a **função**, o veículo, a frota, a rota ou a viagem (se houver), o **tipo da diária**, a **quantidade** e, se for o caso, o **adiantamento**. O valor unitário vem da tabela e o **total** é calculado; você pode alterar o valor unitário. Frota e centro de custo em branco são preenchidos sozinhos (pela frota do veículo ou do centro de custo).
3. **Gravar**. A diária fica **Lançada**.
4. **Pagar:** informe o **vencimento** e clique em **Pagar (gerar conta a pagar)**. O sistema cria a conta a pagar no Financeiro (com o valor do adiantamento ou, se não houve adiantamento, o total) e a diária passa a **Paga**. Em seguida, a diária é contabilizada (veja [Contabilização das diárias](#contabilizacao-das-diarias)).
5. **Acertar** (só diárias com adiantamento): quando o colaborador prestar contas, informe o **valor gasto** e clique em **Acertar**. O saldo é o adiantamento menos o gasto: se sobrou, o colaborador **devolve**; se faltou, a empresa **paga a diferença**. A diária passa a **Acertada**.

Diária paga ou acertada não pode mais ser alterada nem excluída. Use os filtros de período, colaborador e frota no alto da tela e, em *Relatórios*, **Diárias por colaborador**, **Diárias por frota** e **Diárias pagas e acertadas**.

### Contabilização das diárias

Quando a diária é paga, o sistema gera o lançamento contábil: **débito** na conta de despesa de diárias (com o centro de custo da diária, que vem da frota) e **crédito** na conta a pagar ou no caixa, no valor pago.

1. Antes, em *Cadastros › Parâmetros › Frota*, grupo **Contabilização das diárias**, informe a **conta de despesa das diárias** e a **conta de crédito** (contas analíticas do plano de contas). Informe as duas ou nenhuma. Clique em **Salvar**.
2. **Contabilização On-line** (o padrão): o lançamento sai na hora, ao clicar em **Pagar**; a tela avisa que o lançamento foi gerado. Se as contas ainda não foram informadas, a diária é paga normalmente e a tela avisa que o pagamento não foi contabilizado. Se o lançamento não puder ser feito (por exemplo, período fechado), a diária é paga e fica nas pendências de contabilização, com o aviso do motivo.
3. **Contabilização Em lote:** ao pagar, fica só a pendência da Frota. Em *Integrações › Exportar › Contabilização em lote*, escolha a origem **Frota**, confira a prévia e clique em **Contabilizar selecionadas**; veja [Contabilização em lote](03-contabil.md#contabilizacao-em-lote). Se as contas não estiverem informadas, a pendência continua na lista com a orientação. **Desfazer lote** estorna o lançamento e a pendência volta para a lista.

O valor lançado é o do pagamento (o adiantamento ou, sem adiantamento, o total da diária). O acerto da prestação de contas não gera outro lançamento.

## Ordens de coleta

**Para que serve:** agendar e acompanhar as coletas de mercadoria nos clientes.

**Onde fica:** *Operação › Coletas › Ordens de coleta*.

1. **Nova ordem**: informe a **data e hora** agendadas, o **remetente** e o **destinatário** (clientes ou fornecedores já cadastrados), o **endereço de coleta**, a **mercadoria**, os **volumes**, o **peso** e o **valor**. Escolha o veículo, o motorista e a rota, se já souber. O número é sequencial.
2. **Gravar**. A coleta fica **Agendada**.
3. Para acompanhar, mude a **situação** e grave: **Em coleta** quando o motorista sair, **Coletada** quando recolher a mercadoria. **Cancelada** vale para coletas agendadas ou em andamento. Coleta coletada ou cancelada não pode mais ser alterada.
4. **Imprimir ordem (PDF)** gera a ordem de coleta para entregar ao motorista, com campos de assinatura.
5. Na lista, filtre por **período** e por **situação**.

As coletas também alimentam o TMS: no conhecimento de transporte, escolha a ordem de coleta para trazer remetente, destinatário, carga, veículo e motorista (veja [Conhecimentos de transporte](14-tms.md#conhecimentos-de-transporte-ct-e)).

## Painel da frota

**Onde fica:** *Relatórios › Painel da frota* (atalho **Ctrl+P**).

Mostra, primeiro os vencidos e depois os a vencer:

- documentos e CNHs vencidos ou dentro da antecedência dos avisos;
- manutenções preventivas vencidas ou a vencer;
- veículos cuja última média de consumo ficou abaixo do esperado.

Embaixo do título, os números da frota: veículos ativos, motoristas e viagens em andamento. Clique em **Atualizar** depois de lançar algo.

## Relatórios da frota

**Onde fica:** *Relatórios › Frota e operação* (no Vorticis Suite Logística) ou *Relatórios* (no Frota).

Escolha o **Período** e, nos relatórios que têm, o **Veículo** e o **Motorista** (**(todos)** mostra tudo) e clique em **Gerar**; o relatório aparece na tela e pode ser exportado em PDF, Excel ou CSV.

- **Custos por veículo:** combustível, manutenção (ordens de serviço, menos as canceladas e reprovadas) e despesas de viagem de cada veículo, o km rodado, a média e o **custo por km**.
- **Consumo de combustível:** cada média de tanque cheio a tanque cheio, com a média esperada e a marcação **ABAIXO DO ESPERADO**.
- **Consumo por veículo** e **Consumo por motorista:** o resumo do período: abastecimentos, litros, valor, preço médio do litro, km rodado e média km/l (por veículo, com a esperada e o alerta; por motorista, a média vai para quem fez o abastecimento que fechou o ciclo). O Arla 32 não entra.
- **Resultado das viagens:** frete menos despesas e combustível de cada viagem; viagens com resultado negativo saem em destaque.
- **Viagens por motorista**, **Viagens por cliente** e **Viagens por rota** (origem → destino): quantidade de viagens, km rodado, **frete**, despesas, resultado (frete menos despesas), frete por km e frete médio, do maior frete para o menor. Viagens canceladas não entram. É também o faturamento de frete por cliente e por rota.
- **Coletas por cliente**, **Coletas por rota** e **Coletas por motorista:** quantidade de coletas, coletadas, canceladas, volumes, peso e valor da mercadoria no período.
- **Vencimentos e manutenções:** a mesma lista do painel (documentos, CNHs e manutenções vencidos ou a vencer), para imprimir. Não pede período: é a posição de hoje.
- **Custos por frota:** combustível, manutenção, despesas de viagem e diárias de cada frota (os veículos sem frota saem em *sem frota*).
- **Diárias por colaborador** e **Diárias por frota:** as diárias do período com subtotal de cada colaborador ou frota, adiantamento e saldo do acerto.
- **Diárias pagas e acertadas:** as diárias já pagas, com o valor pago, o valor gasto e o saldo do acerto; as que têm adiantamento e ainda **aguardam acerto** saem em destaque, com o total a acertar no fim.
- **Resumo gerencial da frota:** os indicadores do período (viagens, km, frete, despesas, combustível, litros, média km/l, manutenção, diárias, custo total, resultado, custo por km e coletas) ao lado do período anterior de mesmo tamanho, com a **variação em %**, e a posição de hoje (veículos ativos, ordens de serviço em andamento e vencimentos em atraso). Para ver o mês, use do dia 1 ao último dia do mês.

## Relatórios da manutenção

**Onde fica:** *Relatórios › Manutenção*: *Acompanhamento das ordens de serviço*, *Aprovações de serviços*, *Notas fiscais de compra*, *Manutenções preventivas* e *Histórico de manutenção por veículo*.

Em cada um, informe as datas desejadas em **Período** (de/até) e clique em **Gerar**. Todos saem na tela e podem ser exportados em PDF, Excel ou CSV.

- **Acompanhamento das ordens de serviço:** as ordens abertas no período, com a situação, os **dias em aberto** (até a conclusão, ou até hoje), o valor da ordem, o valor **autorizado**, o total das **notas de compra** e a diferença. Ficam em destaque as ordens aguardando autorização e as que têm notas acima do autorizado.
- **Aprovações de serviços:** as autorizações e reprovações feitas no período, com data e hora, ordem, placa, decisão, valor, usuário e o motivo. As reprovadas ficam em destaque.
- **Notas fiscais de compra:** as notas emitidas no período, com fornecedor, CNPJ, ordem de serviço, placa e os valores de peças e serviços, com o total.
- **Manutenções preventivas:** cada plano de manutenção ativo com a última manutenção, a próxima **por km e por data**, quanto **falta** (negativo = já passou), e a situação **VENCIDA**, **A VENCER** ou **Em dia**; as vencidas vêm primeiro. Pode ser filtrado por veículo. Não pede período: é a posição de hoje.
- **Histórico de manutenção por veículo:** as ordens de serviço do período agrupadas por veículo (data, tipo, serviço, oficina, km, situação, peças e mão de obra) com subtotal de cada veículo. Ordens canceladas ou reprovadas aparecem, mas não entram nos totais. Pode ser filtrado por veículo.

## Parâmetros da Frota

**Onde fica:** *Cadastros › Parâmetros*.

- **Avisar vencimentos com antecedência de (dias):** a partir de quantos dias antes do vencimento documentos, CNHs e preventivas por data passam a **a vencer** (padrão 30).
- **Tolerância do consumo (%):** quanto a média pode ficar abaixo da esperada antes de gerar alerta (padrão 15%). Ex.: média esperada 2,5 km/l e 15% → alerta abaixo de 2,13 km/l.
- **Fator rodoviário** e **Velocidade média (km/h):** usados na estimativa de distância e tempo das [rotas](#rotas) (padrão 1,25 e 60).
- **Valor da diária** de cada tipo: sugerido ao lançar uma [diária](#diarias).
- **Conta de despesa das diárias** e **Conta de crédito:** contas do lançamento gerado quando a diária é paga (veja [Contabilização das diárias](#contabilizacao-das-diarias)).
- **Autorizar sozinhas as ordens de até (R$):** ordens de serviço com valor até este limite são autorizadas automaticamente ao clicar em **Pedir autorização**. Com 0 (padrão), toda ordem passa por *Manutenção › Autorização de serviços*.
