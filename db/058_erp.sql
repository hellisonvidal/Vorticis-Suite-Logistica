-- =============================================================================
-- 058 — Módulo ERP: rotinas da empresa sobre os cadastros comuns (clientes, fornecedores) e os produtos e notas do Fiscal.
--   * Pedidos de venda e de compra; ao faturar (venda) ou receber (compra), o pedido vira nota fiscal no Fiscal
--     (fiscal.documento, com as duplicatas a receber ou a pagar) e movimenta o estoque.
--   * Estoque: movimentos por produto (entrada, saída, ajuste) com custo; mínimo e preço de venda por produto.
--   * Transporte: veículos, motoristas e o registro dos conhecimentos (CT-e) e manifestos (MDF-e).
-- =============================================================================
CREATE SCHEMA erp;

CREATE TABLE erp.produto_parametro (
    produto_id      BIGINT        PRIMARY KEY REFERENCES fiscal.produto(id) ON DELETE CASCADE,
    preco_venda     NUMERIC(15,4) NOT NULL DEFAULT 0 CHECK (preco_venda >= 0),
    estoque_minimo  NUMERIC(15,4) NOT NULL DEFAULT 0 CHECK (estoque_minimo >= 0)
);

CREATE TABLE erp.pedido (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT        NOT NULL REFERENCES core.empresa(id),
    tipo                VARCHAR(6)    NOT NULL,                         -- VENDA | COMPRA
    numero              INTEGER       NOT NULL,
    data                DATE          NOT NULL,
    participante_id     BIGINT        NOT NULL REFERENCES core.participante(id),
    situacao            VARCHAR(9)    NOT NULL DEFAULT 'ABERTO',        -- ABERTO | CONCLUIDO (faturado/recebido) | CANCELADO
    parcelas            SMALLINT      NOT NULL DEFAULT 1 CHECK (parcelas BETWEEN 1 AND 120),
    intervalo_dias      SMALLINT      NOT NULL DEFAULT 30 CHECK (intervalo_dias BETWEEN 0 AND 365),
    primeiro_vencimento DATE,
    observacao          VARCHAR(500),
    documento_id        BIGINT        REFERENCES fiscal.documento(id) ON DELETE SET NULL,
    concluido_em        DATE,
    CONSTRAINT uq_pedido_numero UNIQUE (empresa_id, tipo, numero),
    CONSTRAINT ck_pedido_tipo CHECK (tipo IN ('VENDA', 'COMPRA')),
    CONSTRAINT ck_pedido_situacao CHECK (situacao IN ('ABERTO', 'CONCLUIDO', 'CANCELADO'))
);

CREATE TABLE erp.pedido_item (
    id              BIGSERIAL PRIMARY KEY,
    pedido_id       BIGINT        NOT NULL REFERENCES erp.pedido(id) ON DELETE CASCADE,
    produto_id      BIGINT        NOT NULL REFERENCES fiscal.produto(id),
    quantidade      NUMERIC(15,4) NOT NULL CHECK (quantidade > 0),
    valor_unitario  NUMERIC(15,4) NOT NULL CHECK (valor_unitario >= 0),
    desconto        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (desconto >= 0)
);
CREATE INDEX ix_pedido_item_pedido ON erp.pedido_item (pedido_id);

-- Pedido concluído ou cancelado não muda mais (só a situação pode passar de ABERTO para outra).
CREATE FUNCTION erp.fn_pedido_imutavel() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_situacao VARCHAR;
BEGIN
    IF TG_TABLE_NAME = 'pedido' THEN
        IF OLD.situacao <> 'ABERTO' THEN
            RAISE EXCEPTION 'O pedido % já está %: não pode ser alterado nem excluído.', OLD.numero, lower(OLD.situacao) USING ERRCODE = 'P0001';
        END IF;
    ELSE
        SELECT situacao INTO v_situacao FROM erp.pedido WHERE id = COALESCE(NEW.pedido_id, OLD.pedido_id);
        IF v_situacao IS NOT NULL AND v_situacao <> 'ABERTO' THEN
            RAISE EXCEPTION 'O pedido já está %: os itens não podem mudar.', lower(v_situacao) USING ERRCODE = 'P0001';
        END IF;
    END IF;
    RETURN COALESCE(NEW, OLD);
