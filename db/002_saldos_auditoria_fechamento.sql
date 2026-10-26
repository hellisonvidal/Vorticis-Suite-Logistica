-- =============================================================================
-- Perseus — Migração 002
--   * CNPJ alfanumérico (IN RFB 2.229/2024 — novas inscrições a partir de jul/2026)
--   * Saldos mensais mantidos automaticamente na efetivação
--   * Trilha de auditoria com hash encadeado (SHA-256)
--   * Verificação de invariantes para o fechamento de período
-- =============================================================================

-- -----------------------------------------------------------------------------
-- CNPJ: 12 posições alfanuméricas (raiz + ordem) + 2 dígitos verificadores numéricos
-- -----------------------------------------------------------------------------
ALTER TABLE core.empresa DROP CONSTRAINT ck_empresa_cnpj;
ALTER TABLE core.empresa ADD CONSTRAINT ck_empresa_cnpj CHECK (cnpj ~ '^[0-9A-Z]{12}[0-9]{2}$');
ALTER TABLE core.estabelecimento ADD CONSTRAINT ck_estab_cnpj CHECK (cnpj ~ '^[0-9A-Z]{12}[0-9]{2}$');

-- -----------------------------------------------------------------------------
-- Saldos mensais: acumulados quando o lançamento passa de RASCUNHO para EFETIVADO.
-- Como partidas de lançamento efetivado são imutáveis (trigger R3), o acumulado é
-- sempre consistente. O estorno é outro lançamento e acumula pelo mesmo caminho.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION contabil.fn_acumula_saldo_mensal()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO contabil.saldo_mensal AS s (empresa_id, conta_id, centro_custo_id, competencia, total_debito, total_credito)
    SELECT NEW.empresa_id, p.conta_id, COALESCE(p.centro_custo_id, 0),
           date_trunc('month', NEW.data_lancamento)::date,
           COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'D'), 0),
           COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'C'), 0)
      FROM contabil.partida p
     WHERE p.lancamento_id = NEW.id
     GROUP BY p.conta_id, COALESCE(p.centro_custo_id, 0)
    ON CONFLICT (empresa_id, conta_id, centro_custo_id, competencia) DO UPDATE
       SET total_debito  = s.total_debito  + EXCLUDED.total_debito,
           total_credito = s.total_credito + EXCLUDED.total_credito;
    RETURN NULL;
END $$;

CREATE TRIGGER trg_acumula_saldo_mensal
    AFTER UPDATE OF status ON contabil.lancamento
    FOR EACH ROW
    WHEN (OLD.status = 'RASCUNHO' AND NEW.status = 'EFETIVADO')
    EXECUTE FUNCTION contabil.fn_acumula_saldo_mensal();

-- -----------------------------------------------------------------------------
-- Auditoria encadeada. Cada registro guarda o hash do anterior: alterar ou apagar
-- uma linha antiga quebra a cadeia, detectável por core.fn_verificar_auditoria().
-- A aplicação informa o usuário com: SET LOCAL perseus.usuario = '<login>'.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION core.fn_hash_auditoria(
    p_hash_anterior CHAR(64), p_ocorrido TIMESTAMPTZ, p_usuario TEXT, p_tabela TEXT,
    p_registro BIGINT, p_operacao TEXT, p_antes JSONB, p_depois JSONB)
RETURNS CHAR(64) LANGUAGE sql IMMUTABLE AS $$
    SELECT encode(sha256(convert_to(concat_ws('|',
        COALESCE(p_hash_anterior, ''), to_char(p_ocorrido AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US'),
        p_usuario, p_tabela, COALESCE(p_registro::text, ''), p_operacao,
        COALESCE(p_antes::text, ''), COALESCE(p_depois::text, '')), 'UTF8')), 'hex');
$$;

CREATE OR REPLACE FUNCTION core.fn_auditoria()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_antes    JSONB := CASE WHEN TG_OP <> 'INSERT' THEN to_jsonb(OLD) END;
    v_depois   JSONB := CASE WHEN TG_OP <> 'DELETE' THEN to_jsonb(NEW) END;
    v_registro JSONB := COALESCE(v_depois, v_antes);
    v_empresa  BIGINT;
    v_usuario  TEXT  := COALESCE(NULLIF(current_setting('perseus.usuario', true), ''), current_user);
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

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.empresa
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.empresa_regime
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON contabil.plano_contas
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER UPDATE ON contabil.periodo
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
-- Lançamentos: só mudanças de status (efetivação/estorno); rascunhos não poluem a trilha.
CREATE TRIGGER trg_auditoria AFTER UPDATE OF status ON contabil.lancamento
    FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status)
    EXECUTE FUNCTION core.fn_auditoria();

-- Nem a aplicação altera a trilha.
CREATE OR REPLACE FUNCTION core.fn_auditoria_imutavel()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'A trilha de auditoria é somente inclusão';
END $$;

