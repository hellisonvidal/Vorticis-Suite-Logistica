# TMS – gestão do transporte

O **TMS** do Vorticis Suite Logística cuida do caminho da carga: do preço do frete até a fatura. Ele usa os cadastros que você já tem no Frota (veículos, motoristas, rotas, ordens de coleta e viagens) e os clientes e fornecedores do programa.

O caminho completo é este:

1. **Tabela de frete**: os preços da transportadora.
2. **Cotação de frete**: o preço para um cliente. Aprovada, ela vira um conhecimento.
3. **Conhecimento de transporte (CT-e)**: o documento da carga. Depois de autorizado no emissor da SEFAZ, você registra a chave de acesso.
4. **Manifesto de carga (MDF-e)**: reúne os CT-e que vão no mesmo caminhão.
5. **Entregas e ocorrências**: o acompanhamento até a entrega, com quem recebeu.
6. **Fatura de frete**: junta os CT-e do mesmo cliente e gera a conta a receber.

**Importante:** nesta versão o programa ainda **não transmite** o CT-e nem o MDF-e para a SEFAZ. Você autoriza o documento no emissor que já usa (por exemplo, o emissor gratuito ou o do seu sistema fiscal) e informa no Vorticis a **chave de acesso** e o protocolo. A transmissão direta está prevista (*Emissão › Transmissão de CT-e e MDF-e à SEFAZ*, em breve).

**Quem pode usar:** quem tem a função **TMS** no perfil de acesso (*Configurações › Usuários*, aba *Perfis de acesso*, grupo Frota) inclui, altera e emite. Quem tem as outras funções da Frota só consulta. Os perfis que já lançavam a operação da frota receberam a função TMS automaticamente.

## Tabelas de frete

**Para que serve:** guardar os preços que a transportadora cobra, para o programa calcular o frete sozinho nas cotações e nos conhecimentos.

**Onde fica:** *Cadastros › Tabelas de frete*.

**Como usar:**
1. Clique em **Nova tabela**. O código já vem sugerido. Escreva a **Descrição** (por exemplo, "São Paulo x Rio – carga fracionada").
2. Diga para onde a tabela vale: escolha uma **Rota** já cadastrada ou, sem rota, a **UF de origem** e a **UF de destino**. Deixe *(todas)* para valer para qualquer estado.
3. Preencha os preços:
   - **Valor por kg** e **Frete mínimo**: o frete-peso é o peso vezes o valor por kg, mas nunca menos que o mínimo.
   - **Fator de cubagem (kg/m³)**: para carga leve e volumosa. O programa compara o peso real com o "peso cubado" (metros cúbicos vezes o fator; o comum é 300) e cobra pelo maior.
   - **Ad valorem (%)** e **GRIS (%)**: percentuais sobre o valor da mercadoria (seguro e gerenciamento de risco).
   - **Pedágio por fração de 100 kg**: cada 100 kg ou parte deles paga uma fração (101 kg são 2 frações).
   - **Taxa de coleta**, **Taxa de entrega** e **Outras taxas**: valores fixos. A coleta e a entrega só entram quando você marca na cotação ou no CT-e.
   - **Alíquota de ICMS (%)**: informe a alíquota que a transportadora usa nesse trecho (confirme com o seu contador). O ICMS entra "por dentro": o total já inclui o imposto.
   - **Prazo de entrega (dias)**: usado para calcular a previsão de entrega do CT-e.
4. Confira com a **Simulação**: informe peso, metros cúbicos e valor da mercadoria e clique em **Simular**. O programa mostra cada parte do frete e o total.
5. Clique em **Gravar**.

Uma tabela já usada em cotações não pode ser excluída: desmarque **Ativa** para que ela não apareça mais nas escolhas.

## Cotações de frete

**Para que serve:** passar o preço do frete ao cliente antes do transporte e guardar o que foi aprovado.

**Onde fica:** módulo **Transporte**, *Operação › Cotações de frete*.

**Como usar:**
1. Clique em **Nova cotação**. Informe a **Data**, a **Validade**, o **Cliente** e a **Tabela de frete**.
2. Escreva a **Origem** e o **Destino** e informe o **Peso**, os **m³** e o **Valor da mercadoria**. Marque **Cobrar coleta** e **Cobrar entrega** se for o caso.
3. O valor aparece na hora, parte por parte, embaixo do formulário. Clique em **Gravar**: o número é sequencial e o valor é sempre recalculado pela tabela.
4. Quando o cliente responder, selecione a cotação e clique em **Aprovar** ou **Recusar**. Uma cotação recusada pode ser **Reaberta**.
5. A cotação aprovada aparece no conhecimento de transporte (campo *Cotação aprovada*). Ao gravar o CT-e, ela passa a **Convertida em CT-e**.

