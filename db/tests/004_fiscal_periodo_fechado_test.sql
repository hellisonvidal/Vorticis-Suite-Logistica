-- Testes da migração 065: fechamento do período fiscal bloqueia incluir, alterar e excluir notas (e itens) do mês fechado,
-- mas deixa marcar a contabilização e excluir a empresa em cascata.

DO $$
DECLARE
    v_emp BIGINT;
    v_doc BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ)
    VALUES ('19131243000197', 'Empresa Fechamento LTDA', '2062', '6920601', 'SP', '3550308', '2020-01-01') RETURNING id INTO v_emp;
    INSERT INTO fiscal.documento (empresa_id, modelo, chave_acesso, tipo_operacao, numero, data_emissao, data_entrada_saida, valor_total)
    VALUES (v_emp, '55', repeat('5', 44), 'E', '501', '2026-04-10', '2026-04-10', 100) RETURNING id INTO v_doc;
    INSERT INTO fiscal.documento_item (documento_id, n_item, cfop) VALUES (v_doc, 1, '1102');

    INSERT INTO fiscal.periodo_fechado (empresa_id, competencia) VALUES (v_emp, '2026-04-01');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE fiscal.documento SET valor_total = 200 WHERE id = %s', v_doc), 'está fechado');
    PERFORM pg_temp.espera_erro_dup(format('DELETE FROM fiscal.documento WHERE id = %s', v_doc), 'está fechado');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE fiscal.documento_item SET cfop = ''1101'' WHERE documento_id = %s', v_doc), 'está fechado');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.documento (empresa_id, modelo, tipo_operacao, numero, data_emissao, data_entrada_saida) '
                                           'VALUES (%s, ''55'', ''S'', ''502'', ''2026-04-15'', ''2026-04-15'')', v_emp), 'está fechado');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE fiscal.documento SET data_entrada_saida = ''2026-04-20'' WHERE id = %s', v_doc), 'está fechado');
    -- outro mês continua livre; a marcação de contabilização é permitida no mês fechado
    INSERT INTO fiscal.documento (empresa_id, modelo, chave_acesso, tipo_operacao, numero, data_emissao, data_entrada_saida) VALUES (v_emp, '55', repeat('6', 44), 'S', '503', '2026-05-02', '2026-05-02');
    UPDATE fiscal.documento SET contabilizado_em = now() WHERE id = v_doc;
    -- reaberto, volta a aceitar
    DELETE FROM fiscal.periodo_fechado WHERE empresa_id = v_emp;
    UPDATE fiscal.documento SET valor_total = 150 WHERE id = v_doc;
    -- fechado de novo, a exclusão da empresa (cascata) não é bloqueada
    INSERT INTO fiscal.periodo_fechado (empresa_id, competencia) VALUES (v_emp, '2026-04-01');
    PERFORM core.fn_excluir_empresa(v_emp);
    RAISE NOTICE 'OK: período fiscal fechado bloqueia notas e itens do mês e deixa contabilizar e excluir a empresa';
END $$;

DO $$
DECLARE
    v_emp  BIGINT;
    v_part BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ)
    VALUES ('45723174000110', 'Empresa SPED LTDA', '2062', '6920601', 'SP', '3550308', '2020-01-01') RETURNING id INTO v_emp;
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.efd_ajuste_icms (empresa_id, competencia, codigo, valor) VALUES (%s, ''2026-03-01'', ''SP090001'', 10)', v_emp), 'efd_ajuste_icms_codigo_check');
    INSERT INTO fiscal.efd_ajuste_icms (empresa_id, competencia, codigo, valor) VALUES (v_emp, '2026-03-01', 'SP020001', 10);
    INSERT INTO core.participante (empresa_id, codigo, nome, fornecedor) VALUES (v_emp, 'EN', 'Energia', TRUE) RETURNING id INTO v_part;
    INSERT INTO fiscal.conta_consumo (empresa_id, modelo, participante_id, numero, data_emissao, data_entrada, valor, cfop) VALUES (v_emp, '06', v_part, '1', '2026-03-01', '2026-03-02', 100, '1253');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.conta_consumo (empresa_id, modelo, participante_id, numero, data_emissao, data_entrada, valor, cfop) VALUES (%s, ''06'', %s, ''1'', ''2026-03-01'', ''2026-03-02'', 100, ''1253'')', v_emp, v_part), 'uq_conta_consumo');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO fiscal.conta_consumo (empresa_id, modelo, participante_id, numero, data_emissao, data_entrada, valor, cfop) VALUES (%s, ''55'', %s, ''2'', ''2026-03-01'', ''2026-03-02'', 100, ''1253'')', v_emp, v_part), 'conta_consumo_modelo_check');
    -- a empresa com contas de consumo pode ser excluída (as contas saem antes dos participantes)
    PERFORM core.fn_excluir_empresa(v_emp);
    RAISE NOTICE 'OK: outros lançamentos do SPED Fiscal (ajuste com código válido, conta de consumo única) e exclusão da empresa';
END $$;

