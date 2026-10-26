-- Testes da migração 070: tipo de contabilização da empresa, lotes e pendências de contabilização, preferências de menu.

DO $$
DECLARE
    v_emp  BIGINT;
    v_usu  BIGINT;
    v_lote BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_frota)
    VALUES ('72145896000112', 'Contabilização em Lote LTDA', '2062', '4930202', 'SP', '3550308', '2020-01-01', TRUE, TRUE, TRUE, TRUE) RETURNING id INTO v_emp;
    INSERT INTO core.usuario (login, nome, senha_hash) VALUES ('lote_teste', 'Lote', 'x') RETURNING id INTO v_usu;

    -- tipo de contabilização: só ONLINE ou LOTE
    INSERT INTO contabil.contabilizacao_config (empresa_id) VALUES (v_emp);
    IF (SELECT modo FROM contabil.contabilizacao_config WHERE empresa_id = v_emp) <> 'ONLINE' THEN RAISE EXCEPTION 'o padrão deveria ser ONLINE'; END IF;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE contabil.contabilizacao_config SET modo = ''OUTRO'' WHERE empresa_id = %s', v_emp), 'ck_contabilizacao_modo');
    UPDATE contabil.contabilizacao_config SET modo = 'LOTE' WHERE empresa_id = v_emp;

    -- pendências: origem e situação válidas; uma aberta por referência
    INSERT INTO contabil.pendencia_contabilizacao (empresa_id, origem, tipo, referencia, periodo, descricao, valor)
    VALUES (v_emp, 'DUPLICATAS', 'BAIXA', '10', '2026-04-12', 'Pagamento da duplicata 1', 425);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO contabil.pendencia_contabilizacao (empresa_id, origem, tipo, referencia, periodo, descricao) VALUES (%s, ''DUPLICATAS'', ''BAIXA'', ''10'', ''2026-04-12'', ''repetida'')', v_emp), 'uq_pendencia_aberta');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO contabil.pendencia_contabilizacao (empresa_id, origem, tipo, referencia, periodo, descricao) VALUES (%s, ''BANCO'', ''X'', ''1'', ''2026-04-12'', ''origem ruim'')', v_emp), 'ck_pendencia_origem');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE contabil.pendencia_contabilizacao SET situacao = ''FEITA'' WHERE empresa_id = %s', v_emp), 'ck_pendencia_situacao');

    -- contabilizada libera a referência para uma nova pendência (depois de desfazer o lote)
    INSERT INTO contabil.lote_contabilizacao (empresa_id, criado_por) VALUES (v_emp, v_usu) RETURNING id INTO v_lote;
    UPDATE contabil.pendencia_contabilizacao SET situacao = 'CONTABILIZADA', lote_id = v_lote WHERE empresa_id = v_emp;
    INSERT INTO contabil.pendencia_contabilizacao (empresa_id, origem, tipo, referencia, periodo, descricao, valor)
    VALUES (v_emp, 'DUPLICATAS', 'BAIXA', '10', '2026-04-12', 'Pagamento da duplicata 1', 425);

    -- preferências de menu: um registro por programa/usuário/item e outro por programa/item para todos
    INSERT INTO core.preferencia_menu (programa, usuario_id, chave) VALUES ('VorticisLogistica', v_usu, 'Relatórios');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO core.preferencia_menu (programa, usuario_id, chave) VALUES (''VorticisLogistica'', %s, ''Relatórios'')', v_usu), 'uq_preferencia_menu_usuario');
    INSERT INTO core.preferencia_menu (programa, usuario_id, chave) VALUES ('VorticisLogistica', NULL, 'Emissão');
    PERFORM pg_temp.espera_erro_dup('INSERT INTO core.preferencia_menu (programa, usuario_id, chave) VALUES (''VorticisLogistica'', NULL, ''Emissão'')', 'uq_preferencia_menu_todos');
    DELETE FROM core.usuario WHERE id = v_usu;   -- as preferências do usuário saem com ele
    IF EXISTS (SELECT 1 FROM core.preferencia_menu WHERE usuario_id = v_usu) THEN RAISE EXCEPTION 'preferência ficou após excluir o usuário'; END IF;
    DELETE FROM core.preferencia_menu WHERE programa = 'VorticisLogistica';

    -- tudo sai junto com a empresa
    PERFORM core.fn_excluir_empresa(v_emp);
    IF EXISTS (SELECT 1 FROM contabil.pendencia_contabilizacao WHERE empresa_id = v_emp) OR EXISTS (SELECT 1 FROM contabil.lote_contabilizacao WHERE empresa_id = v_emp)
       OR EXISTS (SELECT 1 FROM contabil.contabilizacao_config WHERE empresa_id = v_emp)
    THEN RAISE EXCEPTION 'contabilização em lote ficou após excluir a empresa'; END IF;
    RAISE NOTICE 'OK: contabilização em lote (tipo, lotes, pendências, preferências de menu e exclusão da empresa)';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
