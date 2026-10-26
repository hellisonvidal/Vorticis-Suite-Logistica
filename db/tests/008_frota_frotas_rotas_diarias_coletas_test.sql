-- Testes da migração 071: frotas (vínculo com veículo e centro de custo), rotas, diárias e coletas.
DO $$
DECLARE
    v_emp BIGINT; v_fro BIGINT; v_cc BIGINT; v_vei BIGINT; v_mot BIGINT; v_rot BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_frota)
    VALUES ('19131243000197', 'Transportadora Rotas LTDA', '2062', '4930202', 'SP', '3550308', '2020-01-01', FALSE, FALSE, FALSE, TRUE) RETURNING id INTO v_emp;
    INSERT INTO frota.frota (empresa_id, codigo, descricao, tipo) VALUES (v_emp, 1, 'Frota própria', 'PROPRIA') RETURNING id INTO v_fro;
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.frota (empresa_id, codigo, descricao, tipo) VALUES (%s, 2, ''X'', ''NAVIO'')', v_emp), 'ck_frota_tipo');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.frota (empresa_id, codigo, descricao) VALUES (%s, 1, ''Dup'')', v_emp), 'uq_frota_codigo');
    INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo, frota_id) VALUES (v_emp, 1, 'ROT1A23', 'CAVALO', v_fro) RETURNING id INTO v_vei;
    INSERT INTO contabil.centro_custo (empresa_id, codigo, descricao, data_inclusao, nivel, tipo, frota_id) VALUES (v_emp, '1', 'Frota própria', '2026-01-01', 1, 'A', v_fro)
        RETURNING id INTO v_cc;
    INSERT INTO frota.motorista (empresa_id, codigo, nome, cpf) VALUES (v_emp, 1, 'João', '52998224725') RETURNING id INTO v_mot;

    INSERT INTO frota.rota (empresa_id, codigo, descricao, distancia_km) VALUES (v_emp, 1, 'SP - RJ', 430) RETURNING id INTO v_rot;
    INSERT INTO frota.rota_parada VALUES (v_rot, 0, 'SP', '3550308', 'São Paulo'), (v_rot, 1, 'RJ', '3304557', 'Rio de Janeiro');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.rota_parada VALUES (%s, 2, ''RJ'', ''12'', ''X'')', v_rot), 'rota_parada_municipio_ibge_check');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.rota SET distancia_origem = ''VOANDO'' WHERE id = %s', v_rot), 'ck_rota_dist_origem');

    INSERT INTO frota.diaria (empresa_id, numero, data, motorista_id, veiculo_id, frota_id, rota_id, tipo, quantidade, valor_unitario, centro_custo_id)
    VALUES (v_emp, 1, '2026-03-02', v_mot, v_vei, v_fro, v_rot, 'PERNOITE', 2, 120, v_cc);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.diaria (empresa_id, numero, data, motorista_id, tipo, quantidade, valor_unitario) VALUES (%s, 2, ''2026-03-02'', %s, ''PERNOITE'', 0, 10)', v_emp, v_mot), 'diaria_quantidade_check');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.diaria SET situacao = ''PAGO'' WHERE empresa_id = %s', v_emp), 'ck_diaria_situacao');
    INSERT INTO frota.coleta (empresa_id, numero, agendada_em, endereco_coleta, rota_id) VALUES (v_emp, 1, '2026-03-02 08:00', 'Rua A, 10', v_rot);
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.coleta SET situacao = ''PERDIDA'' WHERE empresa_id = %s', v_emp), 'ck_coleta_situacao');

    -- excluir a frota solta os vínculos
    DELETE FROM frota.frota WHERE id = v_fro;
    IF (SELECT frota_id FROM frota.veiculo WHERE id = v_vei) IS NOT NULL OR (SELECT frota_id FROM contabil.centro_custo WHERE id = v_cc) IS NOT NULL
    THEN RAISE EXCEPTION 'vínculo com a frota não foi solto'; END IF;
    DELETE FROM frota.diaria WHERE empresa_id = v_emp;
    RAISE NOTICE 'OK: frotas, rotas, diárias e coletas';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
