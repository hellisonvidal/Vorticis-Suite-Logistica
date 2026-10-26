# 07 — eSocial: assinatura e transmissão pelo Vorticis

Este guia descreve o que o eSocial exige para transmitir eventos, o que o módulo Folha do Vorticis já faz e o que ainda falta, e traz um checklist para o usuário. As informações técnicas vêm da documentação oficial (portal do eSocial › Documentação técnica): *Manual de Orientação do Desenvolvedor* v1.16, *Pacote de Comunicação* v1.6 (XSD e WSDL dos lotes) e *Esquemas XSD S-1.3* de 01/07/2026 (a versão de 26/10/2026 entra em vigor nos próximos dias; ver "O que falta").

## 1. Como o eSocial recebe os eventos

| Item | Regra oficial |
|---|---|
| Leiaute | S-1.3. Namespace de cada evento: `http://www.esocial.gov.br/schema/evt/<evtXxx>/v_S_01_03_00` (ex.: `evtAdmissao`). |
| Estrutura do evento | `<eSocial xmlns=…><evtXxx Id="ID…">…</evtXxx><Signature/></eSocial>`. O Id tem 36 caracteres (`ID` + tpInsc + CNPJ de 14 posições + AAAAMMDDHHMMSS + sequencial de 5). |
| Assinatura | XMLDSig *enveloped*, certificado ICP-Brasil A1 ou A3 (RSA 2048), RSA-SHA256, digest SHA-256, transformações enveloped-signature + C14N (`xml-c14n-20010315`), `Reference URI=""`, KeyInfo apenas com o `X509Certificate` do usuário final. A assinatura é o último filho de `<eSocial>`. |
| Lote | `EnvioLoteEventos` v1_1_1: `ideEmpregador`, `ideTransmissor`, `eventos` (até **50** eventos) e atributo `grupo`: **1** tabelas, **2** não periódicos, **3** periódicos. Um lote só tem eventos de um grupo; um evento fora do grupo do lote é rejeitado. |
| Serviços (SOAP 1.1, assíncrono) | `EnviarLoteEventos` v1_1_0 devolve o **protocolo** (`A.B.NNNNNNNNNNNNNNNNNNN`); `ConsultarLoteEventos` v1_1_0 recebe o protocolo e devolve o resultado de cada evento. |
| Autenticação | TLS mútuo: certificado ICP-Brasil do transmissor na conexão (`RequireClientCertificate`). O CPF/CNPJ de `ideTransmissor` é comparado ao do certificado; se diferir, o lote é rejeitado. A cadeia ICP-Brasil/RFB (raiz, AC RFB v3, AC SERPRO RFB SSL) precisa estar confiável na máquina. |
| Produção restrita (testes) | Envio: `https://webservices.producaorestrita.esocial.gov.br/servicos/empregador/enviarloteeventos/WsEnviarLoteEventos.svc` · Consulta: `https://webservices.producaorestrita.esocial.gov.br/servicos/empregador/consultarloteeventos/WsConsultarLoteEventos.svc`. Eventos com `tpAmb = 2`. É possível apagar os dados do empregador enviando S-1000 com `classTrib = 00` e `tpAmb = 2`. |
| Produção | Envio: `https://webservices.envio.esocial.gov.br/servicos/empregador/enviarloteeventos/WsEnviarLoteEventos.svc` · Consulta: `https://webservices.consulta.esocial.gov.br/servicos/empregador/consultarloteeventos/WsConsultarLoteEventos.svc`. Eventos com `tpAmb = 1`. Vale perante Receita/FGTS. |
| Retorno do envio | `cdResposta` 201 (recebido), 202 (recebido com advertências), 203 (já recebido, na fila), 301 (erro no servidor), 401–405 (lote incorreto: preenchimento, schema, versão, certificado, lote vazio). |
| Retorno da consulta | Lote: 101 (aguardando), 201/202 (processado), 301/4xx (erro). Por evento: 201 sucesso, 202 sucesso com advertência, 4xx rejeitado; sucesso traz o **recibo** (`nrRecibo`); rejeição traz `ocorrencias` (tipo 1 erro / 2 advertência, código, descrição, localização). Respeite o `tempoEstimadoConclusao` — consultar demais é "uso indevido". |
| Transmissor ≠ empregador | `ideTransmissor` pode ser o procurador ou o contador (CPF ou CNPJ do certificado diferente do empregador). Exige **procuração eletrônica** (e-CAC/RFB ou Caixa; módulo de procurações do eSocial), com o **perfil** do grupo de eventos: Preliminar (S-2190), Rotinas (S-1010, S-1020, S-1200, S-1210, S-2200, S-2206, S-2230…), SST, Desligamento (S-2299, S-2250) e Especial (S-1000, S-1005, S-1299…). Sem o perfil, o eSocial devolve **411** (assinante inválido). |

