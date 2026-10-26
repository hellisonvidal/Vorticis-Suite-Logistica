# 06 — ECF (Escrituração Contábil Fiscal — SPED)

Gerador do arquivo da **ECF** do ano-calendário, leiaute 12 (`0000.COD_VER = 0012`, válido para o ano-calendário 2025 e situações
especiais de 2026), já **assinado digitalmente** (mesmo formato e mesmo assinador da ECD), para o usuário só importar e validar no PVA da
ECF e transmitir. Escopo da primeira versão: **Lucro Real trimestral** e **Lucro Presumido trimestral**. Empresas do **Simples Nacional**
(e MEI) não entregam ECF: o assistente mostra a mensagem e não gera arquivo.

Fontes usadas:

- **Manual de Orientação do Leiaute 12 da ECF**, atualização de 20/05/2026 (anexo ao ADE Cofis nº 02/2026), baixado de sped.rfb.gov.br
  (pasta 1644, item 8003): campos, tamanhos, obrigatoriedade e exemplos de cada registro;
- **Tabelas Dinâmicas e Planos de Contas Referenciais — Leiaute 12** (atualização de 28/05/2026, item 8002): fórmulas e códigos de
  M300A/M350A (e-Lalur/e-Lacs), N500/N630/N650/N670, P200/P300/P400/P500, `PARTEB_PADRAO` e as árvores L100A/L300A. As tabelas M300A e M350A
  (linhas de entrada vigentes em 2025) estão em `TabelasEcfLalur.cs` e servem para validar o `codigo_ecf` do LALUR;
- `assinatura.jar` do **PVA da ECF 12.2.8 (Linux)**: contém a mesma biblioteca de assinatura do PVA da ECD (etiqueta `SBRCAAEPDR`),
  então o formato descrito em [05-ecd.md](05-ecd.md) vale para a ECF. Os descritores do PVA da ECF não puderam ser lidos (o instalador
  embute os arquivos de forma que não foi possível extraí-los); a estrutura dos registros vem do manual.

## Uso (API para a tela)

`VorticisApp.Ecf` (`Vorticis.Application.Sped.ServicoEcf`), permissão `SPED_ECF_GERAR` (migração 026; perfis padrão Contador e Administrador):

| Método | O que faz |
|---|---|
| `ValidarAsync(empresaId, ano, OpcoesEcf, usuarioId)` | `PreviaEcf`: pendências (bloqueantes e avisos), signatários do 0930, forma de tributação, nº de trimestres. `PodeGerar` |
| `GerarAsync(empresaId, ano, OpcoesEcf, usuarioId, assinantes?)` | `ArquivoEcf` (nome, bytes ISO-8859-1, hash SHA-1, linhas, contagem por registro, assinaturas, avisos). Falha com as pendências bloqueantes. Sem `assinantes`, sai sem assinatura |
| `Assinar(byte[] arquivo, assinantes)` | Acrescenta assinaturas a um arquivo já gerado (ex.: o responsável assina depois do contador) |
| `LerAssinaturas(byte[])` | Assinaturas anexadas, conferidas contra o conteúdo |
| `ListarGeradasAsync(empresaId, usuarioId)` | Histórico (`contabil.ecf_gerada`) |

O assistente (Auditoria › Gerar ECF (SPED)) tem os mesmos cinco passos da ECD: ano-calendário e opções, pré-requisitos (com atalho
para as telas do Contábil; as pendências do módulo Fiscal não têm atalho porque o Fiscal é outro aplicativo), validação prévia,
assinaturas (A1, A3 ou certificado padrão do contador e do responsável) e gerar/salvar. `OpcoesEcf`: retificadora e recibo anterior,
informar o hash da ECD (C040), e-CNPJ como responsável e qualificação do sócio.

## De onde vêm os dados

