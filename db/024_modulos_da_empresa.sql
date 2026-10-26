-- Módulos (Contábil, Fiscal, Folha) em que a empresa está ativa. O banco é único e os cadastros comuns
-- (empresa, participantes, sócios, contadores, procuradores, centros de custo, produtos) são compartilhados;
-- estes indicadores só controlam em quais módulos a empresa aparece na seleção do topo.
-- Empresas existentes: ativas em todos os módulos.
ALTER TABLE core.empresa
    ADD COLUMN modulo_contabil BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN modulo_fiscal   BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN modulo_folha    BOOLEAN NOT NULL DEFAULT TRUE,
    ADD CONSTRAINT ck_empresa_algum_modulo CHECK (modulo_contabil OR modulo_fiscal OR modulo_folha);
