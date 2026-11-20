-- =============================================================================
-- Vorticis Suite Logística — Transmissão de CT-e 4.00 (com IBS/CBS e SVC) e MDF-e 3.00 à SEFAZ
--   * tms.configuracao_sefaz: ambiente (1 Produção / 2 Homologação), RNTRC, contingência automática SVC-RS/SVC-SP e seguro da carga.
--   * tms.cte e tms.manifesto: ambiente, tipo_emissao (1 normal, 7 SVC-RS, 8 SVC-SP), tributos IBS/CBS (EC 132/2023), XMLs assinado/autorizado e protocolos.
--   * tms.transmissao_sefaz: histórico imutável (auditoria) de cada tentativa de autorização, cancelamento ou encerramento enviada à SEFAZ.
--   * core.parametro_legal: semente IBS_CBS_TRANSPORTE para as alíquotas de transição (2026: IBS UF 0,10% + CBS 0,90%).
-- =============================================================================

CREATE TABLE tms.configuracao_sefaz (
    empresa_id                  BIGINT      PRIMARY KEY REFERENCES core.empresa(id) ON DELETE CASCADE,
    ambiente                    SMALLINT    NOT NULL DEFAULT 2 CHECK (ambiente IN (1, 2)),
    rntrc                       VARCHAR(9)  CHECK (rntrc IS NULL OR rntrc ~ '^[0-9]{8,9}$'),
    contingencia_svc_automatica BOOLEAN     NOT NULL DEFAULT TRUE,
    seguradora_nome             VARCHAR(60),
    seguradora_cnpj             CHAR(14)    CHECK (seguradora_cnpj IS NULL OR seguradora_cnpj ~ '^[0-9]{14}$'),
    apolice_seguro              VARCHAR(40),
    averbacao_padrao            VARCHAR(40)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON tms.configuracao_sefaz
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

ALTER TABLE tms.cte
    ADD COLUMN ambiente               SMALLINT      CHECK (ambiente IS NULL OR ambiente IN (1, 2)),
    ADD COLUMN tipo_emissao           SMALLINT      NOT NULL DEFAULT 1 CHECK (tipo_emissao IN (1, 7, 8)),
    ADD COLUMN ibs_uf_pct             NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (ibs_uf_pct BETWEEN 0 AND 99),
    ADD COLUMN ibs_mun_pct            NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (ibs_mun_pct BETWEEN 0 AND 99),
    ADD COLUMN ibs                    NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (ibs >= 0),
    ADD COLUMN cbs_pct                NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (cbs_pct BETWEEN 0 AND 99),
    ADD COLUMN cbs                    NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (cbs >= 0),
    ADD COLUMN cst_ibs_cbs            VARCHAR(3),
    ADD COLUMN class_trib_ibs_cbs     VARCHAR(10),
    ADD COLUMN xml_assinado           TEXT,
    ADD COLUMN xml_autorizado         TEXT,
    ADD COLUMN protocolo_cancelamento VARCHAR(20),
    ADD COLUMN autorizado_em          TIMESTAMPTZ;

ALTER TABLE tms.manifesto
    ADD COLUMN protocolo              VARCHAR(20),
    ADD COLUMN protocolo_encerramento VARCHAR(20),
    ADD COLUMN protocolo_cancelamento VARCHAR(20),
    ADD COLUMN ambiente               SMALLINT      CHECK (ambiente IS NULL OR ambiente IN (1, 2)),
    ADD COLUMN averbacao              VARCHAR(40),
    ADD COLUMN xml_assinado           TEXT,
    ADD COLUMN xml_autorizado         TEXT,
    ADD COLUMN autorizado_em          TIMESTAMPTZ;

CREATE TABLE tms.transmissao_sefaz (
    id              BIGSERIAL    PRIMARY KEY,
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    modelo          CHAR(2)      NOT NULL CHECK (modelo IN ('57', '58')),
    documento_id    BIGINT       NOT NULL,
    acao            VARCHAR(16)  NOT NULL CHECK (acao IN ('AUTORIZACAO', 'CANCELAMENTO', 'ENCERRAMENTO')),
    ambiente        SMALLINT     NOT NULL CHECK (ambiente IN (1, 2)),
    tipo_emissao    SMALLINT     NOT NULL DEFAULT 1 CHECK (tipo_emissao IN (1, 7, 8)),
    contingencia_svc BOOLEAN     NOT NULL DEFAULT FALSE,
    chave           CHAR(44)     CHECK (chave IS NULL OR chave ~ '^[0-9]{44}$'),
    cstat           INTEGER,
    motivo          VARCHAR(500),
    protocolo       VARCHAR(20),
    xml_envio       TEXT         NOT NULL,
    xml_retorno     TEXT,
    enviado_em      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    enviado_por     BIGINT       REFERENCES core.usuario(id) ON DELETE SET NULL
);

CREATE INDEX ix_tms_transmissao_doc ON tms.transmissao_sefaz (empresa_id, modelo, documento_id, enviado_em DESC);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON tms.transmissao_sefaz
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.parametro_legal (chave, vigencia_inicio, valor, fundamento_legal) VALUES
    ('IBS_CBS_TRANSPORTE', DATE '2026-01-01',
     '{"cst": "000", "cClassTrib": "000001", "ibsUfPct": 0.10, "ibsMunPct": 0.00, "cbsPct": 0.90, "incluirNoXml": true}',
     'EC 132/2023, LC 214/2025 e NT 2025.001 CT-e 4.00 (alíquotas de transição 2026: IBS UF 0,10% + CBS 0,90%, grupo IBSCBS)')
ON CONFLICT (chave, vigencia_inicio) DO NOTHING;
