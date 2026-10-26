using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Domain.Sped;

/// <summary>
/// Conferência própria antes de gerar a ECD, com as regras do PVA que mais rejeitam arquivos (Manual do Leiaute 9 e consultas
/// de validação do PGE): cadastro (CNPJ, UF, município, NIRE), período, plano de contas (níveis, natureza), vínculo referencial,
/// balancete fechando, encerramento do exercício, demonstrações conferindo com os saldos e signatários (J930).
/// Bloqueante = o PVA rejeitaria; aviso = o PVA aceita (eventualmente com advertência), mas convém revisar.
/// </summary>
public static class ValidadorEcd
{
    /// <summary>Dois primeiros dígitos do NIRE por UF (tabela do registro 0000 no manual; difere do IBGE em MS: 54).</summary>
    public static readonly IReadOnlyDictionary<string, string> PrefixoNire = new Dictionary<string, string>
    {
        ["AC"] = "12", ["AL"] = "27", ["AM"] = "13", ["AP"] = "16", ["BA"] = "29", ["DF"] = "53", ["CE"] = "23", ["ES"] = "32",
        ["GO"] = "52", ["MA"] = "21", ["MT"] = "51", ["MS"] = "54", ["MG"] = "31", ["PA"] = "15", ["PB"] = "25", ["PE"] = "26",
        ["PR"] = "41", ["PI"] = "22", ["RJ"] = "33", ["RN"] = "24", ["RS"] = "43", ["RR"] = "14", ["RO"] = "11", ["SC"] = "42",
        ["SP"] = "35", ["SE"] = "28", ["TO"] = "17",
    };

