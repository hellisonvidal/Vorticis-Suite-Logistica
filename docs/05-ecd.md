# 05 — ECD (Escrituração Contábil Digital — SPED Contábil)

Gerador do arquivo da ECD do **Livro G (Diário Geral, sem escrituração auxiliar)**, leiaute 9 (`COD_VER_LC = 9.00`, válido a partir do
ano-calendário 2020; não houve alteração de leiaute em 2023, 2024 e 2025), já **assinado digitalmente**, para o usuário só importar e
validar no PVA (PGE) do Sped Contábil e transmitir.

Fontes usadas:

- **Manual de Orientação do Leiaute 9 da ECD**, atualização de maio/2026 (anexo ao ADE Cofis nº 1/2026), baixado de
  sped.rfb.gov.br (pasta 1569, item 7990);
- **PVA/PGE do Sped Contábil 10.4.1 para Linux** (download da RFB), lido sem instalar: o **descritor do leiaute 9**
  (`descritor/escrituracao/estrutura9004/v1/descritor.xml` — campos, tamanhos, obrigatoriedade e valores válidos de cada registro),
  as **consultas SQL das regras de validação** (`descritor/comum/queries/queries.xml`) e as classes de **assinatura**
  (`assinatura.jar`, `jsignnet.jar`, `ControleAssinarEscrituracao`), examinadas com `javap`.

## Uso (API para a tela)

`VorticisApp.Ecd` (`Vorticis.Application.Sped.ServicoEcd`), permissão `SPED_ECD_GERAR` (migração 025; perfis padrão Contador e Administrador):

| Método | O que faz |
|---|---|
| `ValidarAsync(empresaId, ano, OpcoesEcd, usuarioId)` | `PreviaEcd`: pendências (bloqueantes e avisos), signatários do J930, nº do livro sugerido, plano referencial. `PodeGerar` |
| `GerarAsync(empresaId, ano, OpcoesEcd, usuarioId, assinantes?)` | `ArquivoEcd` (nome, bytes ISO-8859-1, hash SHA-1, linhas, contagem por registro, assinaturas, avisos). Falha com as pendências bloqueantes. Sem `assinantes`, sai sem assinatura |
| `Assinar(byte[] arquivo, assinantes)` | Acrescenta assinaturas a um arquivo já gerado (ex.: o responsável assina depois do contador) |
| `LerAssinaturas(byte[])` | Assinaturas anexadas, conferidas contra o conteúdo |
| `ListarGeradasAsync(empresaId, usuarioId)` | Histórico (`contabil.ecd_gerada`) |

`assinantes` é uma lista de `CertificadoAssinante(X509Certificate2, codigoQualificacao)` — `"900"` para o contador e o código do
responsável no J930 (ex.: `"205"` administrador, `"001"` e-CNPJ). Para abrir os certificados: `CertificadosSped.AbrirPfx(pfx, senha)`
(A1; ou `ServicoCertificados.AbrirA1Async` do repositório) e `CertificadosSped.DoRepositorioWindows(impressao)` (A3 em token/cartão;
o driver pede o PIN). O serviço confere se o CPF/CNPJ do certificado (nome ICP-Brasil `NOME:CPF` ou extensões otherName
2.16.76.1.3.1/3.3) é o do signatário do J930 com aquela qualificação.

`OpcoesEcd`: número do livro (padrão: o da última ECD do ano ou o seguinte ao do ano anterior), natureza do livro (`DIÁRIO GERAL`),
nível de aglutinação (3), data de arquivamento dos atos (padrão: início das atividades), indicadores de situação (automáticos),
DMPL/DLPA, históricos padronizados, e-CNPJ como responsável, código de qualificação do sócio, certidão de regularidade do contador
(`NUM_SEQ_CRC` no formato `UF/AAAA/NÚMERO` e validade).

## Registros gerados

| Bloco | Registros | Origem dos dados |
|---|---|---|
| 0 | 0000, 0001, 0007, 0990 | `core.empresa` (CNPJ, nome, UF, município IBGE, IE, NIRE → `IND_NIRE`, `grande_porte` → `IND_GRANDE_PORTE`); `exercicio.cod_plan_ref` → `COD_PLAN_REF`. 0007 = IE na SEFAZ da UF, ou `00` (nenhuma) |
| I | I001, I010 (`G`, `9.00`), I030, I050, I051, I052, I075, I100, I150, I155, I200, I250, I350, I355, I990 | Plano de contas, mapeamento referencial do ano, centros de custo, partidas dos lançamentos efetivados (inclusive estornados e estornos) |
| J | J001, J005, J100, J150, J210, J215, J900, J930, J990 | Calculados dos mesmos saldos; signatários do cadastro de responsáveis |
| 9 | 9001, 9900, 9990, 9999 | Contagens |

Detalhes:

