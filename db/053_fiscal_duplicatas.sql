-- =============================================================================
-- Vorticis Fiscal — Migração 053: duplicatas (contas a pagar e a receber originadas das notas)
--   * fiscal.duplicata: parcela a pagar (nota de entrada) ou a receber (nota de saída), com origem opcional no documento
--     fiscal, participante, vencimento, valor, totais baixados de juros/multa/desconto, saldo e situação
--     (ABERTA, PARCIAL, LIQUIDADA, CANCELADA). Saldo e situação são mantidos pelo banco a partir das baixas.
--   * fiscal.duplicata_baixa: pagamento/recebimento total ou parcial (principal, juros, multa, desconto), conta de
--     disponível/banco, lançamento contábil gerado e, na conciliação com o extrato, a partida bancária e a conciliação.
--   * Nota cancelada cancela as duplicatas ainda abertas; nota com duplicata baixada não pode ser excluída.
--   * fiscal.configuracao: geração de duplicata à vista e contas contábeis das baixas (código da conta analítica).
--   * Funções FISCAL_DUPLICATAS_MANTER (Administrador, Contador e Assistente) e FISCAL_DUPLICATAS_BAIXAR
--     (Administrador e Contador).
-- =============================================================================

ALTER TABLE fiscal.configuracao
    ADD COLUMN duplicata_a_vista        BOOLEAN      NOT NULL DEFAULT TRUE,   -- nota sem cobrança no XML gera uma parcela à vista
    ADD COLUMN duplicata_a_vista_dias   SMALLINT     NOT NULL DEFAULT 0 CHECK (duplicata_a_vista_dias BETWEEN 0 AND 365),
    ADD COLUMN conta_fornecedores       VARCHAR(30),
    ADD COLUMN conta_clientes           VARCHAR(30),
    ADD COLUMN conta_juros_pagos        VARCHAR(30),
    ADD COLUMN conta_multa_paga         VARCHAR(30),
    ADD COLUMN conta_desconto_obtido    VARCHAR(30),
    ADD COLUMN conta_juros_recebidos    VARCHAR(30),
    ADD COLUMN conta_multa_recebida     VARCHAR(30),
    ADD COLUMN conta_desconto_concedido VARCHAR(30),
    ADD COLUMN historico_pagamento      VARCHAR(200) NOT NULL DEFAULT 'Pagamento NF {DOCUMENTO} - {PARTICIPANTE}',
    ADD COLUMN historico_recebimento    VARCHAR(200) NOT NULL DEFAULT 'Recebimento NF {DOCUMENTO} - {PARTICIPANTE}';

CREATE TABLE fiscal.duplicata (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    documento_id     BIGINT        REFERENCES fiscal.documento (id) ON DELETE CASCADE,   -- nota de origem (nulo = lançada à mão)
    participante_id  BIGINT        REFERENCES core.participante (id) ON DELETE SET NULL,
    tipo             VARCHAR(7)    NOT NULL CHECK (tipo IN ('PAGAR', 'RECEBER')),
    parcela          VARCHAR(20)   NOT NULL,
    numero_documento VARCHAR(30)   NOT NULL,
    emissao          DATE          NOT NULL,
    vencimento       DATE          NOT NULL,
    valor            NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    juros            NUMERIC(15,2) NOT NULL DEFAULT 0,     -- totais das baixas
    multa            NUMERIC(15,2) NOT NULL DEFAULT 0,
    desconto         NUMERIC(15,2) NOT NULL DEFAULT 0,
    saldo            NUMERIC(15,2) NOT NULL,
    situacao         VARCHAR(9)    NOT NULL DEFAULT 'ABERTA' CHECK (situacao IN ('ABERTA', 'PARCIAL', 'LIQUIDADA', 'CANCELADA')),
    observacao       VARCHAR(500),
    origem           VARCHAR(6)    NOT NULL DEFAULT 'MANUAL' CHECK (origem IN ('NOTA', 'MANUAL')),
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por       BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    atualizado_em    TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT ck_duplicata_saldo CHECK (saldo >= 0 AND saldo <= valor),
    CONSTRAINT ck_duplicata_vencimento CHECK (vencimento >= emissao)
);
CREATE UNIQUE INDEX uq_duplicata_parcela ON fiscal.duplicata (documento_id, parcela) WHERE documento_id IS NOT NULL;
CREATE INDEX ix_duplicata_empresa_venc ON fiscal.duplicata (empresa_id, tipo, vencimento);
CREATE INDEX ix_duplicata_participante ON fiscal.duplicata (participante_id);

