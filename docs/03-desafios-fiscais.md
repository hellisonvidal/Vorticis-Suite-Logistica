# 03 — Desafios fiscais e legais a programar

> Referência temporal: outubro/2026. Leiautes, prazos e alíquotas mudam com frequência — **todo valor abaixo deve viver em `core.parametro_legal` ou em pacotes de tabelas atualizáveis**, nunca no código. Confirmar sempre no manual/nota técnica vigente antes de implementar.

## 1. Reforma Tributária do consumo (EC 132/2023, LC 214/2025) — o maior risco do projeto

| Ano | O que muda | Impacto no sistema |
|---|---|---|
| **2026** | Ano-teste: CBS 0,9% e IBS 0,1% destacados nos documentos fiscais; compensáveis com PIS/Cofins; dispensa de recolhimento condicionada ao cumprimento das obrigações acessórias | Ler e armazenar o **grupo IBSCBS** da NF-e/NFC-e/CT-e/NFS-e (CST IBS/CBS, `cClassTrib`, bases e alíquotas); NFS-e no **padrão nacional** |
| **2027** | CBS plena; **PIS/Cofins extintos**; IPI zerado (exceto ZFM); início do Imposto Seletivo | Novo plano de contas de tributos, nova apuração não cumulativa por **crédito financeiro**, split payment |
| **2029–2032** | Transição ICMS/ISS → IBS (redução gradual) | Apuração **dupla** simultânea (ICMS/ISS + IBS) com alíquotas por ano |
| **2033** | Modelo novo pleno | Remover regras legadas por vigência, não por exclusão de código |

Requisitos de projeto derivados:
- Toda regra tributária é **versionada por data do fato gerador**.
- O modelo de item de documento já nasce com campos de ICMS/IPI/PIS/Cofins **e** IBS/CBS/IS.
- Simples Nacional: a LC 214 permite optar por recolher IBS/CBS "por fora" do DAS a partir de 2027 — o cadastro de regime precisa desse indicador.

## 2. Contábil e SPED

