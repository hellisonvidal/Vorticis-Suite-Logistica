# 04 — Roadmap

Cada fase só termina quando o **critério de saída** é atendido com dados reais de pelo menos uma empresa piloto.

| Fase | Entregas | Critério de saída |
|---|---|---|
| **0. Fundação** | Solução .NET + Avalonia; instalador com PostgreSQL local; migrações versionadas; login/perfis; multiempresa; backup/restauração; `core.parametro_legal`; trilha de auditoria; CI com testes de banco | Instalar em Windows limpo, criar empresa, fazer backup e restaurar em outra máquina |
| **1. MVP Contábil** | Plano de contas (CRUD + importação de modelo); centros de custo; históricos padrão; lançamento manual com validação de partidas dobradas; estorno; fechamento/reabertura de período; **Livro Diário** e **Razão** (PDF/Excel); **Balancete de Verificação**; importação de saldos iniciais | 3 meses de uma empresa real escriturados; balancete batendo com o sistema anterior |
| **2. Importação e automação** | Importação OFX/CSV/XLSX com dedup; NF-e/CT-e XML (entrada e saída) com validação de assinatura; regras de contabilização por CFOP/NCM/participante; motor de **conciliação bancária** com sugestões e aprendizado por confirmação do usuário; importação de lançamentos por planilha modelo | ≥ 80% das transações bancárias de um mês classificadas automaticamente com acerto conferido |
| **3. Demonstrações e ECD** | Apuração e encerramento do resultado; **BP, DRE, DMPL/DLPA, DFC** (indireto); notas explicativas (texto livre + quadros); plano referencial + de/para; **gerador ECD** com validação prévia; Termos de abertura/encerramento | Arquivo ECD de um exercício validado **sem erros** no PVA |
| **4. Fiscal e ECF** | Apuração Simples Nacional (anexos, Fator R, RBT12), Presumido e Real (LALUR/LACS); retenções; NFS-e nacional; leitura do grupo IBS/CBS; **gerador ECF**; EFD-Reinf | ECF validada no PVA; DAS calculado igual ao PGDAS-D em 3 competências |
| **5. Folha de pagamento** | Cadastros (trabalhador, vínculo, rubricas, sindicatos/CCT); cálculo mensal, férias, 13º, rescisão; provisões; holerite; contabilização automática da folha; relatórios de encargos | Folha de 3 meses idêntica (centavo a centavo) à folha de referência do piloto |
| **6. eSocial e integrações gov** | Assinatura A1/A3; eventos de tabela, não periódicos, periódicos e SST mínimo; fila com retentativa e consulta de lote; painel de pendências; conferência DCTFWeb/FGTS Digital; Distribuição DF-e (SEFAZ) e ADN NFS-e | Ciclo completo admissão → folha → fechamento → desligamento aceito em **produção restrita** e depois em produção |
| **7. Reforma Tributária plena** | Apuração CBS/IBS/IS com créditos financeiros; transição ICMS/ISS→IBS por ano; split payment (conciliação); novos planos/rubricas por vigência | Simulação 2027 com dados de 2026 da empresa piloto |
| **8. Versão completa / escala** | Modo servidor (PostgreSQL na LAN ou nuvem) + API REST; multiusuário concorrente; consolidação (ECD bloco K); ativo imobilizado com depreciação; estoque/custos; dashboards; portal do cliente; Linux/macOS via Avalonia | Escritório com ≥ 5 usuários e ≥ 20 empresas operando simultaneamente |

## Progresso

- [x] Fase 0 — solução .NET 10, migrações versionadas com hash, multiempresa, auditoria encadeada, CI com PostgreSQL, executável Windows
- [x] Fase 0 — login, perfis por empresa (Contador/Assistente/Consulta), administração de usuários, menu principal, configuração de conexão pela UI
- [x] Fase 0 — cadastro de empresa com consulta à base pública do CNPJ (RFB)
- [x] Fase 0 — perfis de acesso configuráveis pelo administrador, verificados nos serviços
- [x] Fase 0 — backup e restauração pela UI (pg_dump/pg_restore, cópia de segurança automática)
- [x] Fase 0 — instalador único (Inno Setup) com PostgreSQL embutido gerenciado pelo programa: sem configuração, sem serviço, sem administrador; testado em Windows no CI
- [x] Fase 1 — lançamento com validação, estorno, fechamento/reabertura, Diário, Razão, Balancete (CSV), plano de contas modelo
- [x] Fase 1 — manutenção do plano de contas e dos centros de custo, saldos iniciais (implantação)
- [x] Fase 1 — Diário/Razão/Balancete em PDF, Diário com termos de abertura e encerramento
- [x] Fase 3 (parcial) — encerramento do exercício (e desfazer), Balanço Patrimonial e DRE em tela e PDF
- [x] Fase 3 — plano referencial da RFB: importação da tabela oficial, vínculo das contas com sugestões, cópia do ano anterior e pendências
- [ ] Fase 3 — DMPL/DLPA, DFC, gerador do arquivo ECD e validação no PVA

## Prioridade dentro do MVP (Fase 1)

1. `ServicoLancamento.Efetivar/Estornar` + triggers (já especificados em [`db/001_core_contabil.sql`](../db/001_core_contabil.sql)).
2. Tela de lançamento com digitação rápida (código reduzido, histórico padrão, totais D/C em tempo real, diferença destacada).
3. Saldos mensais + Balancete (base de todos os relatórios).
4. Diário e Razão com termos de abertura/encerramento.
5. Fechamento de período com as checagens de invariantes (ver [02](02-partidas-dobradas.md#5-invariantes-globais-a-verificar-no-fechamento)).

## Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Mudança de leiaute/legislação durante o desenvolvimento (Reforma Tributária) | Regras por vigência; pacote de tabelas atualizável; acompanhar Notas Técnicas do Portal NF-e, eSocial e SPED |
| Cálculo de folha divergente (risco trabalhista) | Testes de regressão com casos reais "centavo a centavo"; memória de cálculo por rubrica |
| Escopo grande para equipe pequena | Fases com critério de saída; folha/eSocial só depois do contábil estável |
| Dependência de bibliotecas de terceiros (DF-e) | Isolar atrás de interfaces (`IDocumentoFiscalParser`) |
