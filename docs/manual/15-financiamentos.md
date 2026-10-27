# Financiamentos, consórcios e imobilizado

No Vorticis Suite Logística, módulo **Financeiro**: *Contas › Consórcios*, *Contas › Financiamentos* e o menu *Imobilizado*. (No Frota avulso, menu *Contratos*.)

Use estas telas para controlar a compra de caminhões, carretas e outros bens **financiados** (CDC, FINAME, leasing financeiro) ou **por consórcio**: as parcelas com juros e prazos, os pagamentos, o rateio das despesas por placa, o bem no imobilizado e, se quiser, a contabilização automática no Contábil, separando a dívida de **curto** e de **longo prazo**.

## Financiamentos

**Onde fica:** *Financeiro › Contas › Financiamentos*.

**Lançar um financiamento:**
1. Clique em **Novo** e, na aba **Contrato**, preencha:
   - **Nº do contrato** e **Descrição** (ex.: *Cavalo mecânico Scania R 450*);
   - **Banco/financeira** (cadastrado em *Cadastros › Fornecedores*);
   - **Data do contrato** e **Valor do bem**;
   - **Entrada** (o que foi pago à vista) e **IOF e tarifas financiados** (se entraram no valor financiado);
   - **Juros (% ao mês)** e o **Sistema**: **Price** (parcelas iguais) ou **SAC** (amortização igual e parcelas que diminuem);
   - quantidade de **Parcelas** e **1º vencimento**.
