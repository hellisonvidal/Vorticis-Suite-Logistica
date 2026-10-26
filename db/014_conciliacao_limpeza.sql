-- =============================================================================
-- Perseus Contábil — Migração 014
--   * Conciliação de contas: partidas de uma conta que se compensam (débitos = créditos)
--     ficam agrupadas numa conciliação — automática pelo nº do documento ou manual.
--     O nº do documento é o campo NUM_ARQ da partida (ECD I250).
--   * Limpeza do banco (Configurações gerais): apaga todas as empresas e cadastros,
--     mantendo usuários, perfis, configurações e a trilha de auditoria.
-- =============================================================================

CREATE INDEX IF NOT EXISTS ix_partida_documento ON contabil.partida (conta_id, num_arquivo) WHERE num_arquivo IS NOT NULL;

CREATE TABLE contabil.conciliacao (
    id           BIGSERIAL    PRIMARY KEY,
    empresa_id   BIGINT       NOT NULL REFERENCES core.empresa (id),
    conta_id     BIGINT       NOT NULL REFERENCES contabil.plano_contas (id),
    documento    VARCHAR(60),
    automatica   BOOLEAN      NOT NULL DEFAULT FALSE,
    criado_em    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    criado_por   BIGINT       REFERENCES core.usuario (id) ON DELETE SET NULL
);
CREATE INDEX ix_conciliacao_conta ON contabil.conciliacao (conta_id);

CREATE TABLE contabil.conciliacao_partida (
    partida_id      BIGINT  PRIMARY KEY REFERENCES contabil.partida (id) ON DELETE CASCADE,
    conciliacao_id  BIGINT  NOT NULL REFERENCES contabil.conciliacao (id) ON DELETE CASCADE
);
CREATE INDEX ix_conciliacao_partida_conc ON contabil.conciliacao_partida (conciliacao_id);

-- Exclusão da empresa: conciliações e imobilizado saem antes do plano de contas.
CREATE OR REPLACE FUNCTION core.fn_excluir_empresa(p_empresa BIGINT)
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE
    v_lancamentos INTEGER;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM core.empresa WHERE id = p_empresa) THEN
        RAISE EXCEPTION 'Empresa % não encontrada', p_empresa;
    END IF;
    PERFORM set_config('perseus.excluindo_empresa', p_empresa::text, true);

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

    PERFORM set_config('perseus.excluindo_empresa', '', true);
    RETURN v_lancamentos;
END $$;

-- Limpa o banco: todas as empresas (com a escrituração), contadores, escritórios, tabelas do plano
-- referencial e layouts de importação. Ficam usuários, perfis, configurações, grupos do imobilizado,
-- parâmetros legais e a auditoria. Retorna quantas empresas foram excluídas.
CREATE OR REPLACE FUNCTION core.fn_limpar_banco()
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE
    v_empresa BIGINT;
    v_qtd     INTEGER := 0;
BEGIN
    FOR v_empresa IN SELECT id FROM core.empresa ORDER BY id LOOP
        PERFORM core.fn_excluir_empresa(v_empresa);
        v_qtd := v_qtd + 1;
    END LOOP;
    DELETE FROM core.contador;
    DELETE FROM core.escritorio_contabil;
    DELETE FROM contabil.plano_referencial;
    DELETE FROM contabil.plano_referencial_importacao;
    DELETE FROM contabil.layout_importacao;
    RETURN v_qtd;
END $$;
