-- Testes da migração 001. Executar em banco descartável:
--   psql -v ON_ERROR_STOP=1 -f db/001_core_contabil.sql -f db/tests/001_partidas_dobradas_test.sql
-- Cada caso negativo roda dentro de um bloco que espera a exceção.

INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ)
VALUES ('11222333000181', 'Empresa Teste LTDA', '2062', '6920601', 'SP', '3550308', '2020-01-01');

INSERT INTO contabil.exercicio (empresa_id, ano, data_inicio, data_fim) VALUES (1, 2026, '2026-01-01', '2026-12-31');
INSERT INTO contabil.periodo (exercicio_id, competencia)
SELECT 1, generate_series('2026-01-01'::date, '2026-12-01', '1 month')::date;
UPDATE contabil.periodo SET status = 'FECHADO' WHERE competencia = '2026-01-01';

INSERT INTO contabil.plano_contas (empresa_id, codigo, descricao, tipo, nivel, conta_pai_id, cod_nat, natureza_saldo, data_inclusao) VALUES
 (1, '1',          'ATIVO',            'S', 1, NULL, '01', 'D', '2020-01-01'),
 (1, '1.1.01.001', 'Caixa',            'A', 2, 1,    '01', 'D', '2020-01-01'),
 (1, '1.1.02.001', 'Banco Conta Movimento', 'A', 2, 1, '01', 'D', '2020-01-01'),
 (1, '1.1.03.001', 'Clientes',         'A', 2, 1,    '01', 'D', '2020-01-01'),
 (1, '1.1.04',     'Estoques (sintética)', 'S', 2, 1, '01', 'D', '2020-01-01');

CREATE FUNCTION pg_temp.espera_erro(sql TEXT, trecho TEXT) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    BEGIN
        EXECUTE sql;
        SET CONSTRAINTS ALL IMMEDIATE;                         -- força o teste diferido dentro do bloco
    EXCEPTION WHEN OTHERS THEN
        IF position(trecho IN SQLERRM) = 0 THEN
            RAISE EXCEPTION 'Erro inesperado. Esperava "%", obtive "%"', trecho, SQLERRM;
        END IF;
        RAISE NOTICE 'OK (rejeitado): %', SQLERRM;
        RETURN;
    END;
    RAISE EXCEPTION 'Deveria ter falhado: %', trecho;
END $$;

-- Caso 1: lançamento balanceado é aceito.
BEGIN;
INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem) VALUES (100, 1, 1, '2026-03-10', 'MANUAL');
INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico) VALUES
 (100, 1, 2, 'D', 1000.00, 'Depósito em espécie'),
 (100, 2, 3, 'D',  500.50, 'Depósito em espécie'),
 (100, 3, 4, 'C', 1500.50, 'Recebimento de clientes');
UPDATE contabil.lancamento SET status = 'EFETIVADO', numero = 1, efetivado_em = now() WHERE id = 100;
COMMIT;

-- Caso 2: desbalanceado é rejeitado no commit.
SELECT pg_temp.espera_erro($q$
  INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem) VALUES (101, 1, 1, '2026-03-11', 'MANUAL');
  INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico) VALUES
   (101, 1, 2, 'D', 100.00, 'x'), (101, 2, 4, 'C', 99.99, 'x');
  UPDATE contabil.lancamento SET status = 'EFETIVADO', numero = 2 WHERE id = 101;
$q$, 'débitos (100.00) <> créditos (99.99)');

-- Caso 3: sem partida de crédito.
SELECT pg_temp.espera_erro($q$
  INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem) VALUES (102, 1, 1, '2026-03-11', 'MANUAL');
  INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico) VALUES (102, 1, 2, 'D', 10, 'x');
  UPDATE contabil.lancamento SET status = 'EFETIVADO', numero = 2 WHERE id = 102;
$q$, 'ao menos uma partida a débito e uma a crédito');

-- Caso 4: conta sintética.
SELECT pg_temp.espera_erro($q$
  INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem) VALUES (103, 1, 1, '2026-03-11', 'MANUAL');
  INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico) VALUES (103, 1, 5, 'D', 10, 'x');
$q$, 'é sintética');

-- Caso 5: valor zero ou negativo.
SELECT pg_temp.espera_erro($q$
  INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem) VALUES (104, 1, 1, '2026-03-11', 'MANUAL');
  INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico) VALUES (104, 1, 2, 'D', -10, 'x');
$q$, 'ck_valor_positivo');

-- Caso 6: período fechado.
SELECT pg_temp.espera_erro($q$
  INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem) VALUES (105, 1, 1, '2026-01-15', 'MANUAL');
  INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico) VALUES
   (105, 1, 2, 'D', 10, 'x'), (105, 2, 4, 'C', 10, 'x');
  UPDATE contabil.lancamento SET status = 'EFETIVADO', numero = 2 WHERE id = 105;
$q$, 'está fechado');

-- Caso 7: data fora do exercício.
SELECT pg_temp.espera_erro($q$
  INSERT INTO contabil.lancamento (empresa_id, exercicio_id, data_lancamento, origem) VALUES (1, 1, '2027-01-02', 'MANUAL');
$q$, 'fora do exercício');

-- Caso 8: alterar partida de lançamento gravado fora da tela (migração 049: só pela função de alteração).
SELECT pg_temp.espera_erro($q$ UPDATE contabil.partida SET valor = 1 WHERE lancamento_id = 100 AND sequencia = 1 $q$,
                           'altere ou exclua pela tela de lançamentos');

-- Caso 9: excluir lançamento gravado fora da função Excluir.
SELECT pg_temp.espera_erro($q$ DELETE FROM contabil.lancamento WHERE id = 100 $q$, 'pela função Excluir');

-- Caso 10: alterar data de lançamento efetivado.
SELECT pg_temp.espera_erro($q$ UPDATE contabil.lancamento SET data_lancamento = '2026-03-12' WHERE id = 100 $q$, 'é imutável');

-- Caso 11: estorno — novo lançamento com D/C invertidos + marcação do original.
BEGIN;
INSERT INTO contabil.lancamento (id, empresa_id, exercicio_id, data_lancamento, origem, estorno_de_id) VALUES (110, 1, 1, '2026-03-20', 'ESTORNO', 100);
INSERT INTO contabil.partida (lancamento_id, sequencia, conta_id, ind_dc, valor, historico)
SELECT 110, sequencia, conta_id, CASE ind_dc WHEN 'D' THEN 'C' ELSE 'D' END::contabil.indicador_dc, valor,
       'Estorno do lançamento nº 1 - ' || historico
  FROM contabil.partida WHERE lancamento_id = 100;
UPDATE contabil.lancamento SET status = 'EFETIVADO', numero = 2, efetivado_em = now() WHERE id = 110;
UPDATE contabil.lancamento SET status = 'ESTORNADO' WHERE id = 100;
COMMIT;

DO $$
DECLARE v_saldo NUMERIC;
BEGIN
    SELECT SUM(CASE ind_dc WHEN 'D' THEN valor ELSE -valor END) INTO v_saldo
      FROM contabil.vw_razao WHERE conta_id = 4;
    IF v_saldo <> 0 THEN RAISE EXCEPTION 'Saldo de Clientes após estorno deveria ser 0, é %', v_saldo; END IF;
    RAISE NOTICE 'OK: estorno zera o saldo e preserva os dois lançamentos no Razão';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
