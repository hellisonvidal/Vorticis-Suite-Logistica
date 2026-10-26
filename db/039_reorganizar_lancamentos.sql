-- =============================================================================
-- Vorticis — Migração 039
--   Utilitários do Contábil: reorganizar a numeração dos lançamentos efetivados.
--   O trigger de imutabilidade passa a admitir a troca do NÚMERO (e só dele) quando a transação
--   declara a reorganização da empresa (vorticis.reorganizando_lancamentos). Conteúdo contábil nunca muda.
-- =============================================================================

-- Empresa em reorganização nesta transação (definida só pelo repositório, após as conferências da aplicação).
CREATE OR REPLACE FUNCTION core.reorganizando_lancamentos(p_empresa BIGINT)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    SELECT COALESCE(current_setting('vorticis.reorganizando_lancamentos', true), '') = p_empresa::text
$$;

CREATE OR REPLACE FUNCTION contabil.fn_valida_lancamento()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_ex      contabil.exercicio%ROWTYPE;
    v_periodo contabil.status_periodo;
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'RASCUNHO' AND NOT core.excluindo_empresa(OLD.empresa_id) THEN
            RAISE EXCEPTION 'Lançamento % efetivado não pode ser excluído', OLD.numero;
        END IF;
        RETURN OLD;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.status <> 'RASCUNHO' THEN
        -- Reorganização da numeração: só o número muda.
        IF core.reorganizando_lancamentos(OLD.empresa_id)
           AND (to_jsonb(NEW) - 'numero') = (to_jsonb(OLD) - 'numero') THEN
            RETURN NEW;
        END IF;
        IF NOT (OLD.status = 'EFETIVADO' AND NEW.status = 'ESTORNADO'
                AND (to_jsonb(NEW) - 'status') = (to_jsonb(OLD) - 'status')) THEN
            RAISE EXCEPTION 'Lançamento % efetivado é imutável (só é permitido marcar como ESTORNADO)', OLD.numero;
        END IF;
        RETURN NEW;
    END IF;

    SELECT * INTO v_ex FROM contabil.exercicio WHERE id = NEW.exercicio_id;
    IF v_ex.empresa_id <> NEW.empresa_id
       OR NEW.data_lancamento NOT BETWEEN v_ex.data_inicio AND v_ex.data_fim THEN
        RAISE EXCEPTION 'Data % fora do exercício %', NEW.data_lancamento, v_ex.ano;
    END IF;

    IF NEW.status = 'EFETIVADO' THEN
        SELECT p.status INTO v_periodo
          FROM contabil.periodo p
         WHERE p.exercicio_id = NEW.exercicio_id
           AND p.competencia = date_trunc('month', NEW.data_lancamento)::date;
        IF v_periodo IS DISTINCT FROM 'ABERTO' THEN
            RAISE EXCEPTION 'Período de % está fechado ou não existe', to_char(NEW.data_lancamento, 'MM/YYYY');
        END IF;
        IF NEW.ind_lcto = 'E' AND NEW.data_lancamento <> v_ex.data_fim THEN
            RAISE EXCEPTION 'Lançamento de encerramento deve ter a data final do exercício';
        END IF;
    END IF;
    RETURN NEW;
END $$;

-- Evento de auditoria avulso (operação que não é uma linha alterada), na mesma cadeia de hashes.
CREATE OR REPLACE FUNCTION core.fn_registrar_evento_auditoria(
    p_empresa BIGINT, p_tabela TEXT, p_registro BIGINT, p_operacao TEXT, p_depois JSONB)
RETURNS VOID LANGUAGE plpgsql AS $$
DECLARE
    v_usuario  TEXT := COALESCE(NULLIF(current_setting('vorticis.usuario', true), ''), current_user);
    v_agora    TIMESTAMPTZ := clock_timestamp();
    v_anterior CHAR(64);
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('core.auditoria'));
    SELECT hash_registro INTO v_anterior FROM core.auditoria ORDER BY id DESC LIMIT 1;
    INSERT INTO core.auditoria (empresa_id, usuario_login, ocorrido_em, tabela, registro_id, operacao,
                                dados_antes, dados_depois, hash_anterior, hash_registro)
    VALUES (p_empresa, v_usuario, v_agora, p_tabela, p_registro, p_operacao, NULL, p_depois, v_anterior,
            core.fn_hash_auditoria(v_anterior, v_agora, v_usuario, p_tabela, p_registro, p_operacao, NULL, p_depois));
END $$;
