-- =============================================================================
-- Vorticis — Migração 027: procurador da empresa e certificado padrão do procurador
--   * core.procurador: pessoa física com procuração para assinar (e transmitir) a ECD e a ECF pela empresa
--     (qualificação 309 – Procurador, nos registros J930 da ECD e 0930 da ECF). Guarda a vigência da
--     procuração; a procuração eletrônica em si é cadastrada no e-CAC.
--   * core.certificado_digital.padrao_procurador: certificado que assina, por padrão, como procurador
--     (um por empresa, ou um geral), como os padrões do contador e do responsável (migração 029).
-- =============================================================================

CREATE TABLE core.procurador (
    id               BIGSERIAL    PRIMARY KEY,
    empresa_id       BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    nome             VARCHAR(150) NOT NULL,
    cpf              CHAR(11)     NOT NULL,
    email            VARCHAR(150),
    telefone         VARCHAR(30),
    vigencia_inicio  DATE         NOT NULL,
    vigencia_fim     DATE,                                   -- nulo = prazo indeterminado
    observacao       VARCHAR(200),                           -- ex.: número/identificação da procuração no e-CAC
    CONSTRAINT ck_procurador_cpf CHECK (cpf ~ '^[0-9]{11}$'),
    CONSTRAINT ck_procurador_vigencia CHECK (vigencia_fim IS NULL OR vigencia_fim >= vigencia_inicio)
);
CREATE INDEX ix_procurador_empresa ON core.procurador (empresa_id);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.procurador
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

ALTER TABLE core.certificado_digital
    ADD COLUMN padrao_procurador BOOLEAN NOT NULL DEFAULT FALSE;

CREATE UNIQUE INDEX ux_certificado_padrao_procurador
    ON core.certificado_digital ((COALESCE(empresa_id, 0))) WHERE padrao_procurador;