Só cotações abertas podem ser alteradas. Cotações abertas ou recusadas podem ser excluídas.

## Conhecimentos de transporte (CT-e)

**Para que serve:** registrar o documento de cada carga transportada, com as partes, a carga e o valor do frete, e acompanhar a situação dele até a entrega e a fatura.

**Onde fica:** módulo **Transporte**, *Faturamento › Conhecimentos de transporte (CT-e)*.

**Situações do CT-e:** **Digitado** (ainda pode ser alterado) › **Emitido** (com a chave de acesso) › **Em trânsito** (num manifesto ou com ocorrência de saída) › **Entregue**. Ou **Cancelado**.

**Como digitar:**
1. Clique em **Novo CT-e**. Confira a **Série** e a **Emissão**; o número é o próximo da série. O **CFOP** é opcional (4 dígitos começando por 5, 6 ou 7).
2. Para não digitar tudo, use a **Origem dos dados**:
   - **Cotação aprovada**: traz o tomador, a tabela, a carga e o frete;
   - **Ordem de coleta**: traz o remetente, o destinatário, a mercadoria, o veículo, o motorista e, se a coleta tem rota, a origem e o destino;
   - **Viagem**: liga o CT-e à viagem do Frota.
3. Confira as **Partes**: **Remetente**, **Destinatário** e **Tomador** (quem paga o frete; é ele que recebe a fatura).
4. No **Trecho**, escolha a UF e o município de **origem** e de **destino**.
5. Em **Carga**, informe o produto predominante, os volumes, o peso, os m³, o valor da mercadoria e as **chaves das NF-e** transportadas (uma por linha; o programa confere cada chave).
6. Em **Frete**, escolha a **Tabela** e clique em **Calcular pela tabela**. Você pode ajustar qualquer parte (frete-peso, ad valorem, GRIS, pedágio, taxas, alíquota de ICMS); o total da prestação é refeito na hora. A **Previsão de entrega** vem do prazo da tabela.
7. Clique em **Gravar**. O CT-e fica **Digitado**.

**Como registrar a emissão:**
1. Autorize o CT-e no emissor da SEFAZ que você usa.
2. Volte ao Vorticis, selecione o CT-e e preencha a **Chave de acesso** (44 dígitos) e o **Protocolo**.
3. Clique em **Registrar emissão**. O programa confere o dígito da chave, se ela é de CT-e (modelo 57), se é do CNPJ da transportadora e se a série e o número são os deste CT-e. Depois de emitido, o CT-e não pode mais ser alterado.

**Outras ações:**
- **Imprimir minuta (PDF)**: um resumo do CT-e para acompanhar a carga, com campo de recebimento. Não substitui o DACTE autorizado.
- **Cancelar CT-e**: para o digitado ou o emitido que ainda não está num manifesto aberto nem numa fatura. Se ele já foi autorizado, cancele também no emissor da SEFAZ.
- **Excluir**: só o digitado. A cotação usada volta a ficar aprovada.

### Anexar as NF-e pelo XML (painel das notas)

Em vez de digitar as chaves, peso e valores, anexe os XML das notas fiscais que vão na carga. Use a aba **Notas transportadas (XML)**, ao lado da aba **CT-e**.

1. Clique em **Novo CT-e** (ou selecione um CT-e **digitado**).
2. Na aba **Notas transportadas (XML)**, clique em **Anexar XML das NF-e...** e escolha os arquivos. Pode escolher vários de uma vez; para pegar a pasta inteira, use **Ctrl+A**.
3. O programa lê cada nota e:
   - soma os **volumes**, o **peso** (o peso bruto da nota; sem ele, o líquido) e o **valor da mercadoria** na aba *CT-e*;
   - junta as **chaves das NF-e** no campo de chaves;
   - preenche o **produto predominante** (o de maior valor nas notas), se estiver vazio;
   - no CT-e novo, escolhe o **remetente** (emitente da nota) e o **destinatário** pelo CNPJ, se estiverem cadastrados em Clientes/Fornecedores. O **tomador** passa a ser o remetente; troque se o frete for pago pelo destinatário;
   - preenche a **origem** e o **destino** pelos municípios do emitente e do destinatário.
