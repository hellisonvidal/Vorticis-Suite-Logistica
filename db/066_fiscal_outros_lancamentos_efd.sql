-- =============================================================================
-- Vorticis — Migração 066: outros lançamentos da EFD ICMS/IPI (SPED Fiscal), em Lançamentos › SPED Fiscal
--   * fiscal.efd_ajuste_icms: ajustes da apuração do ICMS próprio (registro E111), por competência. O código (tabela 5.1.1
--     da UF) tem 8 posições: UF + "0" (ICMS próprio) + tipo (0 outros débitos, 1 estorno de créditos, 2 outros créditos,
--     3 estorno de débitos, 4 deduções, 5 débitos especiais) + sequência.
--   * fiscal.conta_consumo: notas de energia elétrica (06), gás (28), água (29) e de comunicação/telecomunicação (21/22)
--     recebidas (registros C500/C590 e D500/D590), com o crédito de ICMS.
--   * fiscal.inventario / fiscal.inventario_item: inventário de estoque (bloco H: H005/H010), informado na EFD da
--     competência escolhida (o de 31/12 costuma ir na EFD de fevereiro).
-- =============================================================================

CREATE TABLE fiscal.efd_ajuste_icms (
    id           BIGSERIAL     PRIMARY KEY,
    empresa_id   BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    competencia  DATE          NOT NULL CHECK (EXTRACT(DAY FROM competencia) = 1),
    codigo       CHAR(8)       NOT NULL CHECK (codigo ~ '^[A-Z]{2}0[0-5][0-9]{4}$'),
    descricao    VARCHAR(500)  NOT NULL DEFAULT '',
    valor        NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    criado_em    TIMESTAMPTZ   NOT NULL DEFAULT now()
);
CREATE INDEX ix_efd_ajuste_icms ON fiscal.efd_ajuste_icms (empresa_id, competencia);

CREATE TABLE fiscal.conta_consumo (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    modelo           CHAR(2)       NOT NULL CHECK (modelo IN ('06', '28', '29', '21', '22')),
    participante_id  BIGINT        NOT NULL REFERENCES core.participante (id),
    numero           VARCHAR(20)   NOT NULL CHECK (numero ~ '^[0-9]+$'),
    serie            VARCHAR(4)    NOT NULL DEFAULT '',
    data_emissao     DATE          NOT NULL,
    data_entrada     DATE          NOT NULL CHECK (data_entrada >= data_emissao),
    classe_consumo   CHAR(2)       NOT NULL DEFAULT '01',
    valor            NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    cfop             CHAR(4)       NOT NULL CHECK (cfop ~ '^[123][0-9]{3}$'),
    cst_icms         CHAR(3)       NOT NULL DEFAULT '090' CHECK (cst_icms ~ '^[0-8][0-9]{2}$'),
    base_icms        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (base_icms >= 0),
    aliq_icms        NUMERIC(7,4)  NOT NULL DEFAULT 0 CHECK (aliq_icms >= 0),
    valor_icms       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_icms >= 0),
    valor_pis        NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_cofins     NUMERIC(15,2) NOT NULL DEFAULT 0,
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_conta_consumo UNIQUE (empresa_id, participante_id, modelo, serie, numero)
);
CREATE INDEX ix_conta_consumo_periodo ON fiscal.conta_consumo (empresa_id, data_entrada);

CREATE TABLE fiscal.inventario (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    data             DATE          NOT NULL,
    motivo           CHAR(2)       NOT NULL DEFAULT '01' CHECK (motivo IN ('01', '02', '03', '04', '05', '06')),
    competencia_efd  DATE          NOT NULL CHECK (EXTRACT(DAY FROM competencia_efd) = 1),
    conta_contabil   VARCHAR(60)   NOT NULL DEFAULT '',
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_inventario UNIQUE (empresa_id, data, motivo)
);

CREATE TABLE fiscal.inventario_item (
    id              BIGSERIAL     PRIMARY KEY,
    inventario_id   BIGINT        NOT NULL REFERENCES fiscal.inventario (id) ON DELETE CASCADE,
    produto_id      BIGINT        NOT NULL REFERENCES fiscal.produto (id),
    unidade         VARCHAR(6)    NOT NULL DEFAULT 'UN',
    quantidade      NUMERIC(15,3) NOT NULL CHECK (quantidade > 0),
    valor_unitario  NUMERIC(17,6) NOT NULL CHECK (valor_unitario >= 0),
    valor           NUMERIC(15,2) NOT NULL CHECK (valor >= 0),
    CONSTRAINT uq_inventario_item UNIQUE (inventario_id, produto_id)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.efd_ajuste_icms FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.conta_consumo FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.inventario FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

-- Exclusão da empresa (mesma regra da migração 018): as contas de consumo saem antes dos participantes que elas referenciam.
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
    DELETE FROM fiscal.conta_consumo WHERE empresa_id = p_empresa;   -- referencia o participante (migração 066)
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
