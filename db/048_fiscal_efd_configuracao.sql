-- =============================================================================
-- Vorticis Fiscal — Migração 048: configuração da escrituração SPED (EFD) por empresa
--   Guarda as escolhas que se repetem a cada mês (perfil e atividade da EFD ICMS/IPI, código de receita e dia de
--   vencimento do ICMS, natureza e atividade da EFD-Contribuições), para o assistente já abrir preenchido.
-- =============================================================================

CREATE TABLE fiscal.efd_configuracao (
    empresa_id             BIGINT       PRIMARY KEY REFERENCES core.empresa (id) ON DELETE CASCADE,
    icms_perfil            CHAR(1)      NOT NULL DEFAULT 'A' CHECK (icms_perfil IN ('A', 'B', 'C')),
    icms_ind_ativ          CHAR(1)      NOT NULL DEFAULT '1' CHECK (icms_ind_ativ IN ('0', '1')),   -- 0 industrial ou equiparado, 1 outros
    icms_cod_receita       VARCHAR(20),                    -- E116.COD_REC do ICMS próprio (código da UF)
    icms_cod_receita_st    VARCHAR(20),                    -- E250.COD_REC do ICMS-ST
    icms_dia_vencimento    SMALLINT     NOT NULL DEFAULT 10 CHECK (icms_dia_vencimento BETWEEN 1 AND 28),
    contrib_ind_nat_pj     CHAR(2)      NOT NULL DEFAULT '00',    -- 0000.IND_NAT_PJ
    contrib_ind_ativ       CHAR(1)      NOT NULL DEFAULT '2',     -- 0000.IND_ATIV (0 industrial, 1 serviços, 2 comércio, 3 financeira, 4 imobiliária, 9 outros)
    atualizado_em          TIMESTAMPTZ  NOT NULL DEFAULT now()
);
