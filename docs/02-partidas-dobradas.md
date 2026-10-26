# 02 — Validação de partidas dobradas

A integridade é garantida em **duas camadas**:

1. **Aplicação** (`ServicoLancamento`) — valida tudo, devolve mensagens amigáveis e acumula todos os erros de uma vez.
2. **Banco** (triggers em [`db/001_core_contabil.sql`](../db/001_core_contabil.sql)) — última linha de defesa contra bug, importação malfeita ou acesso direto ao banco.

## 1. Regras

| # | Regra | Fundamento | Onde |
|---|---|---|---|
| R1 | Σ Débitos = Σ Créditos em todo lançamento efetivado | Método das partidas dobradas (ITG 2000) | App + constraint trigger diferido |
| R2 | Ao menos 1 partida a débito e 1 a crédito | idem | App + trigger |
| R3 | Valor > 0; o sinal é dado por `ind_dc`, nunca pelo valor | ECD I250 (VL_DC positivo + IND_DC) | `CHECK` |
| R4 | Só contas **analíticas**, ativas, da mesma empresa, existentes na data | ECD I050/I250 | Trigger |
| R5 | Centro de custo / participante quando a conta exigir | Política da empresa / ECD I250 | Trigger |
| R6 | Data dentro do exercício e em **período aberto** | Fechamento mensal | Trigger |
| R7 | Lançamento efetivado é **imutável**; correção só por estorno (empresa com `usa_estorno`). Sem estorno, a migração 049 libera exclusão/alteração em transação declarada (`vorticis.mantendo_lancamentos`), com período aberto, exercício não encerrado e sem ECD, e registra `EXCLUI`/`ALTERA` na auditoria | ITG 2000 itens 31–36 (retificação por estorno, transferência ou complementação) | Triggers |
| R8 | Numeração sequencial, sem lacunas, atribuída só na efetivação | Livro Diário — ordem cronológica, sem intervalos/rasuras (CC art. 1.183) | App (sequência com lock) |
| R9 | Encerramento de resultado (`IND_LCTO = 'E'`) só na data final do exercício | ECD I200 | Trigger |
| R10 | Histórico obrigatório em toda partida | ECD I250.HIST | `NOT NULL` |
| R11 | Valores com no máximo 2 casas decimais — o validador **rejeita** (não arredonda em silêncio). Módulos que calculam valores (rateios, impostos) usam `Dinheiro.Ratear`, que distribui centavos pelo maior resto e garante soma exata | Evita diferenças "de R$ 0,01" | App |

## 2. Por que *constraint trigger* `DEFERRABLE INITIALLY DEFERRED`

Um `CHECK` comum não enxerga várias linhas. Um trigger imediato falharia ao inserir a primeira partida (ainda desbalanceada). O trigger diferido roda **no COMMIT**, quando todas as partidas já existem — se a soma não fechar, a transação inteira é desfeita.

```sql
-- Trecho central (ver arquivo completo)
SELECT COALESCE(SUM(valor) FILTER (WHERE ind_dc = 'D'), 0),
       COALESCE(SUM(valor) FILTER (WHERE ind_dc = 'C'), 0)
  INTO v_deb, v_cred
  FROM contabil.partida WHERE lancamento_id = v_lanc_id;

IF v_deb <> v_cred THEN
    RAISE EXCEPTION 'Lançamento %: débitos (%) <> créditos (%), diferença %', ...;
END IF;
```

Rascunhos (`status = 'RASCUNHO'`) podem ficar desbalanceados enquanto o usuário digita; a validação dispara na transição para `EFETIVADO`.

## 3. Camada de aplicação (C#)

| Peça | Arquivo | Papel |
|---|---|---|
| `ValidadorLancamento` | `src/Vorticis.Domain/Contabil/ValidadorLancamento.cs` | Regras R1–R6, R9–R11; função pura, acumula todos os erros |
| `Estorno` | `src/Vorticis.Domain/Contabil/Estorno.cs` | R7: gera o lançamento espelhado (bloqueia estorno duplo, de estorno, de encerramento, retroativo) |
| `ServicoLancamento` | `src/Vorticis.Application/Contabil/ServicoLancamento.cs` | Único ponto de gravação: carrega contexto → valida → grava; traduz rejeições do banco em mensagens |
| `RepositorioContabil.GravarEfetivadoAsync` | `src/Vorticis.Infra/Postgres/RepositorioContabil.cs` | R8: transação com `SELECT ... FOR UPDATE` no exercício, número = máx + 1, insere rascunho + partidas, efetiva; no estorno marca o original na mesma transação |
| `ServicoPeriodo` | `src/Vorticis.Application/Contabil/ServicoPeriodo.cs` | Fecha em ordem cronológica após `contabil.fn_verificar_invariantes`; reabre em ordem inversa |

Fluxo de efetivação:

```
LancamentoProposto (códigos) ──► ServicoLancamento.EfetivarAsync
   ├─ carrega contas, centros de custo, participantes e período (1 consulta cada)
   ├─ ValidadorLancamento.Validar ──► erros? devolve Resultado.Falha (nada é gravado)
   └─ RepositorioContabil.GravarEfetivadoAsync (1 transação)
        lock exercício → numero → INSERT lançamento (RASCUNHO) → INSERT partidas (unnest)
        → UPDATE status EFETIVADO (triggers: período aberto, saldo_mensal, auditoria)
        → COMMIT (constraint trigger: Σ D = Σ C)
```

## 4. Testes

`db/tests/001_partidas_dobradas_test.sql` cobre 11 casos no nível SQL; `tests/Vorticis.Domain.Tests` cobre o validador, estorno, balancete, rateio e CNPJ; `tests/Vorticis.Infra.Tests` cobre o fluxo completo contra PostgreSQL real (inclusive 40 efetivações concorrentes sem lacuna de numeração). O SQL (aceite de lançamento balanceado, rejeição de desbalanceado, sem crédito, conta sintética, valor negativo, período fechado, data fora do exercício, alteração/exclusão de efetivado, e estorno zerando saldo). Para rodar:

```bash
createdb vorticis_test
psql -v ON_ERROR_STOP=1 -d vorticis_test -f db/001_core_contabil.sql -f db/002_saldos_auditoria_fechamento.sql \
     -f db/tests/001_partidas_dobradas_test.sql
# saída final esperada: TODOS OS TESTES PASSARAM
```

## 5. Invariantes globais a verificar no fechamento

Implementados em `contabil.fn_verificar_invariantes` (db/002): rascunhos pendentes, Σ D = Σ C do mês, `saldo_mensal` = partidas e numeração sem lacunas. Ainda a implementar (dependem das demonstrações e do SPED): (b) e (d) abaixo.

```sql
-- (a) Balancete fecha: Σ saldos devedores = Σ saldos credores (analíticas)
-- (b) Ativo = Passivo + PL (após apuração do resultado)
-- (c) saldo_mensal == recalculado a partir das partidas (detecta corrupção do cache)
-- (d) toda conta analítica com movimento tem mapeamento referencial no ano (pré-requisito ECD/ECF)
-- (e) nenhuma lacuna em lancamento.numero no exercício
```