| Dado | Fonte |
|---|---|
| Regime do ano | `core.empresa_regime` (vigência que cobre o ano); sem regime cadastrado, deduzido das apurações |
| Cadastro (0000, 0030) | `core.empresa`: CNPJ, razão social, natureza jurídica, CNAE, endereço completo, telefone, e-mail |
| Plano, saldos, lançamentos | os mesmos da ECD (`ConsultasEcd`): lançamentos efetivados do exercício, inclusive encerramento |
| Plano referencial | `contabil.plano_referencial` da tabela do exercício (`cod_plan_ref`), hierarquia incluída; vínculo por conta em `mapeamento_referencial` |
| IRPJ/CSLL trimestrais | `fiscal.apuracao_lucro` (colunas) e, no Presumido, a **memória de cálculo** (JSONB) para separar a receita por percentual de presunção |
| e-Lalur/e-Lacs Parte A | `fiscal.lalur_lancamento` (`codigo_ecf` = código da tabela M300A/M350A; `conta_id` = conta contábil relacionada) |
| Parte B | `fiscal.lalur_parte_b` (saldo inicial, prejuízo gerado, compensação) |
| Sócios (Y600) | `core.socio` |
| Hash da ECD (C040) | `contabil.ecd_gerada` (opcional) |

## Registros gerados

| Bloco | Registros | Observações |
|---|---|---|
| 0 | 0000, 0001, 0010, 0020, 0030, 0930, 0990 | `FORMA_TRIB` 1 (Real) ou 5 (Presumido), `FORMA_APUR = T`, `COD_QUALIF_PJ = 01`, `FORMA_TRIB_PER` = `RRRR`/`PPPP` (trimestres fora do período = `0`). Presumido: `TIP_ESC_PRE = C` e `IND_REC_RECEITA` (2 competência; 1 se `regime_caixa`). 0020: `IND_ALIQ_CSLL = 1` (9%), `IND_QTE_SCP = 0` e todos os demais indicadores `N`. 0930: contador (900) e responsável (205/001...) com os códigos da tabela de qualificação do assinante |
| C | C001 (+ C040 opcional) | Sem dados por padrão: o PVA **recupera a ECD** (Escrituração › Recuperar ECD). Com `InformarEcd` e uma ECD gerada no ano, sai o C040 com o hash |
| E | E001, E990 | Sem dados (saldos da ECF anterior não são importados; o saldo inicial da Parte B vai no M010) |
| J | J001, J050, J051, J990 | Plano de contas do contribuinte (contas com saldo/movimento + superiores + contas do LALUR) e vínculo referencial das analíticas 01–04 |
| K | K001, K030, K155, K156, K355, K356, K990 | Um K030 por trimestre (`T01`..`T04`, recortados pelo exercício). K155 = contas patrimoniais (saldo inicial, débitos, créditos, final, **com** os lançamentos de encerramento); K355 = resultado do período **sem** o encerramento; K156/K356 = mapeamento 1:1 à conta referencial |
| L | L001, L030, L100, L300, L990 | **Lucro Real.** Balanço e DRE referenciais por trimestre (soma das contas mapeadas e das superiores, em pré-ordem) |
| M | M001, M010, M030, M300, M305, M310, M350, M355, M360, M410, M500, M990 | **Lucro Real.** Fórmulas das tabelas M300A/M350A (ver abaixo); Parte B com duas contas (`PF_IRPJ` = 1000 e `BC_CSLL` = 1003 da tabela padrão da RFB) |
| N | N001, N030, N500, N630, N650, N670, N990 | **Lucro Real trimestral.** (N620/N660 são o cálculo por estimativa mensal, só para apuração anual; ver limitações) |
| P | P001, P030, P100, P150, P200, P300, P400, P500, P990 | **Lucro Presumido.** P100/P150 = balanço e DRE referenciais; P200/P300 = base e cálculo do IRPJ; P400/P500 = base e cálculo da CSLL |
| Q, S, T, U, V, W, X | xxx001 (`IND_DAD = 1`), xxx990 | Blocos sem dados (livro caixa, TEF/SAF, arbitrado, imunes, DEREX, país-a-país, econômicas) |
| Y | Y001, Y600, Y672 (Presumido), Y990 | Sócios com PF/PJ (qualificação 02/04, país 105) e percentuais; Y672 só com receitas (`VL_RECEITAS`) e total do ativo |
| 9 | 9001, 9900, 9990, 9999 | `9900.VERSAO` e `ID_TAB_DIN` ficam vazios (opcionais; o PVA preenche) |

Todos os contadores (`xxx990`, `9990`, `9999`) contam as linhas do próprio bloco, inclusive o próprio registro. A formatação é a da ECD
(`FormatoSped`); campos numéricos com sinal (NS) usam `-` na frente.

