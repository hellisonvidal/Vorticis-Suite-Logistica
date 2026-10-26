-- Testes da migração 073: TMS (tabelas de frete, cotações, CT-e, manifestos, ocorrências e faturas de frete).
DO $$
DECLARE
    v_emp BIGINT; v_par BIGINT; v_tab BIGINT; v_cot BIGINT; v_cte BIGINT; v_vei BIGINT; v_mot BIGINT; v_man BIGINT; v_fat BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_frota)
    VALUES ('11444777000161', 'Transportadora TMS LTDA', '2062', '4930202', 'SP', '3550308', '2020-01-01', FALSE, FALSE, FALSE, TRUE) RETURNING id INTO v_emp;
    INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (v_emp, 'C1', 'Cliente') RETURNING id INTO v_par;
    INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo) VALUES (v_emp, 1, 'TMS1A23', 'CAVALO') RETURNING id INTO v_vei;
    INSERT INTO frota.motorista (empresa_id, codigo, nome, cpf) VALUES (v_emp, 1, 'João', '52998224725') RETURNING id INTO v_mot;

    INSERT INTO tms.tabela_frete (empresa_id, codigo, descricao, valor_kg) VALUES (v_emp, 1, 'Geral', 0.5) RETURNING id INTO v_tab;
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO tms.tabela_frete (empresa_id, codigo, descricao) VALUES (%s, 1, ''Dup'')', v_emp), 'uq_tabela_frete_codigo');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.tabela_frete SET gris_pct = 120 WHERE id = %s', v_tab), 'tabela_frete_gris_pct_check');

    INSERT INTO tms.cotacao (empresa_id, numero, data, validade, cliente_id, tabela_id, origem, destino)
    VALUES (v_emp, 1, '2026-03-01', '2026-03-15', v_par, v_tab, 'A', 'B') RETURNING id INTO v_cot;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.cotacao SET validade = ''2026-02-01'' WHERE id = %s', v_cot), 'ck_cotacao_validade');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.cotacao SET situacao = ''PERDIDA'' WHERE id = %s', v_cot), 'ck_cotacao_situacao');

    INSERT INTO tms.cte (empresa_id, numero, emissao, remetente_id, destinatario_id, tomador_id, origem_uf, origem_ibge, origem_municipio,
                         destino_uf, destino_ibge, destino_municipio, valor_total)
    VALUES (v_emp, 1, '2026-03-02', v_par, v_par, v_par, 'SP', '3509502', 'Campinas', 'RJ', '3304557', 'Rio de Janeiro', 100) RETURNING id INTO v_cte;
    -- emitido exige chave; chave só com 44 dígitos; entregue exige data
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.cte SET situacao = ''EMITIDO'' WHERE id = %s', v_cte), 'ck_cte_emitido_tem_chave');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.cte SET chave = ''123'' WHERE id = %s', v_cte), 'ck_cte_chave');
    UPDATE tms.cte SET situacao = 'EMITIDO', chave = repeat('1', 44) WHERE id = v_cte;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.cte SET situacao = ''ENTREGUE'' WHERE id = %s', v_cte), 'ck_cte_entregue');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO tms.cte (empresa_id, numero, emissao, remetente_id, destinatario_id, tomador_id, origem_uf, origem_ibge, origem_municipio, destino_uf, destino_ibge, destino_municipio) VALUES (%s, 1, ''2026-03-02'', %s, %s, %s, ''SP'', ''3509502'', ''C'', ''RJ'', ''3304557'', ''R'')', v_emp, v_par, v_par, v_par), 'uq_cte_numero');

    INSERT INTO tms.manifesto (empresa_id, numero, emissao, uf_inicio, uf_fim, veiculo_id, motorista_id) VALUES (v_emp, 1, '2026-03-02', 'SP', 'RJ', v_vei, v_mot) RETURNING id INTO v_man;
    INSERT INTO tms.manifesto_cte VALUES (v_man, v_cte);
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.manifesto SET situacao = ''ENCERRADO'' WHERE id = %s', v_man), 'ck_manifesto_encerrado');
    INSERT INTO tms.ocorrencia (cte_id, data_hora, tipo) VALUES (v_cte, '2026-03-03 10:00', 'SAIDA');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO tms.ocorrencia (cte_id, data_hora, tipo) VALUES (%s, now(), ''SUMIU'')', v_cte), 'ck_ocorrencia_tipo');

    INSERT INTO tms.fatura (empresa_id, numero, emissao, vencimento, cliente_id, valor_total) VALUES (v_emp, 1, '2026-03-31', '2026-04-30', v_par, 100) RETURNING id INTO v_fat;
    UPDATE tms.cte SET fatura_id = v_fat WHERE id = v_cte;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE tms.fatura SET vencimento = ''2026-03-01'' WHERE id = %s', v_fat), 'ck_fatura_vencimento');
    -- excluir a fatura solta o CT-e; excluir o manifesto leva os vínculos
    DELETE FROM tms.fatura WHERE id = v_fat;
    IF (SELECT fatura_id FROM tms.cte WHERE id = v_cte) IS NOT NULL THEN RAISE EXCEPTION 'CT-e continuou preso à fatura excluída'; END IF;
    DELETE FROM tms.manifesto WHERE id = v_man;
    IF EXISTS (SELECT 1 FROM tms.manifesto_cte WHERE manifesto_id = v_man) THEN RAISE EXCEPTION 'vínculo do manifesto ficou'; END IF;
    -- a função FROTA_TMS foi dada a quem já tinha FROTA_OPERACAO
    IF EXISTS (SELECT 1 FROM core.perfil_permissao a WHERE a.permissao = 'FROTA_OPERACAO'
                AND NOT EXISTS (SELECT 1 FROM core.perfil_permissao b WHERE b.perfil_id = a.perfil_id AND b.permissao = 'FROTA_TMS')) THEN
        RAISE EXCEPTION 'perfil com FROTA_OPERACAO sem FROTA_TMS';
    END IF;
    RAISE NOTICE 'OK: TMS';
END $$;

-- 074: perfil padrão Manutenção com a manutenção da frota, sem autorizar serviços
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM core.perfil p JOIN core.perfil_permissao pp ON pp.perfil_id = p.id
                    WHERE p.nome = 'Manutenção' AND p.padrao AND pp.permissao = 'FROTA_MANUTENCAO') THEN
        RAISE EXCEPTION 'perfil Manutenção sem FROTA_MANUTENCAO';
    END IF;
    IF EXISTS (SELECT 1 FROM core.perfil p JOIN core.perfil_permissao pp ON pp.perfil_id = p.id WHERE p.nome = 'Manutenção' AND pp.permissao = 'FROTA_APROVAR') THEN
        RAISE EXCEPTION 'perfil Manutenção não pode autorizar serviços';
    END IF;
    RAISE NOTICE 'OK: perfil Manutenção';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