CREATE TABLE fiscal.duplicata_baixa (
    id                BIGSERIAL     PRIMARY KEY,
    duplicata_id      BIGINT        NOT NULL REFERENCES fiscal.duplicata (id) ON DELETE CASCADE,
    data              DATE          NOT NULL,
    valor_principal   NUMERIC(15,2) NOT NULL CHECK (valor_principal > 0),
    juros             NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (juros >= 0),
    multa             NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (multa >= 0),
    desconto          NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (desconto >= 0),
    conta_disponivel  VARCHAR(30),                                                      -- conta do banco/caixa (código)
    lancamento_id     BIGINT        REFERENCES contabil.lancamento (id) ON DELETE SET NULL,
    partida_extrato_id BIGINT       REFERENCES contabil.partida (id) ON DELETE SET NULL,   -- movimento do extrato (OFX) conciliado
    conciliacao_id    BIGINT        REFERENCES contabil.conciliacao (id) ON DELETE SET NULL,
    criado_em         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por        BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL
);
CREATE INDEX ix_duplicata_baixa_dup ON fiscal.duplicata_baixa (duplicata_id);
CREATE UNIQUE INDEX uq_duplicata_baixa_extrato ON fiscal.duplicata_baixa (partida_extrato_id) WHERE partida_extrato_id IS NOT NULL;

-- Saldo, totais e situação saem das baixas.
CREATE OR REPLACE FUNCTION fiscal.fn_duplicata_recalcular(p_duplicata BIGINT)
RETURNS VOID LANGUAGE plpgsql AS $$
DECLARE
    v_principal NUMERIC(15,2);
    v_valor     NUMERIC(15,2);
    v_situacao  VARCHAR(9);
BEGIN
    SELECT valor, situacao INTO v_valor, v_situacao FROM fiscal.duplicata WHERE id = p_duplicata;
    IF NOT FOUND THEN RETURN; END IF;
    SELECT COALESCE(SUM(valor_principal), 0) INTO v_principal FROM fiscal.duplicata_baixa WHERE duplicata_id = p_duplicata;
    IF v_principal > v_valor THEN
        RAISE EXCEPTION 'As baixas (%) superam o valor da duplicata (%)', v_principal, v_valor;
    END IF;
    UPDATE fiscal.duplicata d
       SET saldo = v_valor - v_principal,
           juros = COALESCE((SELECT SUM(juros) FROM fiscal.duplicata_baixa WHERE duplicata_id = p_duplicata), 0),
           multa = COALESCE((SELECT SUM(multa) FROM fiscal.duplicata_baixa WHERE duplicata_id = p_duplicata), 0),
           desconto = COALESCE((SELECT SUM(desconto) FROM fiscal.duplicata_baixa WHERE duplicata_id = p_duplicata), 0),
           situacao = CASE WHEN v_situacao = 'CANCELADA' THEN 'CANCELADA'
                           WHEN v_principal = 0 THEN 'ABERTA'
                           WHEN v_principal = v_valor THEN 'LIQUIDADA'
                           ELSE 'PARCIAL' END,
           atualizado_em = now()
     WHERE d.id = p_duplicata;
END $$;

CREATE OR REPLACE FUNCTION fiscal.fn_duplicata_baixa_trg()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM fiscal.fn_duplicata_recalcular(OLD.duplicata_id);
        RETURN OLD;
    END IF;
    IF NEW.data IS NULL THEN RAISE EXCEPTION 'Informe a data da baixa'; END IF;
    PERFORM fiscal.fn_duplicata_recalcular(NEW.duplicata_id);
    RETURN NEW;
