-- =============================================================================
-- Vorticis Contábil — Migração 018: novo nome do sistema
--   O sistema passou a se chamar Vorticis. As variáveis de sessão usadas pelas funções passam de
--   vorticis.* para vorticis.* (a aplicação grava vorticis.usuario). As migrações anteriores não se
--   editam (o migrador confere o hash de cada uma), por isso as funções são redefinidas aqui.
-- =============================================================================

-- Auditoria: login do usuário informado pela aplicação (SET LOCAL vorticis.usuario).
CREATE OR REPLACE FUNCTION core.fn_auditoria()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_antes    JSONB := CASE WHEN TG_OP <> 'INSERT' THEN to_jsonb(OLD) END;
    v_depois   JSONB := CASE WHEN TG_OP <> 'DELETE' THEN to_jsonb(NEW) END;
    v_registro JSONB := COALESCE(v_depois, v_antes);
    v_empresa  BIGINT;
    v_usuario  TEXT  := COALESCE(NULLIF(current_setting('vorticis.usuario', true), ''), current_user);
    v_agora    TIMESTAMPTZ := clock_timestamp();
    v_anterior CHAR(64);
BEGIN
    IF TG_OP = 'UPDATE' AND v_antes = v_depois THEN
        RETURN NULL;
    END IF;

    v_empresa := CASE
        WHEN TG_TABLE_NAME = 'empresa' THEN (v_registro->>'id')::bigint
        WHEN TG_TABLE_NAME = 'periodo' THEN (SELECT e.empresa_id FROM contabil.exercicio e WHERE e.id = (v_registro->>'exercicio_id')::bigint)
        ELSE (v_registro->>'empresa_id')::bigint
    END;

    -- Efetivação/estorno: o snapshot inclui as partidas, para que o hash cubra o conteúdo contábil.
    IF TG_TABLE_NAME = 'lancamento' AND TG_OP = 'UPDATE' THEN
        v_depois := v_depois || jsonb_build_object('partidas',
            (SELECT jsonb_agg(to_jsonb(p) - 'id' ORDER BY p.sequencia) FROM contabil.partida p WHERE p.lancamento_id = NEW.id));
    END IF;

    PERFORM pg_advisory_xact_lock(hashtext('core.auditoria'));   -- serializa a cadeia
    SELECT hash_registro INTO v_anterior FROM core.auditoria ORDER BY id DESC LIMIT 1;

    INSERT INTO core.auditoria (empresa_id, usuario_login, ocorrido_em, tabela, registro_id, operacao,
                                dados_antes, dados_depois, hash_anterior, hash_registro)
    VALUES (v_empresa, v_usuario, v_agora, TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME, (v_registro->>'id')::bigint, TG_OP,
            v_antes, v_depois, v_anterior,
            core.fn_hash_auditoria(v_anterior, v_agora, v_usuario, TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME,
                                   (v_registro->>'id')::bigint, TG_OP, v_antes, v_depois));
    RETURN NULL;
END $$;

-- Empresa sendo excluída nesta transação (definida só por core.fn_excluir_empresa).
CREATE OR REPLACE FUNCTION core.excluindo_empresa(p_empresa BIGINT)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    SELECT COALESCE(current_setting('vorticis.excluindo_empresa', true), '') = p_empresa::text
$$;

-- Exclusão da empresa (mesma regra da migração 014).
CREATE OR REPLACE FUNCTION core.fn_excluir_empresa(p_empresa BIGINT)
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE
    v_lancamentos INTEGER;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM core.empresa WHERE id = p_empresa) THEN
        RAISE EXCEPTION 'Empresa % não encontrada', p_empresa;
    END IF;
    PERFORM set_config('vorticis.excluindo_empresa', p_empresa::text, true);

    SELECT count(*) INTO v_lancamentos FROM contabil.lancamento WHERE empresa_id = p_empresa;
    DELETE FROM contabil.conciliacao WHERE empresa_id = p_empresa;
    DELETE FROM imobilizado.depreciacao WHERE bem_id IN (SELECT id FROM imobilizado.bem WHERE empresa_id = p_empresa);
    DELETE FROM imobilizado.bem WHERE empresa_id = p_empresa;
    DELETE FROM contabil.mapeamento_referencial WHERE empresa_id = p_empresa;
    DELETE FROM contabil.partida WHERE lancamento_id IN (SELECT id FROM contabil.lancamento WHERE empresa_id = p_empresa);
    DELETE FROM contabil.lancamento WHERE empresa_id = p_empresa;
    DELETE FROM contabil.saldo_mensal WHERE empresa_id = p_empresa;
    DELETE FROM contabil.historico_padrao WHERE empresa_id = p_empresa;
    DELETE FROM contabil.plano_contas WHERE empresa_id = p_empresa;
    DELETE FROM contabil.centro_custo WHERE empresa_id = p_empresa;
    DELETE FROM contabil.periodo WHERE exercicio_id IN (SELECT id FROM contabil.exercicio WHERE empresa_id = p_empresa);
    DELETE FROM contabil.exercicio WHERE empresa_id = p_empresa;
    DELETE FROM core.participante WHERE empresa_id = p_empresa;
    DELETE FROM core.socio WHERE empresa_id = p_empresa;
    DELETE FROM core.empresa_contabilidade WHERE empresa_id = p_empresa;
    DELETE FROM core.usuario_empresa WHERE empresa_id = p_empresa;
    DELETE FROM core.empresa_regime WHERE empresa_id = p_empresa;
    DELETE FROM core.estabelecimento WHERE empresa_id = p_empresa;
    DELETE FROM core.empresa WHERE id = p_empresa;

    PERFORM set_config('vorticis.excluindo_empresa', '', true);
    RETURN v_lancamentos;
END $$;

-- Código reduzido (mesma regra da migração 012).
CREATE OR REPLACE FUNCTION contabil.fn_codigo_reduzido()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.tipo = 'S' THEN
        NEW.codigo_reduzido := NULL;
    ELSIF NEW.codigo_reduzido IS NULL THEN
        PERFORM pg_advisory_xact_lock(hashtext('vorticis_reduzido'), NEW.empresa_id::int);
        SELECT COALESCE(MAX(codigo_reduzido), 0) + 1 INTO NEW.codigo_reduzido
          FROM contabil.plano_contas WHERE empresa_id = NEW.empresa_id;
    END IF;
    RETURN NEW;
END $$;