- **I050**: só contas com saldo ou movimento no período e as sintéticas acima delas (CTG 2001), em pré-ordem (superior antes da
  filha). `DT_ALT` = data de inclusão da conta.
- **I051**: um por analítica de natureza 01 a 04, quando o exercício tem plano referencial; sem plano referencial, `COD_PLAN_REF` vazio e sem I051.
- **I052 (aglutinação)**: o código de aglutinação da analítica é o código da sua conta superior no **nível de aglutinação** (padrão 3,
  ex.: `1.1.01 DISPONÍVEL`); analíticas de nível menor são o próprio detalhe. As sintéticas acima viram linhas totalizadoras (`T`) do
  J100/J150, com os mesmos códigos do plano; os códigos do I052 só aparecem em linhas de detalhe (`D`).
- **I150/I155**: um período por mês, saldos por conta e centro de custo calculados **das partidas** (não do cache `saldo_mensal`):
  débitos e créditos de cada mês batem com os I250 por construção; saldo inicial = final do mês anterior; linhas todas zeradas omitidas.
  Saldo zero leva o indicador da natureza da conta.
- **I200/I250**: `NUM_LCTO` = número do lançamento no exercício; `IND_LCTO` N/E/X (X com `DT_LCTO_EXT`); `HIST` com o histórico
  completo (o código do histórico padronizado não é informado, para o PVA não duplicar o texto); `NUM_ARQ` = documento; `COD_PART`
  vazio (só participantes do 0150).
- **I350/I355**: em cada data com lançamentos de encerramento, o saldo de cada conta de resultado (por centro de custo) antes deles; inclui
  as contas movimentadas pelo encerramento com saldo zero (a apuração), porque o PVA exige I355 para toda conta de resultado com
  lançamento `E` (REGRA_VALIDACAO_SALDO_CONTA_I200).
- **J100**: exatamente duas linhas de nível 1 (A e P). Se o plano tiver mais de uma raiz em um grupo (ex.: Patrimônio Líquido como
  grupo de nível 1 separado do Passivo), é criado um totalizador artificial (`P` — "PASSIVO E PATRIMÔNIO LÍQUIDO", ou `A`).
  Valor inicial = saldo antes do período; final = saldo no fim (depois do encerramento).
- **J150**: uma linha de nível 1 (a raiz de resultado do plano, ou o totalizador `RES` "RESULTADO LÍQUIDO DO EXERCÍCIO"), `NU_ORDEM`
  sequencial. Detalhe = movimento do período sem os lançamentos de encerramento, que é exatamente a fórmula de conferência do PVA
  `(SF − SI) + I355`. `VL_CTA_INI` = resultado do exercício anterior quando ele existe no sistema; senão vazio. `IND_GRP_DRE` pela
  natureza da conta (credora = R).
- **J210/J215**: DMPL (padrão, `IND_TIP = 1`) com um J210 por código de aglutinação do PL e os fatos (J215) agrupados — resultado do
  exercício (encerramento), saldos de implantação, integralização de capital (contrapartida no ativo), dividendos (contrapartida no
  passivo), transferências entre contas do PL e outras — de modo que **inicial + créditos − débitos = final** (regra do PVA).
  DLPA (`IND_TIP = 0`): um único J210, o do código de lucros/prejuízos acumulados (classificação de `Mutacoes.Classificar`); se não
  houver um único código, cai para DMPL (aviso).
- **J930**: contador (`900`, CRC, UF do CRC, e-mail, telefone, certidão) e o responsável (`IND_RESP_LEGAL = S`): o sócio representante
  legal (código deduzido da qualificação no QSA: sócio-administrador → 205) ou, por opção, o e-CNPJ da empresa (`001`).
- **Contadores** (`0990`, `I990`, `J990`, `9990`, `9900`, `9999`, `I030/J900.QTD_LIN`) contam todas as linhas, inclusive o próprio
  registro; o 9990 inclui o 9999 (conferido na regra `RegraQtdLinBloco9` do PVA).
- **Formatação**: `|` no início e fim, CR LF, ISO-8859-1; datas `ddmmaaaa`; valores com vírgula e 2 casas, sem milhar, sinal no
  indicador D/C. Textos: `|` vira `-`, controles viram espaço, travessões/aspas tipográficas viram ASCII e o que não existe em
  Latin-1 vira `?`.

### Não gerados (limitações)

- **0020** (descentralizada), **0035** (SCP), **0150/0180**: os participantes do sistema são clientes/fornecedores comuns, que não
  entram no 0150 (só coligadas, controladas, vinculadas etc., que o cadastro ainda não identifica).
