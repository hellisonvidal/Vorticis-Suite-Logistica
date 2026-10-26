-- =============================================================================
-- 080 — Regras fiscais: de/para de CFOP das notas de entrada E de saída, aplicado sozinho na importação do XML.
--   Entrada: o CFOP de saída do emitente (5/6/7, completo ou só o início) vira o CFOP de entrada da escrituração (1/2/3).
--   Saída: o CFOP do XML da própria empresa (5/6/7) vira o CFOP de saída da escrituração (5/6/7).
--   Os CSTs (ICMS, PIS e COFINS) são opcionais: preenchidos, substituem os do XML no item.
--   Substitui as naturezas de entrada (075), que viram regras de entrada com o mesmo código.
-- =============================================================================
CREATE TABLE fiscal.regra_fiscal (
    id             BIGSERIAL     PRIMARY KEY,
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo         INTEGER       NOT NULL CHECK (codigo > 0),
    descricao      VARCHAR(100)  NOT NULL,
    operacao       CHAR(1)       NOT NULL CHECK (operacao IN ('E', 'S')),
    cfop_origem    VARCHAR(4)    NOT NULL CHECK (cfop_origem ~ '^[567][0-9]{0,3}$'),
    cfop_destino   CHAR(4)       NOT NULL CHECK (cfop_destino ~ '^[1-7][0-9]{3}$'),
    cst_icms       VARCHAR(3)    CHECK (cst_icms ~ '^[0-9]{2,3}$'),
    cst_pis        CHAR(2)       CHECK (cst_pis ~ '^[0-9]{2}$'),
    cst_cofins     CHAR(2)       CHECK (cst_cofins ~ '^[0-9]{2}$'),
    ativa          BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_regra_fiscal_destino CHECK (operacao = 'E' AND cfop_destino ~ '^[123]' OR operacao = 'S' AND cfop_destino ~ '^[567]'),
    CONSTRAINT uq_regra_fiscal_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT uq_regra_fiscal_cfop UNIQUE (empresa_id, operacao, cfop_origem)
);

INSERT INTO fiscal.regra_fiscal (empresa_id, codigo, descricao, operacao, cfop_origem, cfop_destino, ativa)
SELECT empresa_id, codigo, descricao, 'E', cfop_saida, cfop_entrada, ativa FROM fiscal.natureza_entrada;

DROP TABLE fiscal.natureza_entrada;
