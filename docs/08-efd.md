# EFD ICMS/IPI (SPED Fiscal) e EFD-Contribuições

Menu **Fiscal › Obrigações › SPED**. Cada arquivo tem um assistente de cinco passos (empresa e período, pré-requisitos, validação prévia,
assinatura, gerar e salvar) e uma aba com o histórico dos arquivos gerados (`fiscal.efd_gerada`). Permissão: `FISCAL_SPED_GERAR`
(Administrador e Contador; migração 047). As escolhas que se repetem a cada mês (perfil, atividade, códigos de receita) ficam em
`fiscal.efd_configuracao` (migração 048). Código: `src/Vorticis.Fiscal/Sped` (geradores e validação puros; `ServicoEfd`; `RepositorioEfd`),
telas em `src/Vorticis.Fiscal.Desktop` (`EfdAssistenteViewModels.cs`, `EfdAssistenteView.axaml`).

Fontes: Guia Prático da EFD ICMS/IPI 3.2.x (vigente em 2026; leiaute **020**, NT 2025.001, `COD_VER` = 020 para 2026 em diante, 019 para 2025) e
Guia Prático da EFD-Contribuições (leiaute **006**, que o PVA mantém desde 04/2021). O `COD_VER` pode ser trocado em `OpcoesEfdIcms.CodigoVersao`
e `OpcoesEfdContribuicoes.CodigoVersao` se a Receita publicar um novo leiaute.

Formato (igual ao da ECD/ECF, `FormatoSped`): campos separados por `|`, CR LF, ISO-8859-1, datas `ddmmaaaa`, valores com vírgula decimal.
Os documentos vêm de `fiscal.documento`/`documento_item` pela **data de entrada/saída** do mês; o CFOP é o de escrituração do item
(`cfop_escrituracao`, senão o da nota).

## EFD ICMS/IPI — registros gerados

| Bloco | Registros | Observações |
|---|---|---|
| 0 | 0000, 0001, 0005, 0100, 0150, 0190, 0200, 0990 | 0150 dos participantes das notas; 0190 das unidades; 0200 dos itens (código = código sequencial do produto cadastrado, ou o `cProd` da nota). Contador: vínculo vigente em `core.empresa_contabilidade` |
| B | B001, B990 | só se a UF for DF (sem dados) |
| C | C001, C100, C170, C190, C990 | NF-e (55) e NFC-e (65). Cancelada (02) e denegada (04) saem só com os 9 primeiros campos do C100 |
| D, G, K | x001 (sem dados), x990 | |
| E | E001, E100, E110, E116, E200/E210/E250, E500/E510/E520, E990 | ver abaixo |
| H | H001, H005 (opcional), H990 | `InventarioZerado`: H005 com valor 0,00, motivo 01; senão sem dados |
| 1 | 1001, 1010, 1990 | indicadores todos `N` |
| 9 | 9001, 9900, 9990, 9999 | contagens recalculadas ao final |

**Decisões**
- **Perfil A/B/C**: no A, C170 sai em todas as notas autorizadas; no B e no C, só nas entradas. C190 sai sempre (por CST+origem, CFOP e alíquota).
- **E110**: débitos = ICMS dos C190 com CFOP 5/6/7; créditos = ICMS dos C190 com CFOP 1/2/3; saldo credor anterior informado no assistente.
  Ajustes da apuração (E111, em Lançamentos › SPED Fiscal): o 4º caractere do código (tabela 5.1.1) leva o valor ao campo do E110 — 0 `VL_TOT_AJ_DEBITOS`, 1 `VL_ESTORNOS_CRED`, 2 `VL_TOT_AJ_CREDITOS`, 3 `VL_ESTORNOS_DEB`, 4 `VL_TOT_DED` (limitado ao saldo devedor), 5 `DEB_ESP`. Créditos das contas de consumo (C590/D590) somam em `VL_TOT_CREDITOS`. `VL_ICMS_RECOLHER` + `DEB_ESP` > 0 gera o **E116** (`COD_OR` 000, vencimento no dia escolhido do mês seguinte, `COD_REC` informado).
- **ICMS-ST**: o retido nas saídas (C190 com `VL_ICMS_ST`) é agrupado pela UF do destinatário (E200/E210, `COD_OR` 002 no E250).
- **IPI** (E500/E510/E520): só quando a atividade é industrial/equiparado; E510 por CFOP e CST reduzido a 00/49/50/99.
- **Participantes sem cadastro**: o 0150 é montado com os dados da nota (aviso, sem código de município).
- Os totais do C100 vêm da soma dos itens (iguais aos do C190); o total do documento (`VL_DOC`) é o da nota.
- NFS-e não entra na EFD ICMS/IPI (aviso).

