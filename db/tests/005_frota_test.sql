-- Testes da migração 068 (módulo Frota, piloto): empresa só da Frota, placa e RENAVAM no formato, viagem concluída exige chegada,
-- um documento pertence a um veículo OU a um motorista, e a exclusão da empresa leva todo o cadastro da frota junto.

DO $$
DECLARE
    v_emp BIGINT;
    v_vei BIGINT;
    v_mot BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_frota)
    VALUES ('63810427000175', 'Transportadora Teste LTDA', '2062', '4930202', 'SP', '3550308', '2020-01-01', FALSE, FALSE, FALSE, TRUE) RETURNING id INTO v_emp;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE core.empresa SET modulo_frota = FALSE WHERE id = %s', v_emp), 'ck_empresa_algum_modulo');

    INSERT INTO frota.veiculo (empresa_id, codigo, placa, renavam, tipo) VALUES (v_emp, 1, 'ABC1D23', '00123456789', 'CAVALO') RETURNING id INTO v_vei;
    INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo) VALUES (v_emp, 2, 'XYZ1234', 'SEMIRREBOQUE');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo) VALUES (%s, 3, ''AB12345'', ''TRUCK'')', v_emp), 'veiculo_placa_check');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo) VALUES (%s, 4, ''ABC1D23'', ''TRUCK'')', v_emp), 'uq_veiculo_placa');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo) VALUES (%s, 5, ''DEF5678'', ''NAVIO'')', v_emp), 'ck_veiculo_tipo');

    INSERT INTO frota.motorista (empresa_id, codigo, nome, cpf, cnh, cnh_categoria, cnh_validade)
    VALUES (v_emp, 1, 'João Motorista', '52998224725', '12345678901', 'E', '2027-05-10') RETURNING id INTO v_mot;
    INSERT INTO frota.abastecimento (empresa_id, veiculo_id, motorista_id, data, km, litros, valor_total, combustivel) VALUES (v_emp, v_vei, v_mot, '2026-03-01', 1000, 300, 1800, 'DIESEL_S10');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.abastecimento (empresa_id, veiculo_id, data, km, litros, valor_total, combustivel) VALUES (%s, %s, ''2026-03-02'', 1500, 0, 10, ''DIESEL'')', v_emp, v_vei), 'abastecimento_litros_check');

    INSERT INTO frota.viagem (empresa_id, numero, veiculo_id, motorista_id, origem, destino, saida, km_saida, valor_frete) VALUES (v_emp, 1, v_vei, v_mot, 'São Paulo/SP', 'Curitiba/PR', '2026-03-02', 1000, 5000);
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.viagem SET situacao = ''CONCLUIDA'' WHERE empresa_id = %s', v_emp), 'ck_viagem_concluida');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.viagem SET chegada = ''2026-03-03'', km_chegada = 900 WHERE empresa_id = %s', v_emp), 'ck_viagem_km');
    UPDATE frota.viagem SET chegada = '2026-03-03', km_chegada = 1420, situacao = 'CONCLUIDA' WHERE empresa_id = v_emp;

    INSERT INTO frota.plano_manutencao (empresa_id, veiculo_id, descricao, intervalo_km, ultima_km) VALUES (v_emp, v_vei, 'Troca de óleo', 20000, 1000);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.plano_manutencao (empresa_id, veiculo_id, descricao) VALUES (%s, %s, ''Sem intervalo'')', v_emp, v_vei), 'ck_plano_intervalo');
    INSERT INTO frota.ordem_servico (empresa_id, numero, veiculo_id, tipo, data, descricao, valor_pecas) VALUES (v_emp, 1, v_vei, 'CORRETIVA', '2026-03-05', 'Troca de lona de freio', 800);

    INSERT INTO frota.documento (empresa_id, veiculo_id, tipo, vencimento) VALUES (v_emp, v_vei, 'LICENCIAMENTO', '2026-09-30');
    INSERT INTO frota.documento (empresa_id, tipo, vencimento) VALUES (v_emp, 'ANTT_RNTRC', '2027-01-31');   -- da empresa
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.documento (empresa_id, veiculo_id, motorista_id, tipo, vencimento) VALUES (%s, %s, %s, ''OUTRO'', ''2026-12-31'')', v_emp, v_vei, v_mot), 'ck_documento_dono');

    -- exclusão da empresa leva a frota junto
    PERFORM core.fn_excluir_empresa(v_emp);
    IF EXISTS (SELECT 1 FROM frota.veiculo WHERE empresa_id = v_emp) THEN RAISE EXCEPTION 'veículos ficaram após excluir a empresa'; END IF;
    RAISE NOTICE 'OK: módulo Frota (empresa só da Frota, placa, viagem concluída, plano de manutenção, documentos e exclusão da empresa)';
END $$;