    public static IReadOnlyList<PendenciaEcd> Validar(DadosEcd d, CalculoEcd calc)
    {
        var res = new List<PendenciaEcd>();
        void B(string codigo, string msg) => res.Add(new(GravidadePendencia.Bloqueante, codigo, msg));
        void A(string codigo, string msg) => res.Add(new(GravidadePendencia.Aviso, codigo, msg));
        var e = d.Empresa;
        var p = d.Parametros;

        // ------------------------------------------------------------ cadastro
        if (!Cnpj.Valido(e.Cnpj)) B("CNPJ_INVALIDO", $"CNPJ da empresa inválido ({e.Cnpj}).");
        if (string.IsNullOrWhiteSpace(e.Nome)) B("NOME_AUSENTE", "Razão social da empresa não informada.");
        if (!Uf.Valida(e.Uf)) B("UF_INVALIDA", $"UF da empresa inválida ({e.Uf}).");
        if (e.CodMunicipioIbge is not { Length: 7 } || !e.CodMunicipioIbge.All(char.IsAsciiDigit))
            B("MUNICIPIO_INVALIDO", "Código IBGE do município da empresa deve ter 7 dígitos.");
        else if (Uf.DeCodigoIbge(e.CodMunicipioIbge[..2]) is { } ufMun && !string.Equals(ufMun, e.Uf, StringComparison.OrdinalIgnoreCase))
            B("MUNICIPIO_UF", $"O município {e.CodMunicipioIbge} não pertence à UF {e.Uf} (o PVA confere o código IBGE com a UF).");
        var nire = Nire.Normalizar(e.Nire);
        if (nire is null)
            A("NIRE_AUSENTE", "Empresa sem NIRE: a ECD sai com IND_NIRE = 0 (sem registro na Junta Comercial). Sociedades empresárias " +
                              "registradas na Junta devem informar o NIRE no cadastro da empresa.");
        else if (nire.Length != Nire.Tamanho)
            B("NIRE_INVALIDO", $"NIRE {e.Nire} inválido: deve ter {Nire.Tamanho} dígitos.");
        else if (PrefixoNire.TryGetValue(e.Uf.ToUpperInvariant(), out var prefixo) && nire[..2] != prefixo)
            B("NIRE_UF", $"O NIRE {nire} começa com {nire[..2]}, mas o de empresas da UF {e.Uf} começa com {prefixo} (regra REGRA_NIRE_UF do PVA).");
        if (p.DataArquivamentoAtos is null && p.DataArquivamentoConversao is null)
            A("DT_ARQ_AUSENTE", "Data de arquivamento dos atos constitutivos (I030.DT_ARQ) não informada.");

        // ------------------------------------------------------------ período
        if (p.Fim < p.Inicio) B("PERIODO_INVALIDO", "Data final anterior à inicial.");
        if (p.Inicio.Year != p.Fim.Year) B("PERIODO_ANOS", "O período da ECD (DT_INI a DT_FIN) deve estar contido em um mesmo ano.");
        if (p.IndicadorSituacaoEspecial is null && p.Fim != new DateOnly(p.Fim.Year, p.Fim.Month, 1).AddMonths(1).AddDays(-1))
            B("PERIODO_FIM_MES", "Sem situação especial, a ECD termina no último dia de um mês.");
        if (p.IndicadorSituacaoInicio == '0' && p.Inicio.Day != 1)
            B("PERIODO_INICIO", "Com início normal (IND_SIT_INI_PER = 0) a ECD começa no primeiro dia de um mês; para empresa aberta no período use o indicador 1 (abertura).");
        if (p.NumeroOrdem <= 0) B("NUM_ORD", "Número de ordem do livro deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(p.NaturezaLivro)) B("NAT_LIVRO", "Informe a natureza (nome) do livro, ex.: DIÁRIO GERAL.");
        if (p.NivelAglutinacao < 2) B("NIVEL_AGL", "O nível de aglutinação das demonstrações deve ser 2 ou maior.");

        var lancamentos = d.Lancamentos.Where(l => l.Data >= p.Inicio && l.Data <= p.Fim).ToList();
        if (lancamentos.Count == 0) A("SEM_LANCAMENTOS", "Não há lançamentos no período.");

        // ------------------------------------------------------------ plano de contas
        var porId = d.Plano.ToDictionary(c => c.Id);
        foreach (var c in calc.PlanoUsado)
        {
            if (c.Analitica && c.CodNat is "01" or "02" or "03" && c.Nivel < 4)
                B("NIVEL_CONTA", $"Conta {c.Codigo} ({c.Descricao}) é analítica de nível {c.Nivel}: no Livro G as analíticas de ativo, passivo e PL devem ter nível 4 ou maior (REGRA_VALIDA_NIVEL_CONTAS; CTG 2001).");
            if (c.Nivel > 1 && c.ContaPaiId is null)
                B("CONTA_SEM_SUPERIOR", $"Conta {c.Codigo} de nível {c.Nivel} sem conta superior.");
            if (c.Nivel > 2 && c.ContaPaiId is long pai && porId.TryGetValue(pai, out var sup) && sup.CodNat != c.CodNat)
                B("NATUREZA_SUPERIOR", $"Conta {c.Codigo} tem natureza {c.CodNat} e a superior {sup.Codigo}, {sup.CodNat}: a partir do nível 3 a natureza deve ser igual à da conta superior (REGRA_NATUREZA_CONTA).");
            if (c.ContaPaiId is long pai2 && porId.TryGetValue(pai2, out var sup2) && sup2.Nivel >= c.Nivel)
                B("NIVEL_SUPERIOR", $"Conta {c.Codigo} tem nível {c.Nivel}, não maior que o da superior {sup2.Codigo} ({sup2.Nivel}).");
            if (c.DataInclusao > p.Fim)
                A("DT_ALT", $"Conta {c.Codigo} com data de inclusão ({c.DataInclusao:dd/MM/yyyy}) posterior ao fim do período.");
        }

        // ------------------------------------------------------------ plano referencial (I051)
        if (p.CodPlanRef is null)
            A("SEM_PLANO_REFERENCIAL", $"Plano referencial do exercício {p.Fim.Year} não definido: a ECD sai sem I051 (COD_PLAN_REF vazio). " +
                                       "O mapeamento é necessário para recuperar os saldos na ECF.");
        else
            foreach (var c in calc.PlanoUsado.Where(c => c.Analitica && c.CodNat is "01" or "02" or "03" or "04" && c.CodigoReferencial is null))
                B("SEM_REFERENCIAL", $"Conta {c.Codigo} ({c.Descricao}) com saldo ou movimento sem vínculo ao plano referencial {p.CodPlanRef} (I051 obrigatório).");

        // ------------------------------------------------------------ balancete e encerramento
        var somaAbertura = calc.Abertura.Values.Sum();
        if (somaAbertura != 0)
            B("ABERTURA_DESBALANCEADA", $"Saldos iniciais não fecham: débitos − créditos = {Formato.Moeda(somaAbertura)} (o PVA exige soma zero no I155).");
        foreach (var per in calc.Periodos)
        {
            var deb = per.Saldos.Sum(s => s.Debitos);
            var cred = per.Saldos.Sum(s => s.Creditos);
            if (deb != cred)
                B("BALANCETE_DESBALANCEADO", $"Balancete de {per.Inicio:MM/yyyy} desbalanceado: débitos {Formato.Moeda(deb)} × créditos {Formato.Moeda(cred)}.");
        }
        // Contas de resultado conferidas por conta (somando os centros de custo), como o PVA (REGRA_VALIDACAO_CONTA_RESULTADO).
        List<string> ResultadoComSaldo(IReadOnlyDictionary<(long Conta, long Centro), decimal> saldos) => saldos
            .Where(kv => porId.TryGetValue(kv.Key.Conta, out var c) && c.CodNat == "04")
            .GroupBy(kv => kv.Key.Conta).Where(g => g.Sum(kv => kv.Value) != 0).Select(g => porId[g.Key].Codigo).Order(StringComparer.Ordinal).ToList();
        var resultadoAbertura = ResultadoComSaldo(calc.Abertura);
        if (resultadoAbertura.Count > 0)
            B("RESULTADO_ANTERIOR_ABERTO", $"Há contas de resultado com saldo no início do período ({string.Join(", ", resultadoAbertura.Take(5))}): " +
                                           "o exercício anterior não foi encerrado, e o Balanço inicial não fecharia.");
        var fimExercicio = p.FimExercicioSocial ?? p.Fim;
        if (fimExercicio >= p.Inicio && fimExercicio <= p.Fim)
        {
            if (!calc.Encerramentos.Any(x => x.Data == fimExercicio))
                B("NAO_ENCERRADO", $"O exercício não foi encerrado: não há lançamentos de encerramento (IND_LCTO = E) em {fimExercicio:dd/MM/yyyy}. " +
                                   "Encerre o exercício (Lançamentos › Encerramento) antes de gerar a ECD.");
            var resultadoAberto = ResultadoComSaldo(calc.Final);
            if (resultadoAberto.Count > 0)
                B("RESULTADO_NAO_ZERADO", $"Contas de resultado com saldo após o encerramento: {string.Join(", ", resultadoAberto.Take(5))}. " +
                                          "Refaça o encerramento (desfazer e encerrar de novo).");
        }
        foreach (var l in lancamentos.Where(l => l.Indicador == IndicadorLancamento.Encerramento && l.Data != fimExercicio))
            A("ENCERRAMENTO_FORA_DO_FIM", $"Lançamento de encerramento nº {l.Numero} em {l.Data:dd/MM/yyyy}, fora da data de fim do exercício.");
        foreach (var l in lancamentos)
        {
            var deb = l.Partidas.Where(x => x.DC == IndicadorDC.D).Sum(x => x.Valor);
            var cred = l.Partidas.Where(x => x.DC == IndicadorDC.C).Sum(x => x.Valor);
            if (deb != cred) B("LANCAMENTO_DESBALANCEADO", $"Lançamento nº {l.Numero}: débitos {Formato.Moeda(deb)} × créditos {Formato.Moeda(cred)}.");
            if (l.Partidas.Any(x => string.IsNullOrWhiteSpace(x.Historico)))
                B("HISTORICO_AUSENTE", $"Lançamento nº {l.Numero} tem partida sem histórico (I250.HIST obrigatório).");
            if (l.Indicador == IndicadorLancamento.Extemporaneo && l.Partidas.Any(x => porId.TryGetValue(x.ContaId, out var c) && c.CodNat == "04"))
                B("EXTEMPORANEO_RESULTADO", $"Lançamento extemporâneo nº {l.Numero} movimenta conta de resultado (não permitido pelo PVA).");
        }

        // ------------------------------------------------------------ demonstrações (J100/J150)
        if (calc.Balanco.Count > 0)
        {
            decimal Soma(char grupo, Func<LinhaAglutinacaoEcd, decimal> v) => calc.Balanco.Where(b => b.Nivel == 1 && b.Grupo == grupo).Sum(v);
            if (Soma('A', b => b.Final) + Soma('P', b => b.Final) != 0)
                B("BALANCO_NAO_FECHA", $"Balanço Patrimonial final não fecha: ativo {Formato.Moeda(Soma('A', b => b.Final))} × passivo e PL {Formato.Moeda(-Soma('P', b => b.Final))}.");
            if (Soma('A', b => b.Inicial) + Soma('P', b => b.Inicial) != 0)
                B("BALANCO_INICIAL_NAO_FECHA", $"Balanço Patrimonial inicial não fecha: ativo {Formato.Moeda(Soma('A', b => b.Inicial))} × passivo e PL {Formato.Moeda(-Soma('P', b => b.Inicial))}.");
        }
        else if (lancamentos.Count > 0)
            B("SEM_BALANCO", "Não há contas patrimoniais com saldo: o Balanço (J100) não pode ser montado.");
        if (calc.Dre.Count == 0 && lancamentos.Count > 0)
            B("SEM_DRE", "Não há contas de resultado com movimento: a DRE (J150) não pode ser montada.");
        if (p.Mutacoes == TipoMutacoesEcd.Dlpa && calc.TipoMutacoes == TipoMutacoesEcd.Dmpl)
            A("DLPA_PARA_DMPL", "As contas de lucros ou prejuízos acumulados não estão em um único código de aglutinação: o J210 foi gerado como DMPL.");

        // ------------------------------------------------------------ signatários (J930)
        var contadores = d.Signatarios.Where(s => s.Contador).ToList();
        if (contadores.Count == 0)
            B("SEM_CONTADOR", "Nenhum contador signatário: vincule à empresa um contador (com CRC) como responsável técnico ou marque-o como assinante da ECD.");
        foreach (var s in contadores)
        {
            var cpf = Cnpj.SomenteDigitos(s.CpfCnpj);
            if (!Cpf.Valido(cpf)) B("CONTADOR_CPF", $"CPF do contador {s.Nome} inválido.");
            if (string.IsNullOrWhiteSpace(s.Crc)) B("CONTADOR_CRC", $"Contador {s.Nome} sem número do CRC (J930.IND_CRC).");
            if (!Uf.Valida(s.UfCrc)) B("CONTADOR_UF_CRC", $"Contador {s.Nome} sem UF do CRC (J930.UF_CRC).");
            if (string.IsNullOrWhiteSpace(s.Email)) B("CONTADOR_EMAIL", $"Contador {s.Nome} sem e-mail (obrigatório no J930 para o contador).");
            if (Cnpj.SomenteDigitos(s.Telefone).Length == 0) B("CONTADOR_FONE", $"Contador {s.Nome} sem telefone (obrigatório no J930 para o contador).");
            if (string.IsNullOrWhiteSpace(s.NumeroCertidaoCrc) || s.ValidadeCertidaoCrc is null)
                A("CONTADOR_CERTIDAO", $"Certidão de regularidade profissional (DHP) do contador {s.Nome} não informada (J930.NUM_SEQ_CRC e DT_CRC): o PVA emite advertência.");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(s.NumeroCertidaoCrc, @"^[A-Z]{2}/\d{4}/\d+$"))
                A("CONTADOR_CERTIDAO_FORMATO", $"Número da certidão do contador {s.Nome} fora do formato UF/AAAA/NÚMERO (ex.: SP/2025/012345).");
            if (s.ResponsavelLegal) B("CONTADOR_RESPONSAVEL", "O contador (código 900) não pode ser o responsável pela assinatura da ECD.");
        }
        var responsaveis = d.Signatarios.Where(s => s.ResponsavelLegal).ToList();
        if (responsaveis.Count == 0)
            B("SEM_RESPONSAVEL", "Nenhum responsável pela assinatura da ECD (representante legal): cadastre um sócio representante legal com CPF ou use o e-CNPJ da empresa.");
        else if (responsaveis.Count > 1)
            B("RESPONSAVEIS", "Só pode haver um responsável pela assinatura da ECD (J930.IND_RESP_LEGAL = S).");
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
        return res;
    }
}
