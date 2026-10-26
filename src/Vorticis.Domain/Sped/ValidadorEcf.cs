using System.Text.RegularExpressions;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Sped;

/// <summary>
/// Validação prévia da ECF: pendências bloqueantes (o PVA rejeitaria o arquivo ou o arquivo ficaria inconsistente com a apuração) e avisos.
/// Reaproveita <see cref="PendenciaEcd"/> (mesma estrutura de código/mensagem/gravidade da ECD).
/// </summary>
public static class ValidadorEcf
{
    private const decimal Tolerancia = 0.05m;

    public static IReadOnlyList<PendenciaEcd> Validar(DadosEcf d, CalculoEcf calc)
    {
        var res = new List<PendenciaEcd>();
        void B(string codigo, string msg) => res.Add(new(GravidadePendencia.Bloqueante, codigo, msg));
        void A(string codigo, string msg) => res.Add(new(GravidadePendencia.Aviso, codigo, msg));
        var p = d.Parametros;
        var e = d.Empresa;
        var real = p.Forma == FormaTributacaoEcf.Real;
        var regime = real ? "Lucro Real" : "Lucro Presumido";

        // ------------------------------------------------------------ cadastro (0000, 0030)
        if (!Cnpj.Valido(e.Cnpj)) B("CNPJ_INVALIDO", $"CNPJ {e.Cnpj} inválido.");
        if (string.IsNullOrWhiteSpace(e.Nome)) B("NOME_AUSENTE", "Razão social da empresa não informada.");
        if (!Uf.Valida(e.Uf)) B("UF_INVALIDA", $"UF {e.Uf} inválida.");
        if (!Regex.IsMatch(e.CodMunicipioIbge ?? "", @"^\d{7}$")) B("MUNICIPIO_IBGE", "Código IBGE do município (7 dígitos) não informado no cadastro da empresa (0030.COD_MUN).");
        if (!Regex.IsMatch(e.NaturezaJuridica ?? "", @"^\d{4}$")) B("NATUREZA_JURIDICA", "Natureza jurídica (4 dígitos) não informada no cadastro da empresa (0030.COD_NAT).");
        if (!Regex.IsMatch(new string((e.Cnae ?? "").Where(char.IsAsciiDigit).ToArray()), @"^\d{7}$")) B("CNAE", "CNAE fiscal principal (7 dígitos) não informado no cadastro da empresa (0030.CNAE_FISCAL).");
        if (string.IsNullOrWhiteSpace(e.Logradouro)) B("ENDERECO", "Logradouro da empresa não informado (0030.ENDERECO).");
        if (string.IsNullOrWhiteSpace(e.Numero)) B("ENDERECO_NUMERO", "Número do endereço da empresa não informado (0030.NUM; use S/N se não houver).");
        if (string.IsNullOrWhiteSpace(e.Bairro)) B("ENDERECO_BAIRRO", "Bairro da empresa não informado (0030.BAIRRO).");
        if (!Regex.IsMatch(new string((e.Cep ?? "").Where(char.IsAsciiDigit).ToArray()), @"^\d{8}$")) B("ENDERECO_CEP", "CEP da empresa não informado (0030.CEP).");
        if (!Regex.IsMatch(e.Email ?? "", @"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,4}$")) B("EMAIL_EMPRESA", "E-mail da empresa ausente ou inválido (0030.EMAIL).");
        if (p.Retificadora && string.IsNullOrWhiteSpace(p.NumeroRecibo)) B("RECIBO_ANTERIOR", "ECF retificadora exige o número do recibo da ECF anterior (0000.NUM_REC).");

        // ------------------------------------------------------------ apurações trimestrais
        var doRegime = d.Apuracoes.Where(a => a.Regime == p.Forma).ToList();
        var outro = d.Apuracoes.Where(a => a.Regime != p.Forma).ToList();
        if (outro.Count > 0)
            B("REGIME_MISTO", $"Há apurações de {(real ? "Lucro Presumido" : "Lucro Real")} no ano ({string.Join(", ", outro.Select(a => a.Trimestre + "º tri"))}): " +
                              "a ECF com mais de uma forma de tributação no ano (FORMA_TRIB 3/4) não é suportada nesta versão.");
        if (doRegime.Count == 0)
            B("SEM_APURACAO", $"Nenhuma apuração de {regime} no ano {p.Ano}: apure o IRPJ e a CSLL trimestrais no módulo Fiscal (Lucro Presumido/Real).");
        foreach (var per in calc.Periodos)
        {
            var ap = doRegime.FirstOrDefault(a => a.Trimestre == per.Trimestre);
            if (ap is null) { if (doRegime.Count > 0) B("APURACAO_AUSENTE", $"Apuração do {per.Trimestre}º trimestre de {p.Ano} ({regime}) ausente."); continue; }
            if (ap.Inicio != per.Inicio || ap.Fim != per.Fim)
                A("APURACAO_PERIODO", $"A apuração do {per.Trimestre}º trimestre cobre {ap.Inicio:dd/MM/yyyy} a {ap.Fim:dd/MM/yyyy}, mas o período da ECF é {per.Inicio:dd/MM/yyyy} a {per.Fim:dd/MM/yyyy}.");
        }

        // ------------------------------------------------------------ plano de contas e vínculo referencial
        if (string.IsNullOrWhiteSpace(p.CodPlanRef))
            B("SEM_PLANO_REFERENCIAL", "O exercício não tem plano de contas referencial definido (Integrações › Importar › Vínculo ao plano referencial): o balanço e a DRE referenciais não podem ser montados.");
        if (calc.SemReferencial.Count > 0)
            B("SEM_REFERENCIAL", $"{calc.SemReferencial.Count} conta(s) com saldo ou movimento sem vínculo ao plano referencial (K156/K356/J051): " +
                                 string.Join(", ", calc.SemReferencial.Take(8).Select(c => c.Codigo)) + (calc.SemReferencial.Count > 8 ? "..." : "") + ".");
        foreach (var c in calc.ReferenciaInexistente)
            B("REFERENCIAL_INEXISTENTE", $"A conta referencial {c} não existe na tabela do plano referencial importado para {p.Ano}.");
        if (d.Lancamentos.Count == 0 && d.SaldosAbertura.Count == 0) B("SEM_LANCAMENTOS", "A empresa não tem lançamentos no ano: a ECF exige a escrituração contábil (bloco K).");

        // Balanço do último período e do início
        if (calc.Patrimoniais.Count > 0)
        {
            var ini = calc.Patrimoniais[0].Sum(s => s.Inicial);
            if (Math.Abs(ini) > Tolerancia) B("BALANCO_INICIAL", $"Os saldos iniciais das contas patrimoniais não fecham (diferença de {ini:N2}).");
            var fin = calc.Patrimoniais[^1].Sum(s => s.Final);
            if (Math.Abs(fin) > Tolerancia)
                B("BALANCO_FINAL", $"O balanço do fim do período não fecha (diferença de {fin:N2}). Encerre o exercício (apuração do resultado) antes da ECF.");
        }

        // ------------------------------------------------------------ Lucro Real
        if (real)
        {
            foreach (var l in d.Lalur)
            {
                var rotulo = $"{l.Trimestre}º tri: {l.Descricao} ({l.Valor:N2})";
                if (string.IsNullOrWhiteSpace(l.CodigoEcf)) { B("LALUR_SEM_CODIGO", $"Lançamento do LALUR sem código da tabela dinâmica da ECF — {rotulo}."); continue; }
                if (l.Irpj && !TabelasEcfLalur.M300A.TryGetValue(l.CodigoEcf!, out var t1))
                    B("LALUR_CODIGO_INVALIDO", $"Código {l.CodigoEcf} não existe (ou não é de entrada) na tabela M300A — {rotulo}.");
                else if (l.Irpj && TabelasEcfLalur.M300A[l.CodigoEcf!].Adicao != l.Adicao)
                    B("LALUR_TIPO_CODIGO", $"O código {l.CodigoEcf} da M300A é de {(l.Adicao ? "exclusão" : "adição")}, mas o lançamento é de {(l.Adicao ? "adição" : "exclusão")} — {rotulo}.");
                if (l.Csll && !TabelasEcfLalur.M350A.TryGetValue(l.CodigoEcf!, out var t2))
                    B("LALUR_CODIGO_INVALIDO", $"Código {l.CodigoEcf} não existe (ou não é de entrada) na tabela M350A (e-Lacs) — {rotulo}.");
                else if (l.Csll && TabelasEcfLalur.M350A[l.CodigoEcf!].Adicao != l.Adicao)
                    B("LALUR_TIPO_CODIGO", $"O código {l.CodigoEcf} da M350A é de {(l.Adicao ? "exclusão" : "adição")}, mas o lançamento é de {(l.Adicao ? "adição" : "exclusão")} — {rotulo}.");
            }
            foreach (var (conta, tri, excesso) in calc.CompensacoesSemSaldo)
                B("COMPENSACAO_SEM_SALDO", $"{tri}º tri: compensação de {(conta == CalculoEcf.ContaPrejuizo ? "prejuízo fiscal" : "base de cálculo negativa")} excede o saldo da Parte B em {excesso:N2}.");
            if (calc.Dre.Any(x => x.Count > 0) && d.Referencial.Count > 0 && !d.Referencial.Any(r => r.Codigo == CalculoEcf.RefResultadoAntesTributos))
                B("DRE_SEM_3_01", $"A tabela referencial não tem a conta {CalculoEcf.RefResultadoAntesTributos} (resultado antes do IRPJ e da CSLL), de que partem o M300 e o M350.");

            // Conferência com a apuração do Fiscal (o PVA recalcula M/N a partir do L300 e do e-Lalur).
            foreach (var r in calc.Real)
            {
                var ap = doRegime.FirstOrDefault(a => a.Trimestre == r.Periodo.Trimestre);
                if (ap is null) continue;
                var t = r.Periodo.Trimestre;
                if (ap.LucroContabil is { } lc && Math.Abs(lc - (-r.LucroAntesCsll)) > Tolerancia && Math.Abs(lc - r.LucroAntesCsll) > Tolerancia)
                    A("DIVERGENCIA_LUCRO_CONTABIL", $"{t}º tri: o lucro contábil da apuração ({lc:N2}) difere do resultado da DRE referencial ({r.LucroAntesCsll:N2}).");
                if (Math.Abs(Math.Max(0, r.LucroReal) - ap.BaseIrpj) > Tolerancia)
                    A("DIVERGENCIA_BASE_IRPJ", $"{t}º tri: o lucro real da ECF ({r.LucroReal:N2}) difere da base de cálculo do IRPJ apurada ({ap.BaseIrpj:N2}).");
                if (Math.Abs(r.IrpjDevido - (ap.IrpjNormal + ap.IrpjAdicional)) > Tolerancia)
                    A("DIVERGENCIA_IRPJ", $"{t}º tri: o IRPJ calculado pela ECF ({r.IrpjDevido:N2}) difere do apurado ({ap.IrpjNormal + ap.IrpjAdicional:N2}).");
                if (Math.Abs(Math.Max(0, r.BaseCsll) - ap.BaseCsll) > Tolerancia)
                    A("DIVERGENCIA_BASE_CSLL", $"{t}º tri: a base da CSLL da ECF ({r.BaseCsll:N2}) difere da apurada ({ap.BaseCsll:N2}).");
                if (Math.Abs(r.CsllDevida - ap.CsllDevida) > Tolerancia)
                    A("DIVERGENCIA_CSLL", $"{t}º tri: a CSLL calculada pela ECF ({r.CsllDevida:N2}) difere da apurada ({ap.CsllDevida:N2}).");
            }
            var temB = d.ParteB.Count > 0;
            if (!temB && calc.Real.Any(r => r.M410.Count > 0)) A("PARTE_B_SEM_SALDO", "Há prejuízo no período, mas a Parte B do LALUR não tem movimentos registrados.");
        }
        else
        {
            foreach (var a in doRegime.Where(a => a.ReceitaBruta > 0))
            {
                var soma = a.Receitas.Sum(r => r.Valor);
                if (a.Receitas.Count == 0 || Math.Abs(soma - a.ReceitaBruta) > Tolerancia)
                    B("RECEITA_SEM_PERCENTUAL", $"{a.Trimestre}º tri: não foi possível identificar a receita por percentual de presunção na memória da apuração (receita {a.ReceitaBruta:N2}); apure de novo no módulo Fiscal.");
                else if (a.Receitas.Any(r => !new[] { 1.6m, 8m, 16m, 32m, 38.4m }.Any(x => Math.Abs(x - r.PercentualIrpj) < 0.005m) || !new[] { 12m, 32m, 38.4m }.Any(x => Math.Abs(x - r.PercentualCsll) < 0.005m)))
                    B("PERCENTUAL_INVALIDO", $"{a.Trimestre}º tri: percentual de presunção fora da tabela do P200/P400.");
            }
            foreach (var r in calc.Presumido)
            {
                var ap = doRegime.FirstOrDefault(a => a.Trimestre == r.Periodo.Trimestre);
                if (ap is null) continue;
                var t = r.Periodo.Trimestre;
                if (Math.Abs(r.BaseIrpj - ap.BaseIrpj) > Tolerancia) A("DIVERGENCIA_BASE_IRPJ", $"{t}º tri: a base do IRPJ da ECF ({r.BaseIrpj:N2}) difere da apurada ({ap.BaseIrpj:N2}).");
                if (Math.Abs(r.IrpjDevido - (ap.IrpjNormal + ap.IrpjAdicional)) > Tolerancia) A("DIVERGENCIA_IRPJ", $"{t}º tri: o IRPJ da ECF ({r.IrpjDevido:N2}) difere do apurado ({ap.IrpjNormal + ap.IrpjAdicional:N2}).");
                if (Math.Abs(r.CsllDevida - ap.CsllDevida) > Tolerancia) A("DIVERGENCIA_CSLL", $"{t}º tri: a CSLL da ECF ({r.CsllDevida:N2}) difere da apurada ({ap.CsllDevida:N2}).");
            }
        }

        // ------------------------------------------------------------ sócios (Y600)
        if (d.Socios.Count == 0) A("SEM_SOCIOS", "Nenhum sócio cadastrado: o Y600 (sócios e dirigentes) sairá vazio.");
        foreach (var s in d.Socios.Where(s => s.Saida is null))
            if (s.TipoPessoa == 'E') A("SOCIO_EXTERIOR", $"Sócio {s.Nome} residente no exterior não é informado no Y600 (país e representante legal não cadastrados).");
            else if (s.Documento is not { Length: 11 or 14 }) A("SOCIO_SEM_DOCUMENTO", $"Sócio {s.Nome} sem CPF/CNPJ completo: não consta do Y600.");
        if (d.Socios.Where(s => s.Saida is null).Sum(s => s.Participacao ?? 0) is var tot && d.Socios.Count > 0 && Math.Abs(tot - 100) > 0.01m)
            A("SOCIOS_CAPITAL", $"A participação dos sócios soma {tot:0.##}% (esperado 100%).");
        A("PARAMETROS_COMPLEMENTARES", "Parâmetros complementares do 0020 gerados com 'N' (sem operações com o exterior, consórcios, incentivos etc.); confira antes de transmitir.");
        if (real) A("REMUNERACAO_SOCIOS", "Y600: remuneração, lucros e IRRF dos sócios saem zerados nesta versão; informe no PVA se houver pagamentos.");

        // ------------------------------------------------------------ signatários (0930)
        var contadores = d.Signatarios.Where(s => s.Contador).ToList();
        if (contadores.Count == 0)
            B("SEM_CONTADOR", "Nenhum contador signatário: vincule à empresa um contador (com CRC) como responsável técnico ou marque-o como assinante da ECD/ECF.");
        foreach (var s in contadores)
        {
            if (!Cpf.Valido(Cnpj.SomenteDigitos(s.CpfCnpj))) B("CONTADOR_CPF", $"CPF do contador {s.Nome} inválido.");
            if (string.IsNullOrWhiteSpace(s.Crc)) B("CONTADOR_CRC", $"Contador {s.Nome} sem número do CRC (0930.IND_CRC).");
            if (string.IsNullOrWhiteSpace(s.Email)) B("CONTADOR_EMAIL", $"Contador {s.Nome} sem e-mail (obrigatório no 0930).");
            if (Cnpj.SomenteDigitos(s.Telefone).Length == 0) B("CONTADOR_FONE", $"Contador {s.Nome} sem telefone (obrigatório no 0930).");
        }
        var responsaveis = d.Signatarios.Where(s => s.ResponsavelLegal).ToList();
        if (responsaveis.Count == 0) B("SEM_RESPONSAVEL", "Nenhum responsável pela assinatura da ECF (representante legal): cadastre um sócio representante legal com CPF ou use o e-CNPJ da empresa.");
        else if (responsaveis.Count > 1) B("RESPONSAVEIS", "Só pode haver um responsável pela assinatura da ECF.");
        foreach (var s in d.Signatarios.Where(s => !s.Contador))
        {
            var doc = new string(s.CpfCnpj.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (doc.Length == 0) B("SIGNATARIO_SEM_DOCUMENTO", $"Signatário {s.Nome} sem CPF/CNPJ.");
            else if (s.CodigoQualificacao == QualificacaoAssinante.PessoaJuridica ? !Cnpj.Valido(doc) : !Cpf.Valido(doc))
                B("SIGNATARIO_DOCUMENTO", $"CPF/CNPJ do signatário {s.Nome} inválido.");
            if (!QualificacaoAssinante.Tabela.ContainsKey(s.CodigoQualificacao))
                B("SIGNATARIO_QUALIFICACAO", $"Código de qualificação {s.CodigoQualificacao} do signatário {s.Nome} não existe na tabela do PVA.");
        }
        foreach (var g in d.Signatarios.GroupBy(s => (new string(s.CpfCnpj.Where(char.IsAsciiLetterOrDigit).ToArray()), s.CodigoQualificacao)).Where(g => g.Count() > 1))
            B("SIGNATARIO_DUPLICADO", $"Signatário {g.First().Nome} repetido com a mesma qualificação ({g.Key.Item2}).");

        if (d.Ecd is null) A("SEM_ECD", "Nenhuma ECD gerada pelo Vorticis para o ano: o bloco C sai sem dados — no PVA da ECF, recupere a ECD (Escrituração › Recuperar ECD) antes de validar.");
        return res;
    }
}
