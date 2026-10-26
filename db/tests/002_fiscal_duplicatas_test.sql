-- Testes da migração 053 (duplicatas). Executar depois das migrações, em banco descartável:
--   cat db/0*.sql db/tests/*.sql | psql -d vorticis_sql -v ON_ERROR_STOP=1 -q
-- Cobre saldo e situação mantidos pelas baixas, cancelamento da nota e proteção contra exclusão de nota com baixa.

CREATE FUNCTION pg_temp.espera_erro_dup(sql TEXT, trecho TEXT) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    BEGIN
        EXECUTE sql;
    EXCEPTION WHEN OTHERS THEN
        IF position(trecho IN SQLERRM) = 0 THEN
            RAISE EXCEPTION 'Erro inesperado. Esperava "%", obtive "%"', trecho, SQLERRM;
        END IF;
        RAISE NOTICE 'OK (rejeitado): %', SQLERRM;
        RETURN;
    END;
    RAISE EXCEPTION 'Deveria ter falhado: %', trecho;
END $$;

DO $$
DECLARE
    v_emp  BIGINT;
    v_doc  BIGINT;
    v_doc2 BIGINT;
    v_d1   BIGINT;
    v_d2   BIGINT;
    v_d3   BIGINT;
    v_b    BIGINT;
    r      RECORD;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ)
    VALUES ('45997418000153', 'Empresa Duplicatas LTDA', '2062', '6920601', 'SP', '3550308', '2020-01-01') RETURNING id INTO v_emp;

    INSERT INTO fiscal.documento (empresa_id, modelo, chave_acesso, tipo_operacao, numero, data_emissao, data_entrada_saida, valor_total)
    VALUES (v_emp, '55', repeat('7', 44), 'E', '1001', '2026-03-10', '2026-03-10', 1000) RETURNING id INTO v_doc;
    INSERT INTO fiscal.documento (empresa_id, modelo, chave_acesso, tipo_operacao, numero, data_emissao, data_entrada_saida, valor_total)
    VALUES (v_emp, '55', repeat('8', 44), 'E', '1002', '2026-03-10', '2026-03-10', 500) RETURNING id INTO v_doc2;

    INSERT INTO fiscal.duplicata (empresa_id, documento_id, tipo, parcela, numero_documento, emissao, vencimento, valor, saldo, origem)
    VALUES (v_emp, v_doc, 'PAGAR', '001', '1001', '2026-03-10', '2026-04-10', 400, 0, 'NOTA') RETURNING id INTO v_d1;
    INSERT INTO fiscal.duplicata (empresa_id, documento_id, tipo, parcela, numero_documento, emissao, vencimento, valor, origem)
    VALUES (v_emp, v_doc, 'PAGAR', '002', '1001', '2026-03-10', '2026-05-10', 600, 'NOTA') RETURNING id INTO v_d2;
    INSERT INTO fiscal.duplicata (empresa_id, documento_id, tipo, parcela, numero_documento, emissao, vencimento, valor, origem)
    VALUES (v_emp, v_doc2, 'PAGAR', '001', '1002', '2026-03-10', '2026-04-10', 500, 'NOTA') RETURNING id INTO v_d3;

    -- o saldo nasce igual ao valor, qualquer que seja o informado
    IF (SELECT saldo FROM fiscal.duplicata WHERE id = v_d1) <> 400 THEN RAISE EXCEPTION 'Saldo inicial deveria ser o valor'; END IF;
    RAISE NOTICE 'OK: saldo inicial = valor';

    -- baixa parcial: juros/multa/desconto acumulam, o saldo cai só pelo principal
    INSERT INTO fiscal.duplicata_baixa (duplicata_id, data, valor_principal, juros, multa, desconto) VALUES (v_d1, '2026-04-12', 100, 5, 2, 1);
    SELECT * INTO r FROM fiscal.duplicata WHERE id = v_d1;
    IF (r.saldo, r.situacao, r.juros, r.multa, r.desconto) IS DISTINCT FROM (300::numeric, 'PARCIAL', 5::numeric, 2::numeric, 1::numeric) THEN
        RAISE EXCEPTION 'Baixa parcial: saldo %, situação %', r.saldo, r.situacao;
    END IF;
    RAISE NOTICE 'OK: baixa parcial';

    -- baixa do restante liquida; estorno (exclusão da baixa) reabre
    INSERT INTO fiscal.duplicata_baixa (duplicata_id, data, valor_principal) VALUES (v_d1, '2026-04-20', 300) RETURNING id INTO v_b;
    IF (SELECT situacao FROM fiscal.duplicata WHERE id = v_d1) <> 'LIQUIDADA' OR (SELECT saldo FROM fiscal.duplicata WHERE id = v_d1) <> 0 THEN
        RAISE EXCEPTION 'Deveria estar liquidada';
    END IF;
    DELETE FROM fiscal.duplicata_baixa WHERE id = v_b;
    IF (SELECT situacao FROM fiscal.duplicata WHERE id = v_d1) <> 'PARCIAL' OR (SELECT saldo FROM fiscal.duplicata WHERE id = v_d1) <> 300 THEN
        RAISE EXCEPTION 'Estorno deveria devolver o saldo';
    END IF;
    RAISE NOTICE 'OK: liquidação e estorno';

    -- baixa acima do valor, em duplicata cancelada, e alteração de valor com baixa são recusadas
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.duplicata_baixa (duplicata_id, data, valor_principal) VALUES (%s, ''2026-04-20'', 301)', v_d1), 'superam o valor');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE fiscal.duplicata SET valor = 999 WHERE id = %s', v_d1), 'já tem baixa');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE fiscal.duplicata SET situacao = ''CANCELADA'' WHERE id = %s', v_d1), 'tem baixa');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.duplicata (empresa_id, tipo, parcela, numero_documento, emissao, vencimento, valor) VALUES (%s, ''PAGAR'', ''1'', ''X'', ''2026-03-10'', ''2026-03-01'', 10)', v_emp), 'ck_duplicata_vencimento');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.duplicata (empresa_id, documento_id, tipo, parcela, numero_documento, emissao, vencimento, valor) VALUES (%s, %s, ''PAGAR'', ''001'', ''1001'', ''2026-03-10'', ''2026-04-10'', 10)', v_emp, v_doc), 'uq_duplicata_parcela');

    -- alterar o valor sem baixas atualiza o saldo
    UPDATE fiscal.duplicata SET valor = 650 WHERE id = v_d2;
    IF (SELECT saldo FROM fiscal.duplicata WHERE id = v_d2) <> 650 THEN RAISE EXCEPTION 'Saldo deveria seguir o valor'; END IF;

    -- nota cancelada: cancela só as duplicatas abertas (sem baixa)
    UPDATE fiscal.documento SET situacao = 'CANCELADA' WHERE id = v_doc;
    IF (SELECT situacao FROM fiscal.duplicata WHERE id = v_d2) <> 'CANCELADA' THEN RAISE EXCEPTION 'Duplicata aberta deveria ser cancelada com a nota'; END IF;
    IF (SELECT situacao FROM fiscal.duplicata WHERE id = v_d1) <> 'PARCIAL' THEN RAISE EXCEPTION 'Duplicata com baixa não pode ser cancelada pela nota'; END IF;
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.duplicata_baixa (duplicata_id, data, valor_principal) VALUES (%s, ''2026-05-20'', 10)', v_d2), 'cancelada');
    RAISE NOTICE 'OK: nota cancelada cancela duplicatas abertas';

    -- excluir nota com duplicata baixada é bloqueado; sem baixa, a exclusão leva as duplicatas
    PERFORM pg_temp.espera_erro_dup(format('DELETE FROM fiscal.documento WHERE id = %s', v_doc), 'tem duplicata baixada');
    DELETE FROM fiscal.documento WHERE id = v_doc2;
    IF EXISTS (SELECT 1 FROM fiscal.duplicata WHERE id = v_d3) THEN RAISE EXCEPTION 'Duplicata deveria sumir com a nota'; END IF;
    RAISE NOTICE 'OK: exclusão da nota protegida pela baixa';
END $$;

