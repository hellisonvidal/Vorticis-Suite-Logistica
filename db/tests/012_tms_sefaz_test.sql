-- Testes da migração 087: configuração SEFAZ do TMS, colunas IBS/CBS e contingência SVC no CT-e/MDF-e e histórico tms.transmissao_sefaz.
DO $$
DECLARE
    v_emp BIGINT; v_par BIGINT; v_cte BIGINT;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM core.parametro_legal WHERE chave = 'IBS_CBS_TRANSPORTE' AND vigencia_inicio = DATE '2026-01-01') THEN
        RAISE EXCEPTION 'Parâmetro legal IBS_CBS_TRANSPORTE não foi semeado pela migração 087';
    END IF;

    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_frota)
    VALUES ('90000087000100', 'Transportadora SEFAZ LTDA', '2062', '4930202', 'SP', '3550308', '2020-01-01', FALSE, FALSE, FALSE, TRUE)
    RETURNING id INTO v_emp;

    INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (v_emp, 'P1', 'Embarcador Teste') RETURNING id INTO v_par;

    -- tms.configuracao_sefaz: restrições de ambiente, RNTRC e CNPJ da seguradora
    INSERT INTO tms.configuracao_sefaz (empresa_id, ambiente, rntrc, contingencia_svc_automatica, seguradora_nome, seguradora_cnpj, apolice_seguro, averbacao_padrao)
    VALUES (v_emp, 2, '12345678', TRUE, 'Porto Seguro', '61198164000160', 'AP-2026-01', 'AV-2026-01');

    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.configuracao_sefaz SET ambiente = 3 WHERE empresa_id = %s', v_emp), 'configuracao_sefaz_ambiente_check');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.configuracao_sefaz SET rntrc = ''123'' WHERE empresa_id = %s', v_emp), 'configuracao_sefaz_rntrc_check');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.configuracao_sefaz SET seguradora_cnpj = ''123'' WHERE empresa_id = %s', v_emp), 'configuracao_sefaz_seguradora_cnpj_check');

    -- tms.cte: tipo_emissao (1, 7, 8) e valores IBS/CBS
    INSERT INTO tms.cte (empresa_id, numero, emissao, remetente_id, destinatario_id, tomador_id, origem_uf, origem_ibge, origem_municipio,
                         destino_uf, destino_ibge, destino_municipio, valor_total, ibs_uf_pct, ibs_mun_pct, ibs, cbs_pct, cbs, cst_ibs_cbs, class_trib_ibs_cbs)
    VALUES (v_emp, 1, '2026-06-01', v_par, v_par, v_par, 'SP', '3550308', 'São Paulo', 'RJ', '3304557', 'Rio de Janeiro',
            1000.00, 0.10, 0.00, 1.00, 0.90, 9.00, '000', '000001')
    RETURNING id INTO v_cte;

    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.cte SET tipo_emissao = 9 WHERE id = %s', v_cte), 'cte_tipo_emissao_check');
    UPDATE tms.cte SET tipo_emissao = 7, ambiente = 2, xml_autorizado = '<cteProc/>' WHERE id = v_cte;

    -- tms.transmissao_sefaz: modelo (57/58) e acao (AUTORIZACAO/CANCELAMENTO/ENCERRAMENTO)
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO tms.transmissao_sefaz (empresa_id, modelo, documento_id, acao, ambiente, xml_envio) VALUES (%s, ''55'', %s, ''AUTORIZACAO'', 2, ''<x/>'')', v_emp, v_cte), 'transmissao_sefaz_modelo_check');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO tms.transmissao_sefaz (empresa_id, modelo, documento_id, acao, ambiente, xml_envio) VALUES (%s, ''57'', %s, ''OUTRA'', 2, ''<x/>'')', v_emp, v_cte), 'transmissao_sefaz_acao_check');

    INSERT INTO tms.transmissao_sefaz (empresa_id, modelo, documento_id, acao, ambiente, tipo_emissao, contingencia_svc, chave, cstat, motivo, protocolo, xml_envio, xml_retorno)
    VALUES (v_emp, '57', v_cte, 'AUTORIZACAO', 2, 7, TRUE, repeat('3', 44), 100, 'Autorizado o uso do CT-e', '135260000012345', '<CTe/>', '<retCTe/>');

    RAISE NOTICE 'OK: TMS SEFAZ';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