**Validação prévia** (bloqueantes impedem gerar): IE da empresa, endereço (CEP/logradouro), contador (CPF, CRC), chave de acesso de 44 dígitos com DV,
CFOP × operação (entrada 1/2/3; saída 5/6/7), nota sem itens, soma dos itens × total da nota, nota sem participante ou participante sem CPF/CNPJ,
código de receita quando há ICMS/ICMS-ST a recolher. Avisos: Simples Nacional, NFS-e fora, participantes sem cadastro/município, itens sem NCM, IPI sem atividade industrial.

## EFD-Contribuições — registros gerados

| Bloco | Registros | Observações |
|---|---|---|
| 0 | 0000, 0001, 0100, 0110, 0140, 0150, 0190, 0200, 0990 | 0110: não cumulativo (1/1/1) ou cumulativo (2, competência ou caixa) conforme a apuração salva |
| A | A001, A010, A100, A170, A990 | NFS-e prestadas e tomadas |
| C | C001, C010 (escrituração individualizada), C100, C170, C990 | NF-e/NFC-e; não gera o consolidado C180/C190 |
| D, F, I, P | x001 (sem dados), x990 | |
| M | M001, M100/M105, M200, M210, M500/M505, M600, M610, M990 | a partir de `fiscal.apuracao_pis_cofins` |
| 1 | 1001, 1990 (sem dados) | |
| 9 | 9001, 9900, 9990, 9999 | |

**Decisões**
- O CST de cada item segue a mesma regra da apuração (CFOP): saída que gera receita → CST 01 com a alíquota da apuração; entrada que gera crédito no
  regime não cumulativo → CST 50; demais → 49 (saída) ou 98 (entrada), valores zerados.
- **M200/M600** (e M210/M610): contribuição do período = débito da apuração; crédito descontado = menor valor entre o crédito e a contribuição;
  devida = contribuição − crédito; a recolher = devida − retenções (≥ 0). No cumulativo: M210 com `COD_CONT` 51, sem crédito. Confere com `pis_a_pagar`/`cofins_a_pagar`.
- **M100/M500**: um único crédito (código 101, origem 0) com base e natureza "01 – bens para revenda" (M105/M505).
- Competência mensal sem apuração salva é bloqueante. Empresas do Simples Nacional/MEI são dispensadas (bloqueante informativo).

## Limitações conhecidas

- Contas de energia, gás e água (C500/C590, 40 campos no leiaute 020) e de comunicação (D500/D590) recebidas; inventário cadastrado (H005/H010, com o 0200 dos produtos) na competência escolhida.
- Sem E112/E113/E115, DIFAL/FCP (E300), controle de crédito do CIAP (G), produção (K) e conhecimentos de transporte (D100).
- Sem C180/C190/C175 consolidados, devoluções (M220/M620), receitas não tributadas (M400/M800), controle de créditos de períodos anteriores (M110/1100),
  créditos de energia/aluguel/depreciação (F100/F120) e CPRB (P). Devoluções de venda e saldo credor geram aviso.
- Operações mistas (parte cumulativa, parte não cumulativa, COD_INC_TRIB 3) não são geradas.
- NFC-e (65) é escriturada no C100; a escrituração consolidada por equipamento (C400/C490) não existe.
- A unidade do 0200 é a da primeira nota em que o item aparece (não gera 0220 de conversão).

## Como validar no PVA

1. Salve o `.txt` do passo 5 (nomes `EFD_ICMS_IPI_<CNPJ>_<AAAAMM>.txt` e `EFD_CONTRIBUICOES_<CNPJ>_<AAAAMM>.txt`).
2. Baixe o PVA da respectiva escrituração no Portal do SPED (Receita Federal; a EFD ICMS/IPI também pelos portais da SEFAZ), Escrituração › **Importar**, escolha o arquivo e **Validar**.
3. Confira o resumo da apuração (E110/E116 ou M200/M600) com o mostrado no passo 3 e só então **Assinar e Transmitir**.
4. Erros de leiaute devolvidos pelo PVA indicam o registro e o campo; ajuste o cadastro (empresa, participante, produto) ou a nota e gere de novo (o histórico guarda o hash SHA-1 de cada geração).

## Assinatura

Reaproveita o `AssinadorSpedCms` da ECD/ECF: PKCS#7 (SHA-1 do arquivo até o `9999`) anexado depois do `9999`, precedido da etiqueta `SBRCAAEPDR` + ordem + qualificação (900).
Os testes verificam a assinatura criptograficamente. **Não foi confirmado** que o PVA da EFD ICMS/IPI/Contribuições aceite uma assinatura já gravada no arquivo importado
(o fluxo oficial assina no próprio PVA, na transmissão). Por isso o padrão do assistente é **não assinar**; se o PVA recusar a assinatura externa, gere sem assinar.
