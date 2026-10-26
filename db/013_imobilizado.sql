-- =============================================================================
-- Perseus Contábil — Migração 013: Imobilizado
--   * Grupos de bens com a taxa fiscal de depreciação da RFB (IN RFB 1.700/2017,
--     Anexo III; RIR/2018 arts. 317 a 330) e contas contábeis sugeridas.
--   * Cadastro de bens por empresa: custo, valor residual, vida útil estimada e
--     método (NBC TG 27 / CPC 27 — Ativo Imobilizado), turnos de uso (depreciação
--     acelerada fiscal, RIR/2018 art. 323), baixa.
--   * Depreciação mensal lançada na contabilidade (origem DEPRECIACAO), uma linha por
--     bem e competência, ligada ao lançamento gerado.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS imobilizado;

CREATE TABLE imobilizado.grupo (
    id                  BIGSERIAL     PRIMARY KEY,
    codigo              VARCHAR(10)   NOT NULL UNIQUE,
    descricao           VARCHAR(120)  NOT NULL,
    taxa_anual_fiscal   NUMERIC(6,2)  NOT NULL CHECK (taxa_anual_fiscal BETWEEN 0 AND 100),
    deprecia            BOOLEAN       NOT NULL DEFAULT TRUE,     -- terrenos, obras de arte: não depreciam
    intangivel          BOOLEAN       NOT NULL DEFAULT FALSE,    -- amortização (software, marcas)
    conta_bem           VARCHAR(30),
    conta_acumulada     VARCHAR(30),
    conta_despesa       VARCHAR(30),
    base_legal          VARCHAR(200)
);

INSERT INTO imobilizado.grupo (codigo, descricao, taxa_anual_fiscal, deprecia, intangivel, conta_bem, conta_acumulada, conta_despesa, base_legal) VALUES
    ('01', 'Edificações e construções',                   4, TRUE,  FALSE, '1.2.03.005', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III — 25 anos'),
    ('02', 'Instalações',                                 10, TRUE,  FALSE, '1.2.03.002', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III — 10 anos'),
    ('03', 'Máquinas e equipamentos',                     10, TRUE,  FALSE, '1.2.03.002', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III — 10 anos'),
    ('04', 'Móveis e utensílios',                         10, TRUE,  FALSE, '1.2.03.001', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III — 10 anos'),
    ('05', 'Veículos de passageiros (automóveis)',        20, TRUE,  FALSE, '1.2.03.003', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III (NCM 8703) — 5 anos'),
    ('06', 'Veículos de carga, caminhões e motocicletas', 25, TRUE,  FALSE, '1.2.03.003', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III (NCM 8704/8711) — 4 anos'),
    ('07', 'Tratores',                                    25, TRUE,  FALSE, '1.2.03.002', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III (NCM 8701) — 4 anos'),
    ('08', 'Computadores e periféricos (hardware)',       20, TRUE,  FALSE, '1.2.03.004', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III (NCM 8471) — 5 anos'),
    ('09', 'Ferramentas',                                 20, TRUE,  FALSE, '1.2.03.002', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III (NCM 8205/8207) — 5 anos'),
    ('10', 'Aparelhos de telefonia e comunicação',        10, TRUE,  FALSE, '1.2.03.002', '1.2.03.007', '4.2.02.007', 'IN RFB 1.700/2017, Anexo III (NCM 8517) — 10 anos'),
    ('11', 'Softwares (amortização)',                     20, TRUE,  TRUE,  '1.2.04.001', '1.2.04.003', '4.2.02.007', 'RIR/2018 art. 330 — 5 anos'),
    ('12', 'Terrenos',                                     0, FALSE, FALSE, '1.2.03.006', NULL,         NULL,         'RIR/2018 art. 318 — terrenos não se depreciam');

CREATE TABLE imobilizado.bem (
    id                     BIGSERIAL      PRIMARY KEY,
    empresa_id             BIGINT         NOT NULL REFERENCES core.empresa (id),
    codigo                 INTEGER        NOT NULL,
    descricao              VARCHAR(200)   NOT NULL,
    grupo_id               BIGINT         NOT NULL REFERENCES imobilizado.grupo (id),
    plaqueta               VARCHAR(40),
    localizacao            VARCHAR(100),
    nota_fiscal            VARCHAR(40),
    fornecedor_id          BIGINT         REFERENCES core.participante (id) ON DELETE SET NULL,
    data_aquisicao         DATE           NOT NULL,
    data_inicio_uso        DATE           NOT NULL,
    valor_aquisicao        NUMERIC(15,2)  NOT NULL CHECK (valor_aquisicao > 0),
    valor_residual         NUMERIC(15,2)  NOT NULL DEFAULT 0 CHECK (valor_residual >= 0),
    vida_util_meses        INTEGER        NOT NULL CHECK (vida_util_meses >= 0),
    metodo                 VARCHAR(20)    NOT NULL DEFAULT 'LINEAR' CHECK (metodo IN ('LINEAR', 'SOMA_DIGITOS')),
    turnos                 SMALLINT       NOT NULL DEFAULT 1 CHECK (turnos BETWEEN 1 AND 3),
    depreciacao_anterior   NUMERIC(15,2)  NOT NULL DEFAULT 0 CHECK (depreciacao_anterior >= 0),  -- implantação
    conta_bem_id           BIGINT         NOT NULL REFERENCES contabil.plano_contas (id),
    conta_acumulada_id     BIGINT         REFERENCES contabil.plano_contas (id),
    conta_despesa_id       BIGINT         REFERENCES contabil.plano_contas (id),
    data_baixa             DATE,
    motivo_baixa           VARCHAR(200),
    criado_em              TIMESTAMPTZ    NOT NULL DEFAULT now(),
    CONSTRAINT uq_bem_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_bem_residual CHECK (valor_residual + depreciacao_anterior <= valor_aquisicao),
    CONSTRAINT ck_bem_uso CHECK (data_inicio_uso >= data_aquisicao),
    CONSTRAINT ck_bem_baixa CHECK (data_baixa IS NULL OR data_baixa >= data_aquisicao)
);

CREATE TABLE imobilizado.depreciacao (
    id              BIGSERIAL      PRIMARY KEY,
    bem_id          BIGINT         NOT NULL REFERENCES imobilizado.bem (id) ON DELETE CASCADE,
    competencia     DATE           NOT NULL CHECK (extract(day FROM competencia) = 1),
    valor           NUMERIC(15,2)  NOT NULL CHECK (valor > 0),
    valor_fiscal    NUMERIC(15,2)  NOT NULL DEFAULT 0,
    lancamento_id   BIGINT         NOT NULL REFERENCES contabil.lancamento (id) ON DELETE CASCADE,
    CONSTRAINT uq_depreciacao UNIQUE (bem_id, competencia)
);
CREATE INDEX ix_depreciacao_lancamento ON imobilizado.depreciacao (lancamento_id);

-- Exclusão da empresa: o imobilizado sai antes do plano de contas.
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