4. Confira o **painel**: quantidade de notas, volumes, peso, valor das notas, quantos remetentes e destinatários diferentes, peso bruto e líquido. Depois de calcular o frete, aparece também quanto o frete representa do valor das notas (%).
5. Para tirar uma nota, selecione-a e clique em **Retirar nota**: os totais são refeitos.
6. Clique em **Gravar** (aba *CT-e*). As notas ficam guardadas com o CT-e.

**Regras:**
- Arquivos que não são NF-e, com chave inválida ou repetidos aparecem em vermelho e ficam de fora.
- Uma NF-e só pode estar em um CT-e ativo; num CT-e cancelado, ela fica livre para outro.
- Depois de **emitido**, as notas do CT-e não mudam mais.
- Se o remetente ou o destinatário não estiverem cadastrados, a mensagem avisa o nome: cadastre em *Cadastros › Clientes* (pode buscar os dados na Receita pelo CNPJ) e escolha no CT-e.

## Manifestos de carga (MDF-e)

**Para que serve:** reunir os CT-e que seguem no mesmo veículo, com o motorista e o percurso, e controlar a abertura e o encerramento da viagem.

**Onde fica:** módulo **Transporte**, *Faturamento › Manifestos de carga (MDF-e)*.

**Como usar:**
1. Clique em **Novo manifesto**. Confira a **Série** e a **Emissão** e escolha a **UF de início**, a **UF de fim** e, se passar por outros estados, as **UFs do percurso** (por exemplo, "MG, BA").
2. Escolha o **Veículo**, o **Motorista** e, se quiser, a **Viagem**. A **Chave do MDF-e** (modelo 58) é opcional: informe depois de autorizar no emissor.
3. Em **CT-e do manifesto** aparecem os CT-e emitidos que ainda não estão em outro manifesto aberto. Marque os que vão no veículo.
4. Clique em **Gravar**. Os CT-e marcados passam a **Em trânsito**. Enquanto o manifesto está aberto, você pode tirar ou pôr CT-e e gravar de novo; os retirados (sem ocorrência registrada) voltam a **Emitido**.
5. Ao fim da viagem, escolha a data e clique em **Encerrar**.
6. **Imprimir (PDF)** gera a relação da carga com os totais de peso, valor e frete. **Cancelar** vale só para o manifesto aberto.

## Entregas e ocorrências

**Para que serve:** acompanhar cada CT-e até a entrega e guardar o comprovante (quem recebeu, quando).

**Onde fica:** módulo **Transporte**, *Operação › Entregas e ocorrências*.

**Como usar:**
1. A lista mostra os CT-e emitidos e em trânsito, com a **previsão** e o **prazo**. Os atrasados aparecem primeiro.
2. Selecione um CT-e: à direita aparecem os dados e as ocorrências já registradas.
3. Em **Nova ocorrência**, escolha o **Tipo**, a **data e hora** e escreva a descrição. Clique em **Registrar ocorrência**.
   - **Saída do veículo**, **Chegada na filial/destino** e **Saiu para entrega** põem o CT-e **em trânsito**;
   - **Tentativa sem sucesso**, **Avaria** e **Devolução** ficam registradas para consulta;
   - **Entregue** exige o **Recebedor** (nome e documento) e fecha o CT-e como **Entregue**.

A ocorrência não pode ser antes da emissão do CT-e nem no futuro. CT-e entregue ou cancelado não recebe mais ocorrências.

## Roteirizador

**Para que serve:** montar o roteiro de uma viagem com várias cidades, achar a **ordem mais curta**, ajustar as distâncias, calcular o **custo aproximado** e ver tudo num **mapa**.

**Onde fica:** módulo **Transporte**, *Operação › Roteirizador*.

**Como usar:**
1. Escolha a UF e o município e clique em **Incluir parada**. A primeira parada é a **origem**.
   - Com **A última parada é o destino final** marcado, as próximas entram antes do destino.
   - Para partir de uma rota já cadastrada, escolha-a em **Rota cadastrada**.