- **Bloco C**: preenchido pelo próprio PGE ao recuperar a ECD anterior (o manual diz que não deve ser importado).
- **Bloco K**: só quando `IND_ESC_CONS = S` (consolidação). O pedido original era emitir K001/K990 "sem dados", mas o descritor do
  leiaute 9 só aceita `K001.IND_DAD = 0` e marca o bloco como condicional (`REGRA_OBRIGATORIEDADE_BLOCO_K`); emitir `|K001|1|`
  seria rejeitado. Por isso o bloco não é emitido.
- **I012/I015, I300/I310, I500–I555** (livros R, A, B, Z), **I020** (campos adicionais / moeda funcional), **I053**, **I157**
  (mudança de plano), **J800/J801** (anexos RTF), **J932** (substituta), **J935**. Só ECD **original** (`IND_FIN_ESC = 0`).
- O encerramento do exercício zera as contas de resultado por conta, não por centro de custo: a conta fica zerada no total (o PVA
  confere por conta), mas os I155 de dezembro mostram os centros com saldos opostos.

## Pendências (validação prévia)

`ValidadorEcd` (Domain) + conferências no banco (`ServicoEcd`). **Bloqueantes** impedem gerar:

- cadastro: CNPJ inválido; UF; município IBGE (7 dígitos e da UF); NIRE com tamanho errado ou de outra UF (prefixo do NIRE por UF
  do manual — MS é 54); nome ausente;
- período: anos diferentes, início/fim fora do mês, nível de aglutinação < 2, nº do livro;
- plano: analítica de ativo/passivo/PL com nível < 4 (REGRA_VALIDA_NIVEL_CONTAS), natureza diferente da superior a partir do
  nível 3, nível não maior que o da superior;
- referencial: com plano referencial definido, conta analítica (01–04) com saldo/movimento sem vínculo;
- balancete: saldos iniciais que não somam zero, mês com débitos ≠ créditos, lançamento desbalanceado, partida sem histórico,
  extemporâneo em conta de resultado, saldo consolidado (`saldo_mensal`) divergente das partidas (I155 × I250);
- encerramento: exercício sem lançamentos `E` na data final, contas de resultado com saldo depois do encerramento, contas de
  resultado com saldo no início (exercício anterior não encerrado), Balanço inicial/final que não fecha;
- signatários: sem contador; CPF, CRC, UF do CRC, e-mail ou telefone do contador ausentes; contador como responsável; sem
  responsável (ou mais de um); CPF/CNPJ inválido; qualificação fora da tabela; duplicados.

**Avisos**: sem NIRE (sai `IND_NIRE = 0`), sem plano referencial, sem data de arquivamento, certidão do contador ausente ou fora do
formato, competências abertas, rascunhos, lacuna na numeração, conta com inclusão posterior ao período, DLPA convertida em DMPL,
encerramento fora da data final.

## Assinatura