## 2. Cadastros obrigatórios por evento (o que o Vorticis precisa ter)

| Evento | Dados indispensáveis | Onde se cadastra no Vorticis |
|---|---|---|
| S-1000 (empregador) | CNPJ, classificação tributária (`classTrib`), `indDesFolha`, `indOptRegEletron` | Empresa; Folha › Parâmetros (classTrib) |
| S-1005 (estabelecimento) | CNAE preponderante, alíquota RAT (1/2/3) e FAP (4 decimais) | Empresa (CNAE); Folha › Parâmetros |
| S-1010 (rubricas) | código, descrição, natureza (Tabela 03), tipo, incidências INSS/IRRF/FGTS | Folha › Rubricas |
| S-1020 (lotação) | código, tipo da lotação, FPAS, terceiros | Folha › Departamentos e Parâmetros |
| S-2200 (admissão) | CPF, nome, sexo, raça/cor, grau de instrução, nascimento, endereço (tipo de logradouro, logradouro, nº, CEP, município IBGE, UF), data de admissão, matrícula, categoria, **CNPJ do sindicato**, cargo, CBO, salário, tipo de contrato, **tipo e descrição da jornada** (`tpJornada`/`dscJorn`), dependentes | Folha › Funcionários (campos novos: *Tipo* de logradouro e *CNPJ do sindicato*) |
| S-2206 / S-2230 / S-2299 | vínculo existente (S-2200 processado), alterações de salário/cargo, afastamentos, rescisão | Folha |
| S-1200 / S-1210 | folha calculada, rubricas já enviadas no S-1010, lotações já enviadas no S-1020 | Folha › Cálculo |
| S-1299 | todos os S-1200/S-1210 da competência processados | Folha |

Observação: no leiaute S-1.3 o NIS (`nisTrab`) não é mais enviado no S-2200 (consta apenas no retorno do evento).

## 3. Sequência de envio

1. **Teste na produção restrita** (ambiente separado, sem efeito legal). Só depois repita em produção.
2. **S-1000** (empregador) → aguarde o processamento.
3. **Tabelas** (grupo 1): S-1005, S-1010, S-1020 → aguarde.
4. **Não periódicos** (grupo 2): S-2200 (admissões), S-2206, S-2230, S-2299 → aguarde.
5. **Periódicos** (grupo 3): S-1200 e S-1210 da competência → aguarde.
6. **S-1299** (fechamento) — só quando todos os S-1200/S-1210 da competência estiverem processados.

O Vorticis impõe essa ordem: não envia um grupo enquanto houver evento de grupo anterior aguardando retorno, e segura o S-1299 até o processamento dos periódicos.

## 4. O que o Vorticis já tem

- Geração dos XMLs S-1000, S-1005, S-1010, S-1020, S-2200, S-2206, S-2230, S-2299, S-1200, S-1210 e S-1299, **validados contra os XSDs oficiais S-1.3** (embutidos em `src/Vorticis.Folha/Esocial/Xsd`; `ValidadorXsdEsocial`). A geração avisa o que falta no cadastro (ex.: CNPJ do sindicato).
- Assinatura XMLDSig (`AssinadorEsocial`) conforme o manual, testada com certificado autoassinado, inclusive a conferência da assinatura dentro do lote.
- Montagem de lotes por grupo (até 50 eventos), validados no XSD `EnvioLoteEventos-v1_1_1`.
- Cliente SOAP com TLS mútuo (`ClienteEsocial`), ambientes **produção restrita (padrão)** e produção (exige confirmação), protocolo, consulta, recibo e ocorrências por evento, status `GERADO → ASSINADO → ENVIADO → PROCESSADO | ERRO`, reenvio de eventos cujo lote foi recusado. Migração `db/044`.
- Transmissor: empregador (e-CNPJ com a mesma raiz do CNPJ), **procurador** (CPF do certificado = procurador cadastrado, com procuração **vigente** hoje) ou **contador/escritório** (documento do certificado = vínculo contábil vigente). Os certificados padrão por papel (Configurações › Certificados digitais) são usados automaticamente.
- Tela **Folha › Obrigações › eSocial › Transmissão** (permissão `FOLHA_ESOCIAL`) e testes com servidor HTTP simulado (nenhum teste acessa a Receita).

