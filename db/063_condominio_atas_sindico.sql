-- =============================================================================
-- 063 — Condomínio: atas de assembleia e síndico do condomínio
--   * condominio.assembleia: assembleias (ordinária/extraordinária) com mesa, pauta e deliberações; a ata sai em
--     Relatórios › Atas de Assembleia, com o bloco de assinaturas (mesa, síndico e lista de presença).
--   * core.empresa_sindico: síndico do condomínio (aba Síndico de Cadastros › Empresas no módulo Condomínio), com as
--     opções de assinar os livros contábeis e fiscais (assinatura comum e digital).
-- =============================================================================

CREATE TABLE condominio.assembleia (
    id              BIGSERIAL     PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero          INTEGER       NOT NULL,
    data            DATE          NOT NULL,
    horario         VARCHAR(5)    NOT NULL DEFAULT '19:00' CHECK (horario ~ '^[0-2][0-9]:[0-5][0-9]$'),
    tipo            VARCHAR(14)   NOT NULL DEFAULT 'ORDINARIA' CHECK (tipo IN ('ORDINARIA', 'EXTRAORDINARIA')),
    convocacao      SMALLINT      NOT NULL DEFAULT 1 CHECK (convocacao IN (1, 2)),
    local           VARCHAR(150)  NOT NULL CHECK (btrim(local) <> ''),
    presidente      VARCHAR(150)  NOT NULL CHECK (btrim(presidente) <> ''),
    secretario      VARCHAR(150)  NOT NULL CHECK (btrim(secretario) <> ''),
    unidades_presentes INTEGER    NOT NULL DEFAULT 0 CHECK (unidades_presentes >= 0),
    pauta           TEXT          NOT NULL CHECK (btrim(pauta) <> ''),
    deliberacoes    TEXT          NOT NULL DEFAULT '',
    criado_em       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_assembleia_numero UNIQUE (empresa_id, numero)
);

CREATE TABLE core.empresa_sindico (
    empresa_id          BIGINT        PRIMARY KEY REFERENCES core.empresa(id) ON DELETE CASCADE,
    nome                VARCHAR(150)  NOT NULL CHECK (btrim(nome) <> ''),
    cpf                 CHAR(11)      CHECK (cpf IS NULL OR cpf ~ '^[0-9]{11}$'),
    email               VARCHAR(150),
    telefone            VARCHAR(30),
    mandato_inicio      DATE,
    mandato_fim         DATE,
    assina_livros       BOOLEAN       NOT NULL DEFAULT FALSE,
    assinatura_digital  BOOLEAN       NOT NULL DEFAULT FALSE,
    CONSTRAINT ck_sindico_mandato CHECK (mandato_fim IS NULL OR mandato_inicio IS NULL OR mandato_fim >= mandato_inicio)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON condominio.assembleia FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.empresa_sindico FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