2. Clique em **Otimizar ordem (mais curta)**. O programa reordena as paradas pelo menor caminho e mostra quantos km economizou.
   - A origem sempre fica no início; o destino final também, se marcado.
   - Marque **Voltar à origem no fim** quando o caminhão retorna à base: a volta entra no cálculo.
3. Para mudar a ordem à mão, selecione a parada e use **▲ Subir** e **▼ Descer**. **Retirar** tira a parada e **Limpar** recomeça.
4. Confira os **Trechos**. O km de cada trecho vem estimado: a linha reta entre as cidades vezes o fator rodoviário dos *Parâmetros*.
   - **Distâncias pela estrada** (precisa de internet) consulta o km real de cada trecho pela estrada e desenha o caminho no mapa.
   - Para **customizar**, digite o km do trecho: o valor digitado vale sobre o estimado e o da estrada. Abaixo de cada trecho aparecem os valores de referência.
5. Em **Custo**, informe o **Custo por km**, os **Pedágios** e a **Velocidade média**.
   - Para o custo por km, escolha o **Veículo** e clique em **Sugerir custo/km**. O programa soma os abastecimentos e as ordens de serviço dos últimos 6 meses e divide pelo km rodado.
6. No alto aparecem o total de km, o tempo de direção e o **Custo aproximado** (km × custo por km + pedágios).

**O mapa:**
- Desenhado pelo próprio programa, funciona sem internet. As cidades da região aparecem como pontos claros e as paradas numeradas: a origem em verde, as intermediárias em azul e o destino final em vermelho.
- O caminho aparece em linha reta entre as paradas ou, depois de **Distâncias pela estrada**, pelo traçado da estrada.
- **Abrir no Google Maps** abre o roteiro no navegador, com as paradas na ordem, para ver o trajeto pelas estradas e o trânsito.

**Gravar como rota:** dê um nome e clique em **Gravar como rota**. O roteiro vai para *Cadastros › Rotas*, com as paradas na ordem, o km total, o tempo e os pedágios, e fica disponível nas ordens de coleta e nas viagens. Com uma rota cadastrada escolhida, ela é atualizada.

**Dicas:**
- Com até 8 paradas no meio, a ordem encontrada é a mais curta possível; com mais, o programa usa um método rápido que chega muito perto disso.
- A ordem é otimizada pelas distâncias em linha reta. Se uma estrada obriga a desvios, ajuste a ordem à mão e confira com **Distâncias pela estrada**.

## CIOT (operações de transporte)

**Para que serve:** registrar cada frete que a transportadora **subcontrata** de um transportador autônomo (**TAC**, pessoa física) ou de uma empresa equiparada (**ETC** com até 3 veículos). A lei (Res. ANTT 5.862/2019) exige que essa operação tenha um **CIOT** (Código Identificador da Operação de Transporte), gerado por uma **IPEF** (Repom, Pamcard, e-Frete, Target e outras), que também paga o frete ao contratado. O CIOT vai no MDF-e da viagem.

**Onde fica:** módulo **Transporte**, *Operação › CIOT – operações de transporte*.

**Situações:** **Digitado** (sem CIOT, pode ser alterado) › **Declarado** (com o CIOT da IPEF) › **Encerrado** (viagem concluída). Ou **Cancelado**.

**Como lançar:**
1. Clique em **Nova operação** e escolha o **Tipo**:
   - **Padrão**: uma viagem;
   - **TAC agregado**: o autônomo trabalha para você por um período.
2. Em **TAC ou empresa**, escolha o contratado. Cadastre-o antes em *Cadastros › Fornecedores*: com **CPF** ele é TAC; com **CNPJ**, ETC.
3. Informe o **RNTRC** do contratado (8 ou 9 dígitos), a **Placa** do veículo dele, o nome e o CPF do **Motorista**.
4. Escolha a **Viagem** (opcional), a **Origem** e o **Destino**, o **Início** e o **Fim**, o **Produto**, o **NCM** e o **Peso**.
5. Informe o **Valor do frete** combinado, o **Vale-pedágio** (pago à parte, não entra no frete), o **Adiantamento** e, para o TAC, os **Dependentes** do IR.
6. Clique em **Calcular retenções** para ver o resumo: frete, retenções, líquido, adiantamento e **SALDO** a pagar.
7. Marque os **CT-e da operação** (os conhecimentos das cargas que o contratado vai levar).
8. **Gravar**. A operação recebe um número e fica **Digitada**.

