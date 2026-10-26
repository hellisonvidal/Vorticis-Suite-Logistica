-- Regras fiscais (080): de/para de CFOP por operação; o destino segue a operação (entrada 1/2/3, saída 5/6/7); logo da empresa (079) limitado.
DO $$
DECLARE
    v_emp BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ, modulo_frota)
    VALUES ('90000003000100', 'Regras Ltda', '2062', '4930202', 'SP', '3550308', '2020-01-01', TRUE) RETURNING id INTO v_emp;
    INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino) VALUES (v_emp, 1, 'Compra', 'E', '5102', '1102');
    INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino, cst_icms) VALUES (v_emp, 2, 'Venda ST', 'S', '5102', '5405', '060');
    PERFORM pg_temp.espera_erro_dup(format($q$INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino) VALUES (%s, 3, 'x', 'E', '5102', '2102')$q$, v_emp), 'uq_regra_fiscal_cfop');
    PERFORM pg_temp.espera_erro_dup(format($q$INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino) VALUES (%s, 4, 'x', 'S', '6102', '1102')$q$, v_emp), 'ck_regra_fiscal_destino');
    PERFORM pg_temp.espera_erro_dup(format($q$INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino) VALUES (%s, 5, 'x', 'E', '6102', '5102')$q$, v_emp), 'ck_regra_fiscal_destino');
    PERFORM pg_temp.espera_erro_dup(format($q$UPDATE core.empresa SET logo = '\x00'::bytea WHERE id = %s$q$, v_emp), 'ck_empresa_logo_tamanho');
    RAISE NOTICE 'OK: regras fiscais e logo';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
