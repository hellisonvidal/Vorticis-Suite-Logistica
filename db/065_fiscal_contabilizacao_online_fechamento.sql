-- =============================================================================
-- Vorticis — Migração 065: Fiscal
--   * fiscal.configuracao.contabilizacao_online: as notas são contabilizadas ao gravar/importar (on-line) em vez de
--     pela função Contabilizar notas fiscais (em lote, padrão).
--   * fiscal.periodo_fechado: fechamento do período fiscal (mês) das empresas do regime normal. Com o mês fechado,
--     notas (e itens) com data de entrada/saída no mês não podem ser incluídas, alteradas nem excluídas; a marcação de
--     contabilização continua permitida. Reabrir o mês apaga o registro.
-- =============================================================================

ALTER TABLE fiscal.configuracao ADD COLUMN contabilizacao_online BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE fiscal.periodo_fechado (
    empresa_id   BIGINT       NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    competencia  DATE         NOT NULL CHECK (EXTRACT(DAY FROM competencia) = 1),
    fechado_em   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    fechado_por  BIGINT,
    PRIMARY KEY (empresa_id, competencia)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.periodo_fechado FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

CREATE FUNCTION fiscal.fn_mes_fechado(p_empresa BIGINT, p_data DATE) RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    SELECT EXISTS (SELECT 1 FROM fiscal.periodo_fechado WHERE empresa_id = p_empresa AND competencia = date_trunc('month', p_data)::date)
$$;

CREATE FUNCTION fiscal.fn_protege_periodo_fechado() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_ignorar TEXT[] := ARRAY['lancamento_id', 'contabilizado_em', 'contabilizado_por', 'atualizado_em'];
BEGIN
    -- exclusão em cascata da empresa (limpeza do banco, exclusão da empresa) não é bloqueada
    IF TG_OP = 'DELETE' AND NOT EXISTS (SELECT 1 FROM core.empresa WHERE id = OLD.empresa_id) THEN RETURN OLD; END IF;
    -- só a marcação de contabilização mudou: permitido
    IF TG_OP = 'UPDATE' AND (to_jsonb(NEW) - v_ignorar) = (to_jsonb(OLD) - v_ignorar) THEN RETURN NEW; END IF;
    IF TG_OP IN ('UPDATE', 'DELETE') AND fiscal.fn_mes_fechado(OLD.empresa_id, OLD.data_entrada_saida) THEN
        RAISE EXCEPTION 'O período fiscal de % está fechado: reabra o mês em Apuração › Fechamento do período fiscal para alterar ou excluir a nota %',
            to_char(OLD.data_entrada_saida, 'MM/YYYY'), OLD.numero USING ERRCODE = 'check_violation';
    END IF;
    IF TG_OP IN ('INSERT', 'UPDATE') AND fiscal.fn_mes_fechado(NEW.empresa_id, NEW.data_entrada_saida) THEN
        RAISE EXCEPTION 'O período fiscal de % está fechado: reabra o mês em Apuração › Fechamento do período fiscal para lançar a nota %',
            to_char(NEW.data_entrada_saida, 'MM/YYYY'), NEW.numero USING ERRCODE = 'check_violation';
    END IF;
    RETURN COALESCE(NEW, OLD);
END $$;

CREATE TRIGGER trg_periodo_fechado BEFORE INSERT OR UPDATE OR DELETE ON fiscal.documento
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_protege_periodo_fechado();

CREATE FUNCTION fiscal.fn_protege_periodo_fechado_item() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_doc fiscal.documento;
BEGIN
    SELECT * INTO v_doc FROM fiscal.documento WHERE id = COALESCE(NEW.documento_id, OLD.documento_id);
    IF NOT FOUND THEN RETURN COALESCE(NEW, OLD); END IF;   -- documento sendo excluído (em cascata)
    IF fiscal.fn_mes_fechado(v_doc.empresa_id, v_doc.data_entrada_saida) THEN
        RAISE EXCEPTION 'O período fiscal de % está fechado: reabra o mês em Apuração › Fechamento do período fiscal para alterar os itens da nota %',
            to_char(v_doc.data_entrada_saida, 'MM/YYYY'), v_doc.numero USING ERRCODE = 'check_violation';
    END IF;
    RETURN COALESCE(NEW, OLD);
END $$;

CREATE TRIGGER trg_periodo_fechado BEFORE INSERT OR UPDATE OR DELETE ON fiscal.documento_item
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_protege_periodo_fechado_item();