END $$;

CREATE TRIGGER trg_duplicata_baixa AFTER INSERT OR UPDATE OR DELETE ON fiscal.duplicata_baixa
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_duplicata_baixa_trg();

-- Duplicata cancelada não recebe baixa; o valor só muda sem baixas (o saldo é recalculado a partir das baixas).
CREATE OR REPLACE FUNCTION fiscal.fn_duplicata_regras()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        NEW.saldo := NEW.valor;
        RETURN NEW;
    END IF;
    IF NEW.valor <> OLD.valor THEN
        IF EXISTS (SELECT 1 FROM fiscal.duplicata_baixa WHERE duplicata_id = OLD.id) THEN
            RAISE EXCEPTION 'A duplicata % já tem baixa: estorne as baixas para alterar o valor', OLD.numero_documento;
        END IF;
        NEW.saldo := NEW.valor;
    END IF;
    IF NEW.situacao = 'CANCELADA' AND OLD.situacao <> 'CANCELADA'
       AND EXISTS (SELECT 1 FROM fiscal.duplicata_baixa WHERE duplicata_id = OLD.id) THEN
        RAISE EXCEPTION 'A duplicata % tem baixa: estorne as baixas antes de cancelar', OLD.numero_documento;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_duplicata_regras BEFORE INSERT OR UPDATE ON fiscal.duplicata
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_duplicata_regras();

CREATE OR REPLACE FUNCTION fiscal.fn_duplicata_baixa_valida()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM fiscal.duplicata WHERE id = NEW.duplicata_id AND situacao = 'CANCELADA') THEN
        RAISE EXCEPTION 'Duplicata cancelada não pode ser baixada';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_duplicata_baixa_valida BEFORE INSERT ON fiscal.duplicata_baixa
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_duplicata_baixa_valida();

-- Nota cancelada: cancela as duplicatas ainda abertas (sem baixa).
CREATE OR REPLACE FUNCTION fiscal.fn_documento_cancela_duplicatas()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.situacao = 'CANCELADA' AND OLD.situacao IS DISTINCT FROM 'CANCELADA' THEN
        UPDATE fiscal.duplicata SET situacao = 'CANCELADA', atualizado_em = now()
         WHERE documento_id = NEW.id AND situacao = 'ABERTA';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_documento_cancela_duplicatas AFTER UPDATE OF situacao ON fiscal.documento
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_documento_cancela_duplicatas();

-- Nota com duplicata baixada não pode ser excluída (a exclusão da empresa, que remove tudo, é permitida).
CREATE OR REPLACE FUNCTION fiscal.fn_documento_protege_duplicatas()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM fiscal.duplicata_baixa b JOIN fiscal.duplicata d ON d.id = b.duplicata_id WHERE d.documento_id = OLD.id)
       AND current_setting('perseus.excluindo_empresa', true) IS DISTINCT FROM OLD.empresa_id::text THEN
        RAISE EXCEPTION 'A nota % tem duplicata baixada: estorne as baixas antes de excluí-la', OLD.numero;
    END IF;
    RETURN OLD;
END $$;

CREATE TRIGGER trg_documento_protege_duplicatas BEFORE DELETE ON fiscal.documento
    FOR EACH ROW EXECUTE FUNCTION fiscal.fn_documento_protege_duplicatas();

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.duplicata FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.duplicata_baixa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('FISCAL_DUPLICATAS_MANTER'), ('FISCAL_DUPLICATAS_BAIXAR')) AS x(permissao) ON TRUE
 WHERE p.administrador OR (p.padrao AND p.nome = 'Contador')
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'FISCAL_DUPLICATAS_MANTER'
  FROM core.perfil p
 WHERE p.padrao AND p.nome = 'Assistente'
ON CONFLICT DO NOTHING;