### Lucro Real — como M e N são calculados

As linhas "calculadas" das tabelas dinâmicas são escritas com os valores que o PVA recalcula, para o arquivo ser consistente por construção:

- **M300/2** = resultado antes do IRPJ = −(L300 `3.01` + L300 `3.02.01.01.01.01`) — a CSLL provisionada é despesa no lucro de partida e
  volta como adição automática na linha **9**. **M350/2** = resultado da DRE referencial `3.01`. Por isso o plano referencial do exercício
  precisa ter as contas `3.01` (resultado antes do IRPJ e da CSLL) e as provisões `3.02.01.01.01.01` (CSLL) e `.02` (IRPJ), como na tabela L300A.
- **93** = adições (linhas de entrada tipo adição + linha 9); **168** = exclusões; **169** = 2 + 93 − 168; **171** = 169; **173** =
  compensação de prejuízos fiscais de períodos anteriores (limitada ao lucro e ao saldo da Parte B; M305 com a conta `PF_IRPJ`);
  **175** = lucro real. M350 espelha: 93, 168, 169, 171, 173 (base negativa, M355), 175.
- Linhas de entrada vêm de `fiscal.lalur_lancamento` agrupadas por `codigo_ecf`: com `conta_id` em todas as parcelas, `IND_RELACAO = 2` e um
  M310/M360 por conta (D/C pela conversão de sinais do manual: adição = resultado a débito/patrimonial a crédito; exclusão, o inverso);
  senão `IND_RELACAO = 4`. Lançamento `AMBOS` vai ao M300 e ao M350.
- **N500/1** = M300/175; **N630**: 1 base, 3 (15%), 4 adicional (10% sobre o que passa de R$ 20.000 × meses), 20 IRRF (`irpj_deducoes`),
  26 a pagar. **N650/1** = M350/175; **N670**: 1 base, 2 CSLL (9%), 4 total, 17 CSLL retida (`csll_deducoes`), 21 a pagar.
- **Prejuízo do período** (lucro real negativo): M410 `PF` (IRPJ) e `BC` (CSLL) na conta da Parte B; **M500** controla o saldo de cada
  conta por trimestre (inicial, Parte A = compensações, Parte B = prejuízos, final), com a convenção "prejuízo a compensar é devedor".
- O saldo inicial da Parte B (M010) = `SALDO_INICIAL` + movimentos de anos anteriores em `lalur_parte_b`.

### Lucro Presumido — P200/P300/P400/P500

P200: receita por percentual (códigos 2/4/6/8/9 = 1,6/8/16/32/38,4%), 10 = resultado dos percentuais, 20 = outras receitas (ganhos de capital e
rendimentos, a 100%), 26 = base. P300: 1 base, 3 (15%), 4 adicional, 10 IRRF, 15 a pagar. P400: 2/4/5 = receita a 12/32/38,4%, 6, 16, 21 base.
P500: 1 base, 2 CSLL (9%), 4 total, 11 CSLL retida, 13 a pagar. A separação da receita por percentual é extraída das linhas "receita × %"
da **memória de cálculo** gravada pelo módulo Fiscal; se não for possível identificá-la, a pendência `RECEITA_SEM_PERCENTUAL` bloqueia.

## Validação prévia (pendências)

`ValidadorEcf` (Domain) + conferências no `ServicoEcf`. **Bloqueantes** impedem gerar:

- cadastro: CNPJ, UF, município IBGE, natureza jurídica, CNAE, endereço/número/bairro/CEP, e-mail; recibo anterior na retificadora;
- regime/apurações: sem apuração do regime no ano (`SEM_APURACAO`), trimestre sem apuração (`APURACAO_AUSENTE`), apurações de outro regime
  no mesmo ano (`REGIME_MISTO`);
- referencial: exercício sem plano referencial, conta com saldo/movimento sem vínculo (`SEM_REFERENCIAL`), vínculo para conta inexistente
  na tabela importada;
- balanço: saldos iniciais ou finais das contas patrimoniais que não fecham (exercício não encerrado);
- LALUR (Real): lançamento sem `codigo_ecf`, código inexistente nas tabelas M300A/M350A ou de tipo oposto (adição × exclusão), compensação
  maior que o saldo da Parte B, DRE referencial sem a conta `3.01`;
