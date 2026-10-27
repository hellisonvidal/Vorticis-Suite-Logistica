-- =============================================================================
-- Vorticis Suite Logística — Monitor de multas de trânsito (módulo Gerencial › Monitores)
--   * Cada notificação de autuação/penalidade recebida: veículo, condutor, auto de infração, órgão autuador, enquadramento,
--     gravidade, pontos, valor, prazos (indicação do condutor, defesa/recurso) e vencimento do boleto.
--   * Quem paga: a empresa ou o motorista (desconto no acerto/folha); situação do acompanhamento.
--   * Pontos e valores por gravidade (sugestão ao lançar) ficam em core.parametro_legal, chave CTB_INFRACOES.
--   Não há consulta automática ao Detran/SENATRAN: as multas são lançadas ou importadas de planilha.
-- =============================================================================

CREATE TABLE frota.multa (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    veiculo_id         BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    motorista_id       BIGINT        REFERENCES frota.motorista(id),
    auto_infracao      VARCHAR(30)   NOT NULL,
    orgao              VARCHAR(80),
    data_infracao      DATE          NOT NULL,
    local_infracao     VARCHAR(150),
    enquadramento      VARCHAR(10),
    descricao          VARCHAR(200),
    gravidade          VARCHAR(10)   NOT NULL,
    pontos             SMALLINT      NOT NULL DEFAULT 0 CHECK (pontos BETWEEN 0 AND 99),
    valor              NUMERIC(15,2) NOT NULL CHECK (valor >= 0),
    desconto           NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (desconto BETWEEN 0 AND 100),
    vencimento         DATE,
    prazo_indicacao    DATE,
    prazo_recurso      DATE,
    responsavel        VARCHAR(9)    NOT NULL DEFAULT 'EMPRESA',
    situacao           VARCHAR(17)   NOT NULL DEFAULT 'NOTIFICADA',
    data_pagamento     DATE,
    valor_pago         NUMERIC(15,2) CHECK (valor_pago IS NULL OR valor_pago >= 0),
    observacao         VARCHAR(300),
    CONSTRAINT uq_multa UNIQUE (empresa_id, auto_infracao),
    CONSTRAINT ck_multa_gravidade CHECK (gravidade IN ('LEVE', 'MEDIA', 'GRAVE', 'GRAVISSIMA')),
    CONSTRAINT ck_multa_responsavel CHECK (responsavel IN ('EMPRESA', 'MOTORISTA')),
    CONSTRAINT ck_multa_situacao CHECK (situacao IN ('NOTIFICADA', 'CONDUTOR_INDICADO', 'EM_RECURSO', 'PAGA', 'CANCELADA')),
    CONSTRAINT ck_multa_paga CHECK (situacao <> 'PAGA' OR data_pagamento IS NOT NULL),
    CONSTRAINT ck_multa_indicada CHECK (situacao <> 'CONDUTOR_INDICADO' OR motorista_id IS NOT NULL)
);
CREATE INDEX ix_multa_empresa_data ON frota.multa (empresa_id, data_infracao);

-- Pontos (art. 259) e valores (art. 258, Lei 13.281/2016) por gravidade; descontos do art. 284 (20% até o vencimento; 40% com adesão ao SNE).
INSERT INTO core.parametro_legal (chave, vigencia_inicio, valor, fundamento_legal) VALUES
    ('CTB_INFRACOES', DATE '2016-11-01',
     '{"LEVE": {"pontos": 3, "valor": 88.38}, "MEDIA": {"pontos": 4, "valor": 130.16}, "GRAVE": {"pontos": 5, "valor": 195.23},
       "GRAVISSIMA": {"pontos": 7, "valor": 293.47}, "desconto_vencimento": 20, "desconto_sne": 40}',
     'CTB (Lei 9.503/1997), arts. 258, 259 e 284, com a redação da Lei 13.281/2016')
ON CONFLICT (chave, vigencia_inicio) DO NOTHING;
