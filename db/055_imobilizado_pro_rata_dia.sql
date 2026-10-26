-- =============================================================================
-- 055 — Imobilizado: depreciação proporcional aos dias a partir da data de aquisição
--   pro_rata_dia = TRUE: a depreciação começa na data de aquisição e a quota do primeiro mês (e do mês da baixa)
--   é proporcional aos dias; a fração que falta é lançada no mês seguinte ao fim da vida útil.
--   FALSE (padrão): regra anterior, quota integral a partir do mês do início de uso.
-- =============================================================================
ALTER TABLE imobilizado.bem ADD COLUMN pro_rata_dia BOOLEAN NOT NULL DEFAULT FALSE;
