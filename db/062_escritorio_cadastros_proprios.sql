-- =============================================================================
-- 062 — Escritório com cadastros próprios: o módulo é o gerenciador do escritório contábil e não compartilha
-- os seus cadastros com os demais módulos.
--   * escritorio.pessoa: clientes e fornecedores DO ESCRITÓRIO (não são core.empresa nem core.participante).
--     empresa_id passa a ser o escritório dono dos dados (a empresa ativa no módulo Escritório).
--   * Tarefas, protocolos e honorários passam a apontar para o cliente do escritório (pessoa_id).
--     Os dados existentes são preservados: cada empresa que já tinha registros vira um cliente próprio dela mesma.
--   * escritorio.contrato: contratos gerados para clientes (prestação de serviços contábeis) e fornecedores.
-- =============================================================================

CREATE TABLE escritorio.pessoa (
    id          BIGSERIAL     PRIMARY KEY,
    empresa_id  BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo      INTEGER       NOT NULL CHECK (codigo > 0),
    cliente     BOOLEAN       NOT NULL DEFAULT TRUE,
    fornecedor  BOOLEAN       NOT NULL DEFAULT FALSE,
    nome        VARCHAR(150)  NOT NULL CHECK (btrim(nome) <> ''),
    documento   VARCHAR(14)   CHECK (documento IS NULL OR documento ~ '^([0-9]{11}|[0-9]{14})$'),
    regime      VARCHAR(30),
    contato     VARCHAR(100),
    email       VARCHAR(150),
    telefone    VARCHAR(30),
    endereco    VARCHAR(300),
    municipio   VARCHAR(100),
    uf          CHAR(2),
    ativo       BOOLEAN       NOT NULL DEFAULT TRUE,
    observacao  VARCHAR(500),
    CONSTRAINT uq_pessoa_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_pessoa_papel CHECK (cliente OR fornecedor)
);
CREATE UNIQUE INDEX uq_pessoa_documento ON escritorio.pessoa (empresa_id, documento) WHERE documento IS NOT NULL;

INSERT INTO escritorio.pessoa (empresa_id, codigo, nome, documento)
SELECT e.id, 1, e.razao_social, CASE WHEN btrim(e.cnpj) ~ '^([0-9]{11}|[0-9]{14})$' THEN btrim(e.cnpj) END
  FROM core.empresa e
 WHERE e.id IN (SELECT empresa_id FROM escritorio.tarefa UNION SELECT empresa_id FROM escritorio.honorario_contrato
                UNION SELECT empresa_id FROM escritorio.honorario_cobranca UNION SELECT empresa_id FROM escritorio.protocolo);

ALTER TABLE escritorio.tarefa ADD COLUMN pessoa_id BIGINT REFERENCES escritorio.pessoa(id);
ALTER TABLE escritorio.protocolo ADD COLUMN pessoa_id BIGINT REFERENCES escritorio.pessoa(id);
ALTER TABLE escritorio.honorario_contrato ADD COLUMN pessoa_id BIGINT REFERENCES escritorio.pessoa(id);
ALTER TABLE escritorio.honorario_cobranca ADD COLUMN pessoa_id BIGINT REFERENCES escritorio.pessoa(id);

UPDATE escritorio.tarefa x SET pessoa_id = p.id FROM escritorio.pessoa p WHERE p.empresa_id = x.empresa_id;
UPDATE escritorio.protocolo x SET pessoa_id = p.id FROM escritorio.pessoa p WHERE p.empresa_id = x.empresa_id;
UPDATE escritorio.honorario_contrato x SET pessoa_id = p.id FROM escritorio.pessoa p WHERE p.empresa_id = x.empresa_id;
UPDATE escritorio.honorario_cobranca x SET pessoa_id = p.id FROM escritorio.pessoa p WHERE p.empresa_id = x.empresa_id;

ALTER TABLE escritorio.honorario_contrato
    ALTER COLUMN pessoa_id SET NOT NULL,
    DROP CONSTRAINT honorario_contrato_empresa_id_key,
    ADD CONSTRAINT uq_honorario_contrato_pessoa UNIQUE (pessoa_id);
ALTER TABLE escritorio.honorario_cobranca
    ALTER COLUMN pessoa_id SET NOT NULL,
    DROP CONSTRAINT uq_honorario_cobranca,
    ADD CONSTRAINT uq_honorario_cobranca_pessoa UNIQUE (pessoa_id, competencia);
CREATE INDEX ix_tarefa_pessoa ON escritorio.tarefa (pessoa_id);

CREATE TABLE escritorio.contrato (
    id              BIGSERIAL     PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero          INTEGER       NOT NULL,
    pessoa_id       BIGINT        NOT NULL REFERENCES escritorio.pessoa(id),
    modelo          VARCHAR(10)   NOT NULL CHECK (modelo IN ('CLIENTE', 'FORNECEDOR')),
    objeto          VARCHAR(500)  NOT NULL CHECK (btrim(objeto) <> ''),
    valor           NUMERIC(15,2) NOT NULL CHECK (valor >= 0),
    periodicidade   VARCHAR(7)    NOT NULL DEFAULT 'MENSAL' CHECK (periodicidade IN ('MENSAL', 'ANUAL', 'UNICO')),
    dia_vencimento  SMALLINT      CHECK (dia_vencimento BETWEEN 1 AND 31),
    inicio          DATE          NOT NULL,
    fim             DATE,
    indice_reajuste VARCHAR(10)   NOT NULL DEFAULT 'NENHUM' CHECK (indice_reajuste IN ('NENHUM', 'IPCA', 'IGPM', 'INPC', 'SALARIO')),
    foro            VARCHAR(100),
    texto           TEXT          NOT NULL,
    situacao        VARCHAR(9)    NOT NULL DEFAULT 'ATIVO' CHECK (situacao IN ('ATIVO', 'ENCERRADO')),
    criado_em       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_contrato_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_contrato_fim CHECK (fim IS NULL OR fim >= inicio)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON escritorio.pessoa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON escritorio.contrato FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
