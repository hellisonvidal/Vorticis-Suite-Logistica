-- =============================================================================
-- Vorticis — Migração 029: certificado padrão para assinar a ECD/ECF e relatórios em PDF
--   * padrao_contador / padrao_responsavel: o certificado que assina, por padrão, como contador
--     e como responsável (sócio ou e-CNPJ). Um só por papel e por escopo: com empresa_id, vale
--     para aquela empresa; sem empresa_id, vale para todas as que não tiverem o seu.
-- =============================================================================

ALTER TABLE core.certificado_digital
    ADD COLUMN padrao_contador    BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN padrao_responsavel BOOLEAN NOT NULL DEFAULT FALSE;

CREATE UNIQUE INDEX ux_certificado_padrao_contador
    ON core.certificado_digital ((COALESCE(empresa_id, 0))) WHERE padrao_contador;
CREATE UNIQUE INDEX ux_certificado_padrao_responsavel
    ON core.certificado_digital ((COALESCE(empresa_id, 0))) WHERE padrao_responsavel;