END $$;
CREATE TRIGGER trg_pedido_imutavel BEFORE UPDATE OR DELETE ON erp.pedido FOR EACH ROW EXECUTE FUNCTION erp.fn_pedido_imutavel();
CREATE TRIGGER trg_pedido_item_imutavel BEFORE INSERT OR UPDATE OR DELETE ON erp.pedido_item FOR EACH ROW EXECUTE FUNCTION erp.fn_pedido_imutavel();

CREATE TABLE erp.estoque_movimento (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    produto_id      BIGINT        NOT NULL REFERENCES fiscal.produto(id),
    data            DATE          NOT NULL,
    tipo            VARCHAR(7)    NOT NULL,                         -- ENTRADA | SAIDA | AJUSTE
    quantidade      NUMERIC(15,4) NOT NULL CHECK (quantidade <> 0), -- com sinal: entrada +, saída -
    custo_unitario  NUMERIC(15,4) NOT NULL DEFAULT 0,
    pedido_id       BIGINT        REFERENCES erp.pedido(id) ON DELETE SET NULL,
    observacao      VARCHAR(300),
    usuario_id      BIGINT        REFERENCES core.usuario(id),
    criado_em       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT ck_estoque_tipo CHECK (tipo IN ('ENTRADA', 'SAIDA', 'AJUSTE')),
    CONSTRAINT ck_estoque_sinal CHECK ((tipo = 'ENTRADA' AND quantidade > 0) OR (tipo = 'SAIDA' AND quantidade < 0) OR tipo = 'AJUSTE')
);
CREATE INDEX ix_estoque_produto ON erp.estoque_movimento (empresa_id, produto_id, data);

CREATE TABLE erp.veiculo (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    placa           VARCHAR(7)    NOT NULL,
    renavam         VARCHAR(11),
    tipo            VARCHAR(20)   NOT NULL DEFAULT 'CAMINHAO',
    uf              CHAR(2)       NOT NULL,
    tara_kg         INTEGER       NOT NULL DEFAULT 0 CHECK (tara_kg >= 0),
    capacidade_kg   INTEGER       NOT NULL DEFAULT 0 CHECK (capacidade_kg >= 0),
    ativo           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_veiculo_placa UNIQUE (empresa_id, placa),
    CONSTRAINT ck_veiculo_placa CHECK (placa ~ '^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$')
);

CREATE TABLE erp.motorista (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    nome            VARCHAR(150)  NOT NULL,
    cpf             CHAR(11)      NOT NULL,
    cnh             VARCHAR(20),
    cnh_validade    DATE,
    ativo           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_motorista_cpf UNIQUE (empresa_id, cpf)
);

CREATE TABLE erp.documento_transporte (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    tipo            VARCHAR(4)    NOT NULL,                         -- CTE | MDFE
    numero          INTEGER       NOT NULL,
    serie           SMALLINT      NOT NULL DEFAULT 1,
    emissao         DATE          NOT NULL,
    chave           CHAR(44),
    remetente_id    BIGINT        REFERENCES core.participante(id),
    destinatario_id BIGINT        REFERENCES core.participante(id),
    uf_inicio       CHAR(2)       NOT NULL,
    uf_fim          CHAR(2)       NOT NULL,
    veiculo_id      BIGINT        REFERENCES erp.veiculo(id),
    motorista_id    BIGINT        REFERENCES erp.motorista(id),
    valor_frete     NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_frete >= 0),
    valor_carga     NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_carga >= 0),
    peso_kg         NUMERIC(15,3) NOT NULL DEFAULT 0 CHECK (peso_kg >= 0),
    chaves_nfe      TEXT,                                           -- NF-e transportadas (uma por linha)
    situacao        VARCHAR(10)   NOT NULL DEFAULT 'DIGITADO',
    observacao      VARCHAR(500),
    CONSTRAINT uq_doc_transporte UNIQUE (empresa_id, tipo, serie, numero),
    CONSTRAINT ck_doc_transporte_tipo CHECK (tipo IN ('CTE', 'MDFE')),
    CONSTRAINT ck_doc_transporte_situacao CHECK (situacao IN ('DIGITADO', 'AUTORIZADO', 'ENCERRADO', 'CANCELADO'))
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON erp.pedido FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON erp.estoque_movimento FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON erp.documento_transporte FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
