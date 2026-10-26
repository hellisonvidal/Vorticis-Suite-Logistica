-- Testes da migração 078: matriz e filiais.
DO $$
DECLARE
    v_matriz BIGINT; v_filial BIGINT; v_outra BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ, modulo_frota)
    VALUES ('90000001000100', 'Transportes Matriz', '2062', '4930202', 'SP', '3550308', '2020-01-01', TRUE) RETURNING id INTO v_matriz;
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ, modulo_frota, matriz_id)
    VALUES ('90000001000291', 'Transportes Filial', '2062', '4930202', 'RJ', '3304557', '2020-01-01', TRUE, v_matriz) RETURNING id INTO v_filial;
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ, modulo_frota)
    VALUES ('90000002000100', 'Outra Ltda', '2062', '4930202', 'SP', '3550308', '2020-01-01', TRUE) RETURNING id INTO v_outra;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE core.empresa SET matriz_id = %s WHERE id = %s', v_matriz, v_outra), 'raiz de CNPJ');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE core.empresa SET matriz_id = %s WHERE id = %s', v_filial, v_matriz), 'já é filial');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE core.empresa SET matriz_id = id WHERE id = %s', v_outra), 'ck_empresa_matriz_propria');
    RAISE NOTICE 'OK: matriz e filiais';
END $$;