O PVA anexa as assinaturas **ao fim do arquivo, depois do registro 9999** (o manual, no roteiro de substituição: "a assinatura é um
conjunto de caracteres ininteligíveis que fica após o registro 9999"). O formato exato foi lido do código do PVA 10.4.1:

1. **Conteúdo assinado** = hash **SHA-1** (20 bytes) do arquivo **sem assinaturas** (do primeiro byte até o CR LF do 9999).
   Todas as assinaturas assinam esse mesmo hash, inclusive as que forem acrescentadas depois.
2. Cada assinatura é um **PKCS#7/CMS SignedData** em DER (BouncyCastle `CMSSignedDataGenerator.generate(conteúdo, encapsulate=true)`),
   com o hash **encapsulado** (id-data), atributo assinado **signingTime** em UTCTime (o PVA confere se está dentro da validade do
   certificado), cadeia de certificados do signatário, algoritmo **SHA1withRSA**.
3. Gravação: **etiqueta ASCII + DER**, uma após a outra: `SBRCAAEPDR` + ordem da assinatura com 2 dígitos (`01`, `02`...) + código
   de qualificação do J930 com 3 dígitos (`900`, `205`...). Ex.: `SBRCAAEPDR01900<DER>SBRCAAEPDR02205<DER>`. O PVA valida a etiqueta
   com `SBRCAAEPDR([0-9]{2})?([0-9]{3})?`, procura as assinaturas lendo de trás para frente e aceita até 20.000 bytes por assinatura.
4. O PVA ainda grava um atributo assinado **informativo** (OID 1.3.6.1.4.1.311.88.2.2, `|1|versão do PVA|id do descritor|versão|`
   em UTF-16LE), usado só para exibir dados da assinatura — a leitura o trata como opcional. **Não é gravado** aqui.

Implementação: `Vorticis.Infra.Sped.AssinadorSpedCms` (`System.Security.Cryptography.Pkcs.SignedCms`), interface
`IAssinadorSped` no Application. Digest SHA-1 por padrão, como o PVA (SHA-256 opcional no construtor).

**Nível de confiança**: alto quanto ao formato (etiqueta, posição, conteúdo assinado, estrutura CMS e atributos), porque foi tirado
diretamente do código de assinatura e de validação do PVA e conferido com `SignedCms.CheckSignature` nos testes. Não foi possível
executar o PVA neste ambiente para importar um arquivo assinado aqui; ficam como pontos a confirmar na primeira importação real:
(a) a falta do atributo 1.3.6.1.4.1.311.88.2.2 (opcional na leitura do PVA; não se sabe se o ReceitanetBX o exige na transmissão);
(b) a cadeia ICP-Brasil do certificado (o PVA valida a cadeia e se o CPF/CNPJ do certificado é o do J930 — certificados de teste
autoassinados não passam nessa etapa). Se o PVA recusar a assinatura externa, gere o arquivo **sem assinatura** (sempre disponível)
e assine no próprio PVA.

## Certificado padrão

Em Configurações › Certificados digitais, um certificado pode ser marcado como **padrão do contador** e/ou **do responsável**
(migração 029; um por papel, por empresa ou geral). O assistente da ECD já o escolhe para cada signatário do J930. Em A1 a senha é
conferida e guardada só no computador, protegida pelo Windows (DPAPI); o banco não guarda senha. A3 usa o repositório do Windows
(o driver pede o PIN). A lista de A3 mostra apenas certificados com chave em dispositivo/não exportável; certificados A1 instalados
no Windows (chave exportável) podem ser importados para o Vorticis como A1. Os mesmos certificados padrão assinam relatórios em PDF
(opção "Assinar digitalmente o PDF": assinatura CMS destacada `adbe.pkcs7.detached`, SHA-256, com PDFsharp).

## Contador usa o certificado da empresa; assinatura como procurador

- **Quem pode usar os certificados.** Cadastrar, importar, excluir e definir o padrão continuam **só do administrador**. Para **assinar a ECD/ECF**,
  quem tem a função `SPED_ECD_GERAR` ou `SPED_ECF_GERAR` na empresa (perfil Contador, por exemplo) lista os certificados da empresa e os gerais,
  usa o certificado padrão de cada papel e abre o A1 com a senha guardada neste computador: `ServicoCertificados.ListarParaAssinaturaAsync`,
  `ObterPadraoParaAssinaturaAsync` e `AbrirA1ParaAssinaturaAsync` (certificado de outra empresa é recusado). `ListarAsync`, `ObterPadraoAsync` e
  `AbrirA1Async` seguem exclusivos do administrador. O assistente usa os métodos "para assinatura".
- **Procurador (migrações 027 e 052).** O procurador é o **contador** do vínculo da empresa marcado como procurador (Cadastros › Contadores, seção
  Procuração eletrônica): colunas `procurador`, `procuracao_inicio/fim`, `procuracao_abrangencia` (ECD, ECF, EFD, ESOCIAL) e `procuracao_observacao`
  em `core.empresa_contabilidade` (permissão de manter responsáveis). A tabela `core.procurador` foi migrada para os contadores (CPF) e removida. Na opção **Assinar como procurador** (passo 1), o responsável do J930 passa a ser o procurador com
  `COD_ASSIN = 309` (Procurador, conforme a tabela de qualificação do assinante dos manuais da ECD e da ECF) e `IND_RESP_LEGAL = S`, no lugar do sócio
  ou do e-CNPJ. Validações: procuração **vigente na data da geração e que abrange a ECD** (senão `PROCURADOR_NAO_VIGENTE`, bloqueante, dizendo se não há procurador ou
  qual a vigência) e **CPF do certificado = CPF do procurador** (o certificado de outra pessoa é recusado como nas demais qualificações). O
  certificado padrão do papel **procurador** é definido em Configurações › Certificados (`padrao_procurador`). O aviso `PROCURACAO_ECAC` e o passo
  final lembram que a **procuração eletrônica precisa estar cadastrada no e-CAC** (Procurações) para a transmissão.

## Como validar no PVA

1. Gerar no Vorticis (Auditoria › Gerar ECD — assistente em 5 passos: dados, pré-requisitos, validação, assinaturas, gerar e salvar), resolvendo as pendências bloqueantes.
2. No PVA do Sped Contábil: *Escrituração › Importar*, escolher o `.txt`. O PVA lê as assinaturas anexadas e mostra os signatários.
3. *Escrituração › Validar*. Erros e advertências apontam registro e regra (os nomes das regras deste documento são os do PVA).
4. Conferir em *Escrituração › Assinar* a lista de assinantes (devem aparecer como já assinados); se faltar alguém, assinar ali ou
   pelo Vorticis (`ServicoEcd.Assinar`) e reimportar.
5. Transmitir (*Escrituração › Transmitir*) — o hash da escrituração é o `HashSha1` gerado (também gravado em `contabil.ecd_gerada`).
