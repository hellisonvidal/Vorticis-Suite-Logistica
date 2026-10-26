-- =============================================================================
-- 069 — Frota: menu Manutenção.
--   * Catálogo de peças e serviços (código, unidade e valor de referência).
--   * Ordem de serviço com itens (peças e serviços) e fluxo de autorização:
--     ABERTA → AGUARDANDO (autorização) → APROVADA ou REPROVADA → CONCLUIDA; CANCELADA a qualquer momento antes de concluir.
--     Até o limite da configuração a autorização é automática (0 = toda ordem precisa ser autorizada).
--   * Histórico das autorizações (quem, quando, decisão, valor) e notas fiscais de compra ligadas à ordem.
--   * Funções FROTA_MANUTENCAO (planos, peças e serviços, ordens, notas) e FROTA_APROVAR (autorizar ordens).
-- =============================================================================
INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('FROTA_MANUTENCAO'), ('FROTA_APROVAR')) AS x(permissao) ON TRUE
 WHERE p.administrador OR (p.padrao AND p.nome = 'Contador')
ON CONFLICT DO NOTHING;

-- quem já lançava ordens de serviço (FROTA_OPERACAO) continua lançando, agora pelo menu Manutenção
INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT pp.perfil_id, 'FROTA_MANUTENCAO' FROM core.perfil_permissao pp WHERE pp.permissao = 'FROTA_OPERACAO'
ON CONFLICT DO NOTHING;

ALTER TABLE frota.configuracao
    ADD COLUMN limite_autorizacao NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (limite_autorizacao >= 0);

CREATE TABLE frota.item_manutencao (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL CHECK (codigo > 0),
    tipo               VARCHAR(7)    NOT NULL,
    descricao          VARCHAR(150)  NOT NULL,
    unidade            VARCHAR(6)    NOT NULL DEFAULT 'UN',
    valor_referencia   NUMERIC(15,2) CHECK (valor_referencia IS NULL OR valor_referencia >= 0),
    ativo              BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_item_manutencao UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_item_manutencao_tipo CHECK (tipo IN ('PECA', 'SERVICO'))
);

ALTER TABLE frota.ordem_servico
    ALTER COLUMN situacao TYPE VARCHAR(10),
    DROP CONSTRAINT ck_os_situacao,
    ADD CONSTRAINT ck_os_situacao CHECK (situacao IN ('ABERTA', 'AGUARDANDO', 'APROVADA', 'REPROVADA', 'CONCLUIDA', 'CANCELADA')),
    ADD COLUMN solicitante VARCHAR(100),
    ADD COLUMN previsao    DATE,
    ADD COLUMN conclusao   DATE;

-- ordens concluídas antes desta versão: conclusão na própria data da ordem
UPDATE frota.ordem_servico SET conclusao = data WHERE situacao = 'CONCLUIDA';

ALTER TABLE frota.ordem_servico
    ADD CONSTRAINT ck_os_conclusao CHECK ((situacao = 'CONCLUIDA') = (conclusao IS NOT NULL)),
    ADD CONSTRAINT ck_os_datas CHECK ((conclusao IS NULL OR conclusao >= data) AND (previsao IS NULL OR previsao >= data));

CREATE TABLE frota.ordem_item (
    id                 BIGSERIAL     PRIMARY KEY,
    ordem_id           BIGINT        NOT NULL REFERENCES frota.ordem_servico(id) ON DELETE CASCADE,
    item_id            BIGINT        REFERENCES frota.item_manutencao(id) ON DELETE SET NULL,
    tipo               VARCHAR(7)    NOT NULL,
    descricao          VARCHAR(150)  NOT NULL,
    quantidade         NUMERIC(12,3) NOT NULL CHECK (quantidade > 0),
    valor_unitario     NUMERIC(15,2) NOT NULL CHECK (valor_unitario >= 0),
    CONSTRAINT ck_ordem_item_tipo CHECK (tipo IN ('PECA', 'SERVICO'))
);
CREATE INDEX ix_ordem_item_ordem ON frota.ordem_item (ordem_id);

CREATE TABLE frota.autorizacao (
    id                 BIGSERIAL     PRIMARY KEY,
    ordem_id           BIGINT        NOT NULL REFERENCES frota.ordem_servico(id) ON DELETE CASCADE,
    momento            TIMESTAMPTZ   NOT NULL DEFAULT now(),
    usuario_id         BIGINT        REFERENCES core.usuario(id) ON DELETE SET NULL,
    decisao            VARCHAR(9)    NOT NULL,
    valor              NUMERIC(15,2) NOT NULL CHECK (valor >= 0),
    automatica         BOOLEAN       NOT NULL DEFAULT FALSE,
    observacao         VARCHAR(300),
    CONSTRAINT ck_autorizacao_decisao CHECK (decisao IN ('APROVADA', 'REPROVADA')),
    CONSTRAINT ck_autorizacao_motivo CHECK (decisao = 'APROVADA' OR length(trim(coalesce(observacao, ''))) > 0)
);
CREATE INDEX ix_autorizacao_ordem ON frota.autorizacao (ordem_id);

CREATE TABLE frota.nota_compra (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    ordem_id           BIGINT        REFERENCES frota.ordem_servico(id) ON DELETE SET NULL,
    fornecedor         VARCHAR(150)  NOT NULL,
    cnpj               VARCHAR(14),
    numero             VARCHAR(20)   NOT NULL,
    serie              VARCHAR(3)    NOT NULL DEFAULT '1',
    emissao            DATE          NOT NULL,
    chave              CHAR(44)      CHECK (chave IS NULL OR chave ~ '^[0-9]{44}$'),
    valor_pecas        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_pecas >= 0),
    valor_servicos     NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_servicos >= 0),
    observacao         VARCHAR(300),
    CONSTRAINT ck_nota_compra_valor CHECK (valor_pecas + valor_servicos > 0)
);
CREATE UNIQUE INDEX uq_nota_compra ON frota.nota_compra (empresa_id, coalesce(cnpj, upper(fornecedor)), numero, serie);
CREATE INDEX ix_nota_compra_emissao ON frota.nota_compra (empresa_id, emissao);
