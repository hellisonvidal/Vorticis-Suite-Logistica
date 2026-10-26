-- =============================================================================
-- 061 — Cadastros financeiros da empresa (usados no Contábil, no Fiscal e no ERP; o Escritório não os vê):
--   * core.conta_bancaria: banco (código de compensação), agência, conta e a conta contábil do banco.
--   * core.forma_pagamento: meio (dinheiro, boleto, PIX, cartão...) e os prazos em dias de cada parcela
--     ("0" = à vista; "30/60/90" = três parcelas), com a conta bancária onde cai o dinheiro.
--   * fiscal.documento.forma_pagamento_id: forma escolhida na digitação da nota (gera as duplicatas).
--   Excluir a empresa leva os cadastros junto (ON DELETE CASCADE).
-- =============================================================================

CREATE TABLE core.conta_bancaria (
    id              BIGSERIAL     PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo          INTEGER       NOT NULL CHECK (codigo > 0),
    descricao       VARCHAR(80)   NOT NULL CHECK (btrim(descricao) <> ''),
    banco           VARCHAR(3)    NOT NULL CHECK (banco ~ '^[0-9]{3}$'),
    agencia         VARCHAR(10)   NOT NULL CHECK (agencia ~ '^[0-9]{1,6}(-[0-9Xx])?$'),
    conta           VARCHAR(20)   NOT NULL CHECK (conta ~ '^[0-9]{1,15}(-[0-9Xx]{1,2})?$'),
    conta_contabil  VARCHAR(30),
    ativa           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_conta_bancaria_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT uq_conta_bancaria_numero UNIQUE (empresa_id, banco, agencia, conta)
);

CREATE TABLE core.forma_pagamento (
    id                  BIGSERIAL     PRIMARY KEY,
    empresa_id          BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo              INTEGER       NOT NULL CHECK (codigo > 0),
    descricao           VARCHAR(80)   NOT NULL CHECK (btrim(descricao) <> ''),
    meio                VARCHAR(20)   NOT NULL DEFAULT 'BOLETO'
                        CHECK (meio IN ('DINHEIRO', 'BOLETO', 'PIX', 'CARTAO_CREDITO', 'CARTAO_DEBITO', 'CHEQUE', 'TRANSFERENCIA', 'OUTROS')),
    prazos              VARCHAR(200)  NOT NULL DEFAULT '0' CHECK (prazos ~ '^[0-9]{1,3}(/[0-9]{1,3}){0,119}$'),
    conta_bancaria_id   BIGINT        REFERENCES core.conta_bancaria(id) ON DELETE SET NULL,
    ativa               BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_forma_pagamento_codigo UNIQUE (empresa_id, codigo)
);

ALTER TABLE fiscal.documento
    ADD COLUMN forma_pagamento_id BIGINT REFERENCES core.forma_pagamento(id) ON DELETE SET NULL;

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.conta_bancaria FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.forma_pagamento FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
