-- Regras fiscais: conta contábil da contabilização (Cadastros › Regras fiscais, campo Conta contábil).
-- Na entrada é a conta debitada (estoque, despesa ou custo); na saída, a conta creditada (receita). A contrapartida continua vindo da regra de
-- contabilização ou da conta de fornecedores/clientes dos Parâmetros.
ALTER TABLE fiscal.regra_fiscal ADD COLUMN conta_contabil VARCHAR(30);
