namespace Vorticis.Folha.Regras;

/// <summary>
/// Rubricas padrão usadas pelo cálculo. São gravadas por empresa na primeira utilização (e podem ser ajustadas, exceto o
/// código); a natureza segue a Tabela 03 do eSocial e as incidências as tabelas 21 (CP), 22 (IRRF) e 23 (FGTS).
/// </summary>
public static class RubricasPadrao
{
    public const string Salario = "1000";
    public const string HoraExtra50 = "1001";
    public const string HoraExtra100 = "1002";
    public const string Dsr = "1003";
    public const string AdicionalNoturno = "1004";
    public const string Insalubridade = "1005";
    public const string Periculosidade = "1006";
    public const string Comissoes = "1007";
    public const string SalarioFamilia = "1008";
    public const string OutrosProventos = "1009";
    public const string FeriasGozadas = "1010";
    public const string TercoFerias = "1011";
    public const string AbonoPecuniario = "1012";
    public const string TercoAbono = "1013";
    public const string Decimo1 = "1020";
    public const string Decimo2 = "1021";
    public const string SaldoSalario = "1030";
    public const string AvisoPrevioIndenizado = "1031";
    public const string FeriasVencidas = "1032";
    public const string TercoFeriasVencidas = "1033";
    public const string FeriasProporcionais = "1034";
    public const string TercoFeriasProporcionais = "1035";
    public const string DecimoProporcional = "1036";

    public const string Inss = "2001";
    public const string Irrf = "2002";
    public const string Faltas = "2003";
    public const string DsrFaltas = "2004";
    public const string Adiantamento = "2005";
    public const string ValeTransporte = "2006";
    public const string PensaoAlimenticia = "2007";
    public const string OutrosDescontos = "2008";
    public const string AvisoNaoCumprido = "2009";
    public const string InssDecimo = "2010";
    public const string IrrfDecimo = "2011";
    public const string AdiantamentoDecimo = "2012";

    public const string BaseInss = "9001";
    public const string BaseIrrf = "9002";
    public const string BaseFgts = "9003";
    public const string ValorFgts = "9004";
    public const string MultaFgts = "9005";

    private static RubricaCalc P(string cod, string desc, string nat, string inss, string irrf, string fgts, bool variavel = false)
        => new(cod, desc, 'P', nat, inss, irrf, fgts, variavel);

    private static RubricaCalc D(string cod, string desc, string nat, string inss = "00", string irrf = "00", string fgts = "00")
        => new(cod, desc, 'D', nat, inss, irrf, fgts);

    private static RubricaCalc I(string cod, string desc) => new(cod, desc, 'I', "9900", "00", "00", "00");

    public static readonly IReadOnlyList<RubricaCalc> Todas =
    [
        P(Salario, "Salário", "1000", "11", "01", "11"),
        P(HoraExtra50, "Horas extras 50%", "1003", "11", "01", "11", true),
        P(HoraExtra100, "Horas extras 100%", "1003", "11", "01", "11", true),
        P(Dsr, "DSR sobre variáveis", "1002", "11", "01", "11", true),
        P(AdicionalNoturno, "Adicional noturno", "1205", "11", "01", "11", true),
        P(Insalubridade, "Adicional de insalubridade", "1202", "11", "01", "11"),
        P(Periculosidade, "Adicional de periculosidade", "1203", "11", "01", "11"),
        P(Comissoes, "Comissões", "1207", "11", "01", "11", true),
        P(SalarioFamilia, "Salário-família", "1409", "00", "00", "00"),
        P(OutrosProventos, "Outros proventos (gratificações, bônus)", "1000", "11", "01", "11"),
        P(FeriasGozadas, "Férias gozadas", "1020", "11", "01", "11"),
        P(TercoFerias, "1/3 constitucional de férias", "1020", "11", "01", "11"),
        P(AbonoPecuniario, "Abono pecuniário de férias", "1023", "00", "00", "00"),
        P(TercoAbono, "1/3 sobre abono pecuniário", "1023", "00", "00", "00"),
        P(Decimo1, "13º salário – 1ª parcela", "5001", "00", "00", "12"),
        P(Decimo2, "13º salário", "5001", "12", "09", "12"),
        P(SaldoSalario, "Saldo de salário", "6000", "11", "01", "11"),
        P(AvisoPrevioIndenizado, "Aviso prévio indenizado", "6003", "00", "00", "11"),
        P(FeriasVencidas, "Férias vencidas indenizadas", "1020", "00", "00", "00"),
        P(TercoFeriasVencidas, "1/3 sobre férias vencidas", "1020", "00", "00", "00"),
        P(FeriasProporcionais, "Férias proporcionais", "1020", "00", "00", "00"),
        P(TercoFeriasProporcionais, "1/3 sobre férias proporcionais", "1020", "00", "00", "00"),
        P(DecimoProporcional, "13º salário proporcional", "5001", "12", "09", "12"),

        D(Inss, "INSS", "9201", inss: "21"),
        D(Irrf, "IRRF", "9203"),
        D(Faltas, "Faltas", "9207", "11", "01", "11"),
        D(DsrFaltas, "DSR sobre faltas", "9207", "11", "01", "11"),
        D(Adiantamento, "Adiantamento salarial", "9200"),
        D(ValeTransporte, "Vale-transporte (6%)", "9216"),
        D(PensaoAlimenticia, "Pensão alimentícia", "9213", irrf: "31"),
        D(OutrosDescontos, "Outros descontos", "9200"),
        D(AvisoNaoCumprido, "Desconto de aviso prévio não cumprido", "9219"),
        D(InssDecimo, "INSS sobre 13º salário", "9201", inss: "22"),
        D(IrrfDecimo, "IRRF sobre 13º salário", "9203"),
        D(AdiantamentoDecimo, "Adiantamento de 13º salário (1ª parcela)", "9200"),

        I(BaseInss, "Base de cálculo do INSS"),
        I(BaseIrrf, "Base de cálculo do IRRF (rendimento tributável)"),
        I(BaseFgts, "Base de cálculo do FGTS"),
        I(ValorFgts, "FGTS do mês"),
        I(MultaFgts, "Multa rescisória do FGTS (depositada na conta vinculada)"),
    ];

    private static readonly Dictionary<string, RubricaCalc> PorCodigo = Todas.ToDictionary(r => r.Codigo);

    public static RubricaCalc Obter(string codigo) => PorCodigo[codigo];

    public static bool EhPadrao(string codigo) => PorCodigo.ContainsKey(codigo);

    /// <summary>Catálogo padrão por código (para cálculos sem cadastro, como nos testes).</summary>
    public static IReadOnlyDictionary<string, RubricaCalc> Catalogo => PorCodigo;
}
