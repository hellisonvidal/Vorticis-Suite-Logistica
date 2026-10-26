-- =============================================================================
-- 070 — Preferências de menu e contabilização em lote.
--   * core.preferencia_menu: itens de menu que o usuário não quer ver, por programa. usuario_id nulo = regra do
--     administrador para todos os usuários. Só ficam gravados os itens ocultos (item visível = sem linha).
--   * contabil.contabilizacao_config: tipo de contabilização da empresa — ONLINE (padrão: cada documento, baixa ou folha
--     gera o lançamento na hora) ou LOTE (os módulos só registram pendências, processadas na tela Contabilização em lote).
--   * contabil.lote_contabilizacao e contabil.pendencia_contabilizacao: o lote e as pendências por origem.
-- =============================================================================
CREATE TABLE core.preferencia_menu (
    id             BIGSERIAL     PRIMARY KEY,
    programa       VARCHAR(40)   NOT NULL,
    usuario_id     BIGINT        REFERENCES core.usuario(id) ON DELETE CASCADE,
    chave          VARCHAR(200)  NOT NULL,
    atualizado_em  TIMESTAMPTZ   NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uq_preferencia_menu_usuario ON core.preferencia_menu (programa, usuario_id, chave) WHERE usuario_id IS NOT NULL;
CREATE UNIQUE INDEX uq_preferencia_menu_todos   ON core.preferencia_menu (programa, chave) WHERE usuario_id IS NULL;

CREATE TABLE contabil.contabilizacao_config (
    empresa_id     BIGINT        PRIMARY KEY REFERENCES core.empresa(id) ON DELETE CASCADE,
    modo           VARCHAR(6)    NOT NULL DEFAULT 'ONLINE',
    atualizado_em  TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT ck_contabilizacao_modo CHECK (modo IN ('ONLINE', 'LOTE'))
);

CREATE TABLE contabil.lote_contabilizacao (
    id             BIGSERIAL     PRIMARY KEY,
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    criado_em      TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por     BIGINT        REFERENCES core.usuario(id) ON DELETE SET NULL,
    desfeito_em    TIMESTAMPTZ,
    desfeito_por   BIGINT        REFERENCES core.usuario(id) ON DELETE SET NULL
);

CREATE TABLE contabil.pendencia_contabilizacao (
    id             BIGSERIAL     PRIMARY KEY,
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    origem         VARCHAR(12)   NOT NULL,
    tipo           VARCHAR(12)   NOT NULL,
    referencia     VARCHAR(80)   NOT NULL,
    periodo        DATE          NOT NULL,
    descricao      VARCHAR(250)  NOT NULL,
    valor          NUMERIC(15,2) NOT NULL DEFAULT 0,
    situacao       VARCHAR(14)   NOT NULL DEFAULT 'PENDENTE',
    lote_id        BIGINT        REFERENCES contabil.lote_contabilizacao(id),
    lancamento_id  BIGINT        REFERENCES contabil.lancamento(id) ON DELETE SET NULL,
    mensagem       VARCHAR(500),
    criado_em      TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por     BIGINT        REFERENCES core.usuario(id) ON DELETE SET NULL,
    CONSTRAINT ck_pendencia_origem CHECK (origem IN ('FISCAL', 'DUPLICATAS', 'FOLHA', 'FROTA', 'GERENCIAL')),
    CONSTRAINT ck_pendencia_situacao CHECK (situacao IN ('PENDENTE', 'CONTABILIZADA', 'CANCELADA'))
);
-- uma pendência aberta por documento/baixa/competência
CREATE UNIQUE INDEX uq_pendencia_aberta ON contabil.pendencia_contabilizacao (empresa_id, origem, tipo, referencia) WHERE situacao = 'PENDENTE';
CREATE INDEX ix_pendencia_empresa ON contabil.pendencia_contabilizacao (empresa_id, situacao, periodo);
CREATE INDEX ix_pendencia_lote ON contabil.pendencia_contabilizacao (lote_id);
