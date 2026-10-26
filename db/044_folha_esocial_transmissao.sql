-- =============================================================================
-- Vorticis — Migração 044
--   Folha › eSocial: assinatura e transmissão dos eventos. Os eventos passam a ter os status
--   GERADO → ASSINADO → ENVIADO → PROCESSADO | ERRO (EXPORTADO continua valendo para quem só grava os XMLs em arquivo).
--   Cada envio é um lote (até 50 eventos de um mesmo grupo) com protocolo, retornos brutos e ocorrências.
-- =============================================================================

CREATE TABLE folha.esocial_lote (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    ambiente            SMALLINT     NOT NULL,                  -- 1 produção | 2 produção restrita
    grupo               SMALLINT     NOT NULL,                  -- 1 tabelas | 2 não periódicos | 3 periódicos
    transmissor_papel   VARCHAR(12)  NOT NULL,                  -- EMPREGADOR | PROCURADOR | CONTADOR
    transmissor_tp_insc SMALLINT     NOT NULL,                  -- 1 CNPJ | 2 CPF
    transmissor_nr_insc VARCHAR(14)  NOT NULL,
    protocolo           VARCHAR(40),                            -- protocoloEnvio devolvido pelo eSocial (A.B.NNNNNNNNNNNNNNNNNNN)
    status              VARCHAR(10)  NOT NULL DEFAULT 'ENVIADO',-- ENVIADO | PROCESSADO | ERRO
    codigo_resposta     INTEGER,                                -- cdResposta da última resposta (envio ou consulta)
    descricao_resposta  VARCHAR(2048),
    ocorrencias         JSONB,
    xml_envio           TEXT         NOT NULL,
    xml_retorno_envio   TEXT,
    xml_retorno_consulta TEXT,
    enviado_em          TIMESTAMPTZ  NOT NULL DEFAULT now(),
    enviado_por         BIGINT       REFERENCES core.usuario(id),
    consultado_em       TIMESTAMPTZ,
    CONSTRAINT ck_folha_lote_ambiente CHECK (ambiente IN (1, 2)),
    CONSTRAINT ck_folha_lote_grupo CHECK (grupo IN (1, 2, 3)),
    CONSTRAINT ck_folha_lote_papel CHECK (transmissor_papel IN ('EMPREGADOR', 'PROCURADOR', 'CONTADOR')),
    CONSTRAINT ck_folha_lote_status CHECK (status IN ('ENVIADO', 'PROCESSADO', 'ERRO'))
);
CREATE INDEX ix_folha_lote_empresa ON folha.esocial_lote (empresa_id, enviado_em DESC);
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.esocial_lote
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

ALTER TABLE folha.esocial_evento
    DROP CONSTRAINT ck_folha_esocial_status,
    ADD COLUMN xml_assinado     TEXT,
    ADD COLUMN assinado_em      TIMESTAMPTZ,
    ADD COLUMN assinado_por     BIGINT       REFERENCES core.usuario(id),
    ADD COLUMN ambiente         SMALLINT,                       -- ambiente do XML assinado (tpAmb)
    ADD COLUMN lote_id          BIGINT       REFERENCES folha.esocial_lote(id) ON DELETE SET NULL,
    ADD COLUMN recibo           VARCHAR(40),                    -- nrRecibo (evento aceito)
    ADD COLUMN codigo_resposta  INTEGER,                        -- cdResposta do evento (201 sucesso, 202 advertência, 4xx rejeitado)
    ADD COLUMN ocorrencias      JSONB,
    ADD COLUMN processado_em    TIMESTAMPTZ,
    ADD CONSTRAINT ck_folha_esocial_status CHECK (status IN ('GERADO', 'ASSINADO', 'ENVIADO', 'PROCESSADO', 'ERRO', 'EXPORTADO')),
    ADD CONSTRAINT ck_folha_esocial_ambiente CHECK (ambiente IS NULL OR ambiente IN (1, 2));
CREATE INDEX ix_folha_esocial_lote ON folha.esocial_evento (lote_id);