| Desafio | O que programar |
|---|---|
| **Obrigatoriedade da ECD** | Lucro Real: sempre. Lucro Presumido: obrigado se distribuir lucros acima da base presumida sem livro caixa. Imunes/isentas: conforme receita (IN RFB 2.003/2021). Simples Nacional: dispensado da ECD (salvo exceções), **mas não da escrituração** (CC art. 1.179; NBC para microentidades/PMEs) — o sistema deve escriturar para todos os regimes |
| **Leiaute ECD** | Blocos 0, I (I010, I030, I050, I051, I052, I075, I100, I150/I155, I200/I250, I350/I355), J (J005, J100 BP, J150 DRE, J210 DLPA/DMPL, J800 notas, J900/J930 encerramento e signatários), K (consolidação, se aplicável), 9. Validar contra o PVA antes de entregar |
| **Prazos** | ECD: último dia útil de **junho** do ano seguinte; ECF: último dia útil de **julho**. Gerar alertas por calendário configurável |
| **Plano referencial** | Muda por ano e por tipo de PJ (geral, financeira, seguradora, imune...). Exigir mapeamento I051 de 100% das analíticas com saldo/movimento antes de gerar o arquivo |
| **Saldos periódicos (I155)** | Mensais, com saldo inicial = saldo final do mês anterior; indicadores D/C; validar continuidade entre ECDs de anos consecutivos (recuperação de saldos da ECD anterior) |
| **Encerramento** | Lançamentos de zeramento do resultado com `IND_LCTO = 'E'` na data final; I355 com saldos **antes** do encerramento |
| **Retificação** | ECD substituta só antes do prazo ou com regras específicas; após isso, lançamentos **extemporâneos** (`IND_LCTO = 'X'` + data) |
| **Assinatura** | Certificado ICP-Brasil do representante legal **e** do contador (CRC ativo); o sistema gera e já assina no formato do PVA (PKCS#7 após o 9999 — ver [05-ecd.md](05-ecd.md)); o PVA valida e transmite |
| **Demonstrações** | BP e DRE (Lei 6.404/76 art. 178–187; NBC TG 26); DMPL (ou DLPA, quando dispensada a DMPL — art. 186 §2º); DFC obrigatória para S/A fechadas com PL ≥ R$ 2 mi e demais regidas pela lei; DVA só para companhias abertas |
| **Norma por porte** | NBC TG completa / NBC TG para PMEs / norma para microentidades — o conjunto de demonstrações e notas exigidas varia; parametrizar por empresa |
| **ECF** | Bloco M (e-Lalur/e-Lacs: parte A adições/exclusões, parte B controle de prejuízos e diferenças temporárias), compensação de prejuízo fiscal limitada a **30%** do lucro ajustado (Lucro Real), recuperação dos saldos da ECD por hash |

## 3. Apuração por regime

| Regime | Pontos de atenção |
|---|---|
| **Simples Nacional** | Receita bruta acumulada (RBT12) e proporcionalização para empresas novas; anexos I–V; **Fator R** (folha 12m / receita 12m ≥ 28% → anexo III em vez de V); sublimites estaduais (ICMS/ISS fora do DAS); segregação de receitas (monofásico, ST, exportação); PGDAS-D mensal e DEFIS anual; excesso de limite e exclusão |
| **Lucro Presumido** | Percentuais de presunção por atividade (1,6% / 8% / 16% / 32% p/ IRPJ; 12% / 32% p/ CSLL), atividades mistas, adicional de IRPJ de 10% sobre a parcela > R$ 60 mil/trimestre; regime caixa x competência; **verificar alterações recentes de presunção (LC 224/2025) para receitas acima de R$ 5 mi**; PIS/Cofins cumulativos até 2026 |
| **Lucro Real** | Trimestral x anual com estimativas mensais (balancetes de suspensão/redução); LALUR/LACS; adições/exclusões; JCP; PIS/Cofins não cumulativos (créditos) até 2026; depreciação fiscal x societária; subvenções (Lei 14.789/2023) |
| **Retenções** | IRRF, CSRF (PIS/Cofins/CSLL 4,65%), INSS 11% sobre cessão de mão de obra, ISS retido — reconhecidas na entrada de NFS-e e escrituradas na **EFD-Reinf** (série R-2000 e R-4000; DIRF extinta) |

## 4. Documentos fiscais (importação)

| Desafio | O que programar |
|---|---|
| Autenticidade | Validar schema XSD **e** assinatura XMLDSig do XML; opcionalmente consultar situação na SEFAZ (cancelamento posterior) |
| Duplicidade | Chave de acesso única (44 dígitos, com DV módulo 11); hash SHA-256 do arquivo |
| Eventos | Cancelamento, carta de correção, manifestação do destinatário — estornar contabilização de nota cancelada |
| NFS-e | Até 2025 havia ~5.000 leiautes municipais; padrão nacional (ADN) é o alvo — manter adaptadores para legados |
| CFOP → contabilização | Regra por CFOP + NCM + participante + CST; devoluções (CFOP 1.2xx/5.2xx) invertem a natureza |
| Guarda | XML original imutável pelo prazo decadencial (CTN arts. 150 §4º e 173: 5 anos, contados conforme o caso) — reter ao menos 6 anos |
| OFX | Versões 1.x (SGML) e 2.x (XML), encodings variados; deduplicar por `FITID` + conta; conferir saldo final do extrato com o saldo contábil |

## 5. Folha de pagamento e eSocial

| Desafio | O que programar |
|---|---|
| **Ordem de eventos eSocial** | Tabelas (S-1000, S-1005, S-1010 rubricas, S-1020 lotações) → não periódicos (S-2200 admissão, S-2206, S-2230 afastamento, S-2299/S-2399 desligamento) → periódicos (S-1200 remuneração, S-1210 pagamentos, S-1299 fechamento). Leiaute **S-1.3** (confirmar NT vigente) |
| **Prazos** | S-2200 até o dia anterior ao início do trabalho; S-2299 em até 10 dias do desligamento; periódicos até o dia 15 do mês seguinte |
| **Rubricas** | Natureza (Tabela 03) + incidências de CP, IRRF e FGTS por rubrica — erros aqui geram recolhimento errado |
| **INSS** | Alíquotas progressivas por faixa até o teto (Portaria anual); contribuição patronal 20% + RAT×FAP + terceiros (por FPAS); desoneração da folha em reoneração gradual (Lei 14.973/2024: 2025–2027) |
| **IRRF** | Tabela progressiva, dedução por dependente ou **desconto simplificado** (o mais vantajoso); a partir de 2026, isenção até R$ 5.000/mês com redutor até R$ 7.350 (Lei 15.270/2025) — modelar como regra parametrizada; regime de caixa (competência do pagamento, S-1210) |
| **FGTS** | 8% (2% aprendiz); recolhimento pelo **FGTS Digital** (guia GFD a partir do eSocial) — GFIP extinta para FGTS |
| **Contribuições previdenciárias** | Apuradas pela **DCTFWeb** a partir de eSocial + EFD-Reinf; DARF único |
| **Cálculos trabalhistas** | Férias (1/3, abono, dobra), 13º (adiantamento até 30/11, saldo até 20/12), rescisão por tipo (verbas, aviso proporcional Lei 12.506/2011, multa 40%/20%), médias de variáveis, DSR, horas extras, adicional noturno, insalubridade/periculosidade |
| **Convenções coletivas** | Pisos, reajustes, adicionais e prazos por sindicato/CCT — cadastro com vigência |
| **SST** | S-2210 (CAT), S-2220 (ASO), S-2240 (agentes nocivos) — integração com prestador de SST ou cadastro mínimo |
| **Arredondamentos** | Cada rubrica arredondada a 2 casas, com memória de cálculo gravada (auditoria e reclamatória) |
| **Contabilização da folha** | Salários, encargos, **provisões mensais de férias e 13º com encargos** (regime de competência), baixa das provisões no pagamento |

## 6. Integrações governamentais

| Sistema | Interface | Uso no Vorticis |
|---|---|---|
| eSocial | Web service SOAP, lotes assinados, consulta assíncrona por protocolo | Envio/retorno de eventos da folha |
| EFD-Reinf | API REST, eventos assinados | Retenções na fonte (R-4010/R-4020), serviços tomados (R-2010) |
| DCTFWeb | Portal e-CAC (gerada a partir de eSocial/Reinf) | Conferência e emissão de DARF — no sistema, conciliar o valor esperado |
| FGTS Digital | Portal (dados vêm do eSocial) | Conferência da guia |
| SEFAZ (NF-e) | Web services DF-e: Distribuição DF-e, Consulta Protocolo, Manifestação | Baixar automaticamente NF-e emitidas contra o CNPJ |
| NFS-e Nacional (ADN) | API REST | Baixar NFS-e emitidas/tomadas |
| SPED (ECD/ECF/EFD) | **PVA** da RFB (Java) | O sistema gera o `.txt`; o PVA valida, assina e transmite |
| Simples Nacional | PGDAS-D/DEFIS (portal) | Sistema calcula e gera memória para conferência |

## 7. Requisitos não funcionais com consequência legal

- **Imutabilidade e trilha de auditoria** (fiscalização e perícia contábil).
- **Prazo de guarda** de livros e documentos: enquanto não prescritas as ações (CTN; CC art. 1.194); para folha e FGTS, prazos trabalhistas/previdenciários próprios — política de retenção configurável, sem exclusão física dentro do prazo.
- **LGPD** (Lei 13.709/2018): base legal de obrigação legal para dados trabalhistas, minimização, controle de acesso e registro de tratamento.
- **Responsabilidade técnica**: identificar o contador (CRC) responsável por empresa e período.