2. Clique em **Simular parcelas** para conferir a tabela com o contrato do banco (amortização, juros, valor da parcela e saldo devedor). Se o banco arredondou diferente, a diferença de centavos aparece só na última parcela.
3. Na aba **Rateio por placa**, escolha o veículo (ou os veículos) e o percentual de cada um (veja [Rateio por placa](#rateio-por-placa)).
4. Na aba **Contabilização**, se quiser contabilizar, marque **Contabilizar no Contábil** e informe as contas (veja [Contabilização](#contabilizacao-curto-e-longo-prazo)).
5. **Gravar**. As parcelas aparecem na lista de baixo.

**O valor financiado** é: valor do bem − entrada + IOF e tarifas financiados.

**Pagar uma parcela:** selecione a parcela na lista, confira **Pagar em** (data) e **Valor pago** e clique em **Pagar parcela**. Se pagou com atraso (juros/multa) ou com desconto, digite o valor realmente pago: a diferença vai para a despesa de juros. Para desfazer, selecione a parcela e clique em **Estornar pagamento** (o lançamento contábil também é estornado). Quando todas as parcelas são pagas, o contrato fica **Quitado**.

**Corrigir um contrato:** enquanto não há parcela paga nem lançamento contábil, você pode mudar valores, taxa, prazo e datas (as parcelas são refeitas). Depois disso só a descrição, o credor, as contas, o rateio e a situação podem mudar.

## Consórcios

**Onde fica:** *Financeiro › Contas › Consórcios*.

1. Clique em **Novo** e preencha o **Grupo/cota**, a **Descrição**, a **Administradora**, a **Data da adesão** e o valor da **Carta de crédito**.
2. Informe a **Taxa de administração (%)** e o **Fundo de reserva (%)**, ambos sobre a carta e para o plano inteiro, o **Seguro por mês (R$)**, a quantidade de **Parcelas** e o **1º vencimento**.
3. **Simular parcelas** e **Gravar**. Cada parcela tem o **fundo comum** (carta ÷ parcelas, na coluna *Amortização*), a **taxa de administração** (coluna *Juros / taxa adm.*), o **fundo de reserva** e o **seguro**.
4. Pague as parcelas como no financiamento.
5. **Contemplação:** quando a cota for contemplada (sorteio ou lance) e o bem for recebido, informe a data no fim da aba **Contrato** e clique em **Registrar contemplação**. A partir daí, o que falta pagar do fundo comum é uma dívida (curto e longo prazo) e o bem pode entrar no imobilizado.

## Rateio por placa

Na aba **Rateio por placa**, divida as despesas do contrato entre os veículos: **juros**, **IOF e tarifas**, **taxa de administração** e **seguro**.

- Clique em **Incluir placa**, escolha o veículo e digite o percentual. O total precisa dar **100%**. Para tirar uma placa, use **Retirar**.
- Um financiamento de um caminhão só: uma placa com 100%. Um consórcio de um conjunto cavalo + carreta: por exemplo 70% e 30%.
- Na contabilização, cada parte da despesa vai ao **centro de custo da frota** do veículo (*Cadastros › Frotas*, campo centro de custo, ou o centro de custo ligado à frota no Contábil). Assim o custo financeiro aparece no resultado de cada frota. Veículo sem frota: a despesa entra sem centro de custo.
- Sem nenhuma placa, a despesa é lançada inteira, sem centro de custo.

## Contabilização (curto e longo prazo)

A contabilização é **opcional**: marque **Contabilizar no Contábil** na aba **Contabilização** e informe as contas (código ou reduzido do plano de contas):

| Conta | Para que serve | Exemplo |
|---|---|---|
| **Conta do bem** | Imobilizado (veículos). Se o contrato estiver ligado a um bem do imobilizado, vale a conta do bem. | 1.2.03.003 Veículos |
| **Pagamento (banco/caixa)** | De onde sai a entrada e o pagamento das parcelas. | 1.1.01.002 Bancos |
| **Curto prazo (circulante)** | Parcelas que vencem nos próximos 12 meses. | 2.1.05.001 Financiamentos |
| **Longo prazo (não circulante)** | Parcelas que vencem depois de 12 meses. | 2.2.01.001 Financiamentos LP |
| **Despesa (juros/taxas)** | Juros, IOF e tarifas, taxa de administração e seguro. | 4.3.01.001 Juros pagos |
| **Consórcio (ativo)** | Só no consórcio: o que foi pago antes da contemplação e o fundo de reserva. | Consórcios a contemplar |

**Regra do curto e do longo prazo:** pela Lei 6.404/76 (art. 180), a dívida que vence até o fim do exercício seguinte é **passivo circulante** (curto prazo) e o resto é **não circulante** (longo prazo). O sistema considera curto prazo as parcelas que vencem **até 12 meses depois da data-base** (as atrasadas também); no balanço de 31/12, isso é o exercício seguinte.

**Lançamentos do financiamento:**
1. **Contratação**: clique em **Contabilizar contratação** (aba *Contabilização*), antes de pagar a primeira parcela.
   - D Conta do bem (valor do bem);
   - D Despesa (IOF e tarifas, rateados por placa);
   - C Pagamento (entrada);
   - C Curto prazo e C Longo prazo (o valor financiado: a amortização das parcelas dos próximos 12 meses e a das demais).
   
   **Desfazer** estorna esse lançamento, se ainda não houver outros.
2. **Cada parcela paga**, no momento do pagamento:
   - D Curto prazo (amortização);
   - D Despesa (juros e a diferença de atraso/desconto, rateados por placa);
   - C Pagamento (valor pago).
3. **Reclassificação**: todo fim de mês (ou pelo menos no balanço), informe a **data-base** e clique em **Reclassificar curto/longo prazo**. As parcelas que passaram a vencer nos próximos 12 meses saem do longo para o curto prazo (D Longo prazo, C Curto prazo). O sistema lança só a diferença; se não houver nada a mudar, avisa.

**Lançamentos do consórcio:**
1. **Antes da contemplação**, cada parcela paga:
   - D Consórcio/ativo (fundo comum e fundo de reserva);
   - D Despesa (taxa de administração e seguro, rateados por placa);
   - C Pagamento.
   
   Não há lançamento na adesão.
2. **Contemplação** (*Registrar contemplação*):
   - D Conta do bem (carta de crédito);
   - C Consórcio/ativo (fundo comum já pago);
   - C Curto prazo e C Longo prazo (fundo comum das parcelas que faltam).
3. **Depois da contemplação**, cada parcela paga:
   - D Curto prazo (fundo comum);
   - D Consórcio/ativo (fundo de reserva, que é devolvido no fim do grupo);
   - D Despesa (taxa de administração e seguro);
   - C Pagamento.
   
   A **Reclassificação** funciona como no financiamento.

A linha em negrito da aba mostra os saldos já contabilizados (curto prazo, longo prazo e ativo do consórcio); compare com o balancete. Os lançamentos ficam com a origem **Frota** e podem ser consultados em *Contabilidade › Consultas › Lançamentos*.

**Dicas:**
- Ligue a contabilização **antes** do primeiro pagamento. Se já pagou parcelas sem contabilizar, estorne os pagamentos, ligue a contabilização e pague de novo.
- Os juros são reconhecidos como despesa no pagamento de cada parcela (parcelas mensais coincidem com o mês de competência).
- Não lance as parcelas também em *Contas a pagar*: o pagamento é registrado aqui (senão a despesa sai duas vezes).

## Bens financiados e consorciados

**Onde fica:** *Financeiro › Imobilizado › Bens financiados e consorciados*.

Mostra a correspondência entre os contratos e o imobilizado: para cada financiamento e consórcio, o **bem no imobilizado**, as **placas** do rateio, o valor do bem, as parcelas pagas, o **saldo a pagar** e a divisão em **curto** e **longo prazo** na **data-base** escolhida. No alto, o total da dívida de curto e de longo prazo dos contratos ativos (útil para conferir o balanço). Clique em **Atualizar** depois de mudar a data-base.

**Ligar o contrato ao imobilizado:** no contrato, aba **Imobilizado**, escolha o grupo (ex.: *Veículos de carga, caminhões e motocicletas*) e clique em **Gerar bem no imobilizado**. O sistema cadastra o bem com:
- a descrição do contrato;
- o valor do bem (ou da carta de crédito);
- a data do contrato (ou da contemplação) como aquisição e início de uso;
- a vida útil e as contas do grupo;
- a placa como plaqueta.

No consórcio, registre a contemplação antes. Depois, a depreciação é calculada pelo Contábil, em *Imobilizado › Cálculo e lançamento da depreciação*, que também está neste menu, junto com o *Cadastro de bens*.