**Retenções do TAC** (calculadas sozinhas, só para contratado com CPF):
- **INSS**: 11% sobre 20% do frete, até o teto do INSS do mês;
- **SEST/SENAT**: 2,5% sobre os mesmos 20%;
- **IRRF**: tabela progressiva mensal sobre 10% do frete, menos o INSS e os dependentes, com a redução da Lei 15.270/2025.

Os fretes do mesmo TAC no mesmo mês são somados, porque o teto do INSS e a tabela do IR são mensais: o segundo frete do mês retém a diferença. As alíquotas e tabelas ficam nos parâmetros legais (TAC_RETENCOES e as tabelas de INSS e IRRF), não no programa. Contratado com CNPJ (ETC) não tem retenção.

**Depois de gravar:**
1. Registre a operação no site ou aplicativo da **IPEF** contratada. Ela gera o CIOT e faz o pagamento do adiantamento e do saldo ao contratado.
2. Volte ao Vorticis, informe a **IPEF**, o **CIOT** (12 dígitos) e o **Código verificador** e clique em **Declarar CIOT**. A operação passa a **Declarada** e não muda mais.
3. Informe o CIOT também no MDF-e da viagem (no emissor da SEFAZ).
4. Ao fim da viagem, encerre a operação na IPEF e clique em **Encerrar**.

**Outras ações:**
- **Contrato de frete (PDF)**: contrato e recibo com as partes, o trecho, a carga, os valores, as retenções, o adiantamento e o saldo, com campos de assinatura.
- **Cancelar operação**: para a digitada ou a declarada (cancele também na IPEF).
- **Excluir**: só a digitada.

**Regras:**
- Um CT-e só pode estar em uma operação ativa.
- O adiantamento não pode passar do valor líquido.
- O CIOT é obrigatório para declarar e encerrar.

**Importante:** o Vorticis não se comunica com as IPEFs. A geração do CIOT e o pagamento são feitos no sistema da IPEF; aqui ficam o controle, os cálculos e os documentos. O piso mínimo de frete da ANTT não é conferido: consulte a tabela da ANTT ao combinar o valor.

## Faturas de frete

**Para que serve:** cobrar do cliente vários fretes de uma vez, numa fatura com vencimento, e lançar a conta a receber no Financeiro.

**Onde fica:** módulo **Transporte**, *Faturamento › Faturas de frete*.

**Como usar:**
1. Em **Nova fatura**, escolha o **Cliente (tomador)**. Aparecem os CT-e emitidos dele que ainda não foram faturados, todos marcados. Desmarque os que ficam para depois; o total é mostrado embaixo.
2. Informe a **Emissão** e o **Vencimento** e, se quiser, uma observação.
3. Clique em **Gerar fatura**. O número é sequencial e a **conta a receber** é criada em *Financeiro › Contas a receber* (documento "FAT-" e o número da fatura), no valor total e no vencimento informado.
4. **Imprimir fatura (PDF)** gera a fatura com a relação dos CT-e para enviar ao cliente.
5. **Cancelar fatura** apaga a conta a receber e libera os CT-e para outra fatura. Só vale para a fatura **sem recebimento**: se já houve baixa, estorne-a antes em *Financeiro › Contas a receber*.

A lista mostra o **saldo a receber** de cada fatura, vindo da conta a receber, e a situação: *A receber*, *Vencida*, *Recebida* ou *Cancelada*.

## Relatórios do TMS

**Onde fica:** *Relatórios* do módulo **Transporte** (junto com os relatórios de faturas, vendas e recebimentos).

| Relatório | O que mostra |
|---|---|
| **Fretes por cliente** | Por tomador: quantidade de CT-e, peso, valor da mercadoria, ICMS, frete e frete por kg. |
| **Fretes por trecho** | O mesmo, por origem e destino. |
| **Conhecimentos emitidos** | Todos os CT-e do período, com situação e fatura (os cancelados em destaque e fora do total). |
| **Prazo de entrega** | Previsão, data da entrega, recebedor e situação do prazo; atrasados primeiro e o percentual entregue no prazo. |
| **Faturas de frete** | Faturas do período com valor, saldo a receber e situação (vencidas em destaque). |

Escolha o **período** e clique em **Gerar**. Os relatórios de fretes consideram só os CT-e emitidos, em trânsito ou entregues (digitados e cancelados não entram). Como os demais relatórios, podem ser exportados para PDF, Excel ou CSV.