## 5. O que falta / precisa ser confirmado

- **Homologar na produção restrita com um certificado real**: nenhum teste automatizado fala com a Receita. Pontos a observar: cadeia de confiança ICP-Brasil no computador, leitura do CPF/CNPJ do certificado (o sistema lê o CN `NOME:documento` e, se não houver, o *otherName* 2.16.76.1.3.x), tempo de resposta e o comportamento do eSocial ao reenviar um evento cujo lote foi recusado.
- A procuração eletrônica é verificada **apenas pelo cadastro do Vorticis** (vigência); a validade real é conferida pelo eSocial (erro 411). O perfil outorgado precisa cobrir os grupos enviados.
- Subconjunto de campos: o Vorticis gera os campos obrigatórios e os usuais; ficam de fora (por ora) FGTS/opção, trabalhador estrangeiro/imigrante, deficiência, sucessão, estatutários, trabalho intermitente/temporário, aprendiz, S-2205/S-2250/S-2298 e eventos de SST (S-2210, S-2220, S-2240) e de retificação/exclusão (S-3000). Retificação (`indRetif = 2`) e `nrRecibo` ainda não são gerados.
- A **tabela 03 (natureza das rubricas)**, os códigos de incidência (21/22/23) e a classificação das rubricas padrão precisam ser conferidos por quem conhece a folha da empresa: o XSD só valida o formato, não o significado.
- XSDs: foi embutido o pacote de **01/07/2026**. O de **26/10/2026** (NT 07/2026) entra em vigor em poucos dias; substitua os arquivos em `Esocial/Xsd`, rode os testes e ajuste os geradores se houver mudança de campos.
- CNPJ alfanumérico (a partir de jul/2026): o Vorticis ainda trata o CNPJ como numérico.
- Consulta automática periódica (hoje é por botão) e leitura do evento de retorno com totalizadores (S-5xxx).

## 6. Checklist do usuário

**Antes de qualquer envio**
- [ ] Empresa cadastrada com CNPJ, CNAE, UF e município corretos; Folha › Parâmetros preenchidos (classificação tributária, FPAS, RAT/FAP, responsável).
- [ ] Rubricas conferidas (natureza, incidências); departamentos/lotações cadastrados.
- [ ] Funcionários completos: CPF, data de nascimento, sexo, raça/cor, escolaridade, endereço **com tipo de logradouro**, CBO, cargo, categoria, **CNPJ do sindicato**, jornada.
- [ ] Folha da competência calculada (para S-1200/S-1210).
- [ ] Gerar os XMLs em *Obrigações › eSocial › Geração dos eventos*; resolver todos os avisos.

**Certificado**
- [ ] Certificado ICP-Brasil A1 (.pfx) importado, ou A3 conectado (Windows), em *Configurações › Certificados digitais*, e definido como **padrão** do papel (responsável/empresa, procurador ou contador).
- [ ] Cadeia ICP-Brasil/RFB instalada no computador que transmite.

**Transmitindo com procurador ou contador**
- [ ] Procuração eletrônica outorgada no **e-CAC** (ou módulo de procurações do eSocial) ao CPF/CNPJ do certificado, com os perfis dos eventos (Especial, Rotinas, Desligamento…) e prazo vigente.
- [ ] Contador procurador cadastrado no Vorticis, com vigência e abrangência eSocial (*Cadastros › Contadores*, Procuração eletrônica); contador/escritório com vínculo vigente.

**Teste e produção**
- [ ] Transmitir tudo na **produção restrita** primeiro (S-1000 → tabelas → não periódicos → periódicos → S-1299), consultando o retorno entre as etapas; corrigir as ocorrências e regerar os eventos rejeitados.
- [ ] Só então repetir em **produção** (o sistema pede confirmação). Guardar os recibos.