- Presumido: receita sem percentual identificável ou fora da tabela;
- signatários: sem contador (CPF, CRC, e-mail, telefone), sem responsável (ou mais de um), documento ou qualificação inválidos, duplicados.

**Avisos**: divergência entre os cálculos da ECF e a apuração do Fiscal (lucro contábil, base e imposto do IRPJ e da CSLL), apuração com
período diferente do trimestre, sem ECD gerada (bloco C sem dados), sócios sem documento/exterior/capital ≠ 100%, parâmetros do 0020 e
remuneração de sócios gerados com valores padrão, competências abertas.

## Decisões e limitações

- **Só apuração trimestral.** O módulo Fiscal só apura IRPJ/CSLL por trimestre; o Lucro Real **anual com estimativas mensais** (N620/N660,
  `A00`/`A01..A12`, balancetes de suspensão/redução) e as formas mistas (Presumido/Real, Arbitrado) não são gerados.
- **Centros de custo** são somados (o mapeamento referencial é por conta): `COD_CCUS` sai vazio e não há J100.
- **Bloco C**: sem dados por padrão, porque o C040 é "preenchido pelo sistema ao recuperar a ECD" (com C050/C051/C155...). Com `InformarEcd`
  sai só o C040 (hash da ECD gerada no ano); o PVA ainda pede para recuperar a ECD.
- **Bloco E**: sem dados; o saldo da Parte B do ano anterior entra pelo M010.
- **Deduções do IRPJ** (`irpj_deducoes`) são informadas integralmente como IRRF (N630/20, P300/10); incentivos (PAT, Lei Rouanet...) não têm linha própria.
- **Y540** (receitas por CNAE) não existe no leiaute 12 do manual; Y600 sai com remuneração, lucros/dividendos e IRRF **zerados**; Y612 é
  de imunes/isentas; não há Y570 (retenções), Y520/Y590 (exterior), Y620 (equivalência) nem X/V/W.
- **Parâmetros do 0020** saem todos `N` (sem operações no exterior, consórcios, incentivos regionais etc.); conferir antes de transmitir.
- **Tabelas dinâmicas**: `9900.VERSAO`/`ID_TAB_DIN` ficam vazios; a vigência dos códigos M300A/M350A considera 2025.
- **Não verificado no PVA**: neste ambiente não foi possível executar o PVA da ECF para importar/validar um arquivo (nem ler seus
  descritores). A estrutura segue o manual e os exemplos de preenchimento dele. Pontos a confirmar na primeira importação real: a ordem e
  quantidade de campos de 0010/0020, a aceitação das linhas calculadas (CNA) no arquivo, o tratamento do bloco C sem dados e a exigência de
  L210/L200 e Q100 em casos específicos.
- A assinatura segue o formato do PVA (ver [05-ecd.md](05-ecd.md)); a ECF assina o hash SHA-1 do arquivo sem assinaturas, com etiqueta
  `SBRCAAEPDR` + ordem + código de qualificação do 0930.

## Certificados e procurador

Valem para a ECF as mesmas regras da ECD (ver [05-ecd.md](05-ecd.md#contador-usa-o-certificado-da-empresa-assinatura-como-procurador)): o perfil
Contador usa os certificados cadastrados da empresa para assinar (cadastro continua do administrador) e a opção **Assinar como procurador**
coloca no 0930 o contador procurador com vigência na data da geração e abrangência ECF, com `IDENT_QUALIF = 309` (Procurador), conferindo o CPF do certificado. A
transmissão por procurador exige a procuração eletrônica no e-CAC (aviso `PROCURACAO_ECAC` e lembrete no passo final).

## Como validar no PVA

1. Gerar no Vorticis (Auditoria › Gerar ECF (SPED)), resolvendo as pendências bloqueantes.
2. No PVA da ECF: *Escrituração › Importar* o `.txt` (as assinaturas anexadas são lidas) e *Escrituração › Recuperar ECD* se o bloco C saiu sem dados.
3. *Escrituração › Validar* e conferir as advertências (os nomes das regras do manual aparecem no PVA).
4. Transmitir (*Escrituração › Transmitir*); o hash gerado também fica em `contabil.ecf_gerada`.