CREATE TRIGGER trg_auditoria_imutavel BEFORE UPDATE OR DELETE ON core.auditoria
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria_imutavel();

-- Retorna o primeiro registro cuja cadeia está quebrada (NULL = íntegra).
CREATE OR REPLACE FUNCTION core.fn_verificar_auditoria()
RETURNS BIGINT LANGUAGE plpgsql STABLE AS $$
DECLARE
    r          core.auditoria%ROWTYPE;
    v_anterior CHAR(64);
BEGIN
    FOR r IN SELECT * FROM core.auditoria ORDER BY id LOOP
        IF r.hash_anterior IS DISTINCT FROM v_anterior
           OR r.hash_registro <> core.fn_hash_auditoria(r.hash_anterior, r.ocorrido_em, r.usuario_login, r.tabela,
                                                        r.registro_id, r.operacao, r.dados_antes, r.dados_depois) THEN
            RETURN r.id;
        END IF;
        v_anterior := r.hash_registro;
    END LOOP;
    RETURN NULL;
END $$;

-- -----------------------------------------------------------------------------
-- Invariantes verificados antes de fechar uma competência (docs/02, seção 5).
-- Retorna uma linha por problema; vazio = pode fechar.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION contabil.fn_verificar_invariantes(p_empresa_id BIGINT, p_competencia DATE)
RETURNS TABLE (codigo TEXT, descricao TEXT) LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_ini DATE := date_trunc('month', p_competencia)::date;
    v_fim DATE := (date_trunc('month', p_competencia) + INTERVAL '1 month - 1 day')::date;
    v_exercicio BIGINT;
BEGIN
    SELECT e.id INTO v_exercicio FROM contabil.exercicio e
     WHERE e.empresa_id = p_empresa_id AND v_ini BETWEEN e.data_inicio AND e.data_fim;

    -- (1) Rascunhos pendentes no mês ficariam presos em período fechado.
    RETURN QUERY
    SELECT 'RASCUNHO_PENDENTE', format('%s lançamento(s) em rascunho no período', COUNT(*))
      FROM contabil.lancamento l
     WHERE l.empresa_id = p_empresa_id AND l.status = 'RASCUNHO' AND l.data_lancamento BETWEEN v_ini AND v_fim
    HAVING COUNT(*) > 0;

    -- (2) Movimento do mês: Σ débitos = Σ créditos.
    RETURN QUERY
    SELECT 'MOVIMENTO_DESBALANCEADO', format('Débitos %s <> créditos %s', d, c)
      FROM (SELECT COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'D'), 0) d,
                   COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'C'), 0) c
              FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id
             WHERE l.empresa_id = p_empresa_id AND l.status <> 'RASCUNHO'
               AND l.data_lancamento BETWEEN v_ini AND v_fim) t
     WHERE d <> c;

    -- (3) Cache de saldos confere com as partidas.
    RETURN QUERY
    WITH real AS (
        SELECT p.conta_id, COALESCE(p.centro_custo_id, 0) cc,
               SUM(p.valor) FILTER (WHERE p.ind_dc = 'D') deb,
               SUM(p.valor) FILTER (WHERE p.ind_dc = 'C') cred
          FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id
         WHERE l.empresa_id = p_empresa_id AND l.status <> 'RASCUNHO'
           AND l.data_lancamento BETWEEN v_ini AND v_fim
         GROUP BY 1, 2)
    SELECT 'SALDO_DIVERGENTE', format('Conta %s: saldo_mensal (%s D / %s C) difere das partidas (%s D / %s C)',
                                      pc.codigo, COALESCE(s.total_debito, 0), COALESCE(s.total_credito, 0),
                                      COALESCE(r.deb, 0), COALESCE(r.cred, 0))
      FROM real r
      FULL JOIN (SELECT * FROM contabil.saldo_mensal
                  WHERE empresa_id = p_empresa_id AND competencia = v_ini) s
             ON s.conta_id = r.conta_id AND s.centro_custo_id = r.cc
      JOIN contabil.plano_contas pc ON pc.id = COALESCE(r.conta_id, s.conta_id)
     WHERE COALESCE(s.total_debito, 0) <> COALESCE(r.deb, 0)
        OR COALESCE(s.total_credito, 0) <> COALESCE(r.cred, 0);

    -- (4) Numeração do Diário sem lacunas no exercício.
    RETURN QUERY
    SELECT 'LACUNA_NUMERACAO', format('Numeração de lançamentos com lacuna: esperados %s, existem %s (máximo %s)',
                                      mx, qt, mx)
      FROM (SELECT COUNT(*) qt, COALESCE(MAX(numero), 0) mx FROM contabil.lancamento
             WHERE exercicio_id = v_exercicio AND numero IS NOT NULL) t
     WHERE qt <> mx;
END $$;
