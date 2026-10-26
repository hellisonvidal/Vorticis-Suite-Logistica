using System.Globalization;
using System.Xml.Linq;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Esocial;

/// <summary>
/// Geração dos XMLs dos eventos do eSocial (leiaute S-1.3). Os eventos seguem os XSDs oficiais (pacote de 01/07/2026, embutido
/// e conferido por <see cref="ValidadorXsdEsocial"/>); a assinatura XMLDSig e a transmissão ficam em <see cref="AssinadorEsocial"/>
/// e no cliente de envio de lotes.
/// </summary>
public static class GeradoresEsocial
{
    /// <summary>Sufixo de versão do leiaute nos namespaces (xmlns .../evt/evtXxx/v_S_01_03_00).</summary>
    public const string VersaoSchema = "v_S_01_03_00";
    public const string VersaoProcesso = "Vorticis 1.0";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ Id e utilidades

    /// <summary>
    /// Atributo Id do evento: "ID" + tipo de inscrição (1 = CNPJ) + CNPJ com 14 posições + data/hora (AAAAMMDDHHMMSS) + sequencial de 5 dígitos
    /// = 36 caracteres.
    /// </summary>
    public static string GerarId(string cnpj, DateTime agora, int sequencial)
    {
        var nr = new string(cnpj.Where(char.IsDigit).ToArray()).PadRight(14, '0')[..14];
        return $"ID1{nr}{agora:yyyyMMddHHmmss}{sequencial:00000}";
    }

    public static bool IdValido(string id) => id.Length == 36 && id.StartsWith("ID1", StringComparison.Ordinal) && id[3..].All(char.IsAsciiDigit);

    /// <summary>Prefixo do Id até os segundos (para numerar os eventos gerados no mesmo instante).</summary>
    public static string PrefixoId(string cnpj, DateTime agora) => GerarId(cnpj, agora, 0)[..^5];

    private static string Decimal(decimal v) => v.ToString("0.00", Inv);
    private static string Data(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    private static string Periodo(DateOnly d) => d.ToString("yyyy-MM", Inv);
    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    private static string SN(bool b) => b ? "S" : "N";

    private static XElement E(XNamespace ns, string nome, params object?[] conteudo) => new(ns + nome, conteudo);

    private static string Montar(string nome, string id, Func<XNamespace, IEnumerable<XElement>> corpo)
    {
        XNamespace ns = $"http://www.esocial.gov.br/schema/evt/{nome}/{VersaoSchema}";
        var raiz = new XElement(ns + "eSocial", new XElement(ns + nome, new XAttribute("Id", id), corpo(ns)));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + raiz.ToString(SaveOptions.None);
    }

    private static XElement IdeEvento(XNamespace ns, int ambiente, bool comRetif = true, int? indApuracao = null, string? perApur = null)
    {
        var e = E(ns, "ideEvento");
        if (comRetif) e.Add(E(ns, "indRetif", 1));
        if (indApuracao is { } ia) e.Add(E(ns, "indApuracao", ia));
        if (perApur is not null) e.Add(E(ns, "perApur", perApur));
        e.Add(E(ns, "tpAmb", ambiente), E(ns, "procEmi", 1), E(ns, "verProc", VersaoProcesso));
        return e;
    }

    /// <summary>Empregador pessoa jurídica: tipo de inscrição 1 e a raiz do CNPJ (8 dígitos).</summary>
    private static XElement IdeEmpregador(XNamespace ns, string cnpj) => E(ns, "ideEmpregador", E(ns, "tpInsc", 1), E(ns, "nrInsc", Digitos(cnpj)[..8]));

    private static XElement IdeVinculo(XNamespace ns, string cpf, string matricula) => E(ns, "ideVinculo", E(ns, "cpfTrab", Digitos(cpf)), E(ns, "matricula", matricula));

    // ------------------------------------------------------------------ eventos de tabela

    /// <summary>S-1000 – Informações do empregador/contribuinte.</summary>
    public static string S1000(string id, EmpresaFolha e, ParametroEmpresa p, DateOnly iniValid, int ambiente = 2)
        => Montar("evtInfoEmpregador", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: false),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "infoEmpregador", E(ns, "inclusao",
                E(ns, "idePeriodo", E(ns, "iniValid", Periodo(iniValid))),
                E(ns, "infoCadastro",
                    E(ns, "classTrib", p.ClassTrib), E(ns, "indCoop", 0), E(ns, "indConstr", 0), E(ns, "indDesFolha", 0), E(ns, "indOptRegEletron", 0)))),
        ]);

    /// <summary>S-1005 – Tabela de estabelecimentos (matriz): CNAE preponderante e alíquotas RAT/FAP.</summary>
    public static string S1005(string id, EmpresaFolha e, ParametroEmpresa p, DateOnly iniValid, int ambiente = 2)
        => Montar("evtTabEstab", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: false),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "infoEstab", E(ns, "inclusao",
                E(ns, "ideEstab", E(ns, "tpInsc", 1), E(ns, "nrInsc", Digitos(e.Cnpj)), E(ns, "iniValid", Periodo(iniValid))),
                E(ns, "dadosEstab", E(ns, "cnaePrep", Digitos(e.CnaePrincipal)),
                    E(ns, "aliqGilrat", E(ns, "aliqRat", ((int)Math.Round(p.RatAliquota)).ToString(Inv)), E(ns, "fap", p.Fap.ToString("0.0000", Inv)))))),
        ]);

    /// <summary>S-1010 – Tabela de rubricas.</summary>
    public static string S1010(string id, EmpresaFolha e, Rubrica r, DateOnly iniValid, int ambiente = 2)
        => Montar("evtTabRubrica", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: false),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "infoRubrica", E(ns, "inclusao",
                E(ns, "ideRubrica", E(ns, "codRubr", r.Codigo), E(ns, "ideTabRubr", "TAB1"), E(ns, "iniValid", Periodo(iniValid))),
                E(ns, "dadosRubrica", E(ns, "dscRubr", r.Descricao), E(ns, "natRubr", r.Natureza),
                    E(ns, "tpRubr", r.Tipo switch { 'P' => 1, 'D' => 2, _ => 3 }),
                    E(ns, "codIncCP", r.IncInss), E(ns, "codIncIRRF", r.IncIrrf), E(ns, "codIncFGTS", r.IncFgts)))),
        ]);

    /// <summary>S-1020 – Tabela de lotações tributárias (departamento ou lotação padrão).</summary>
    public static string S1020(string id, EmpresaFolha e, string codigo, ParametroEmpresa p, DateOnly iniValid, int ambiente = 2)
        => Montar("evtTabLotacao", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: false),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "infoLotacao", E(ns, "inclusao",
                E(ns, "ideLotacao", E(ns, "codLotacao", codigo), E(ns, "iniValid", Periodo(iniValid))),
                E(ns, "dadosLotacao", E(ns, "tpLotacao", "01"),
                    E(ns, "fpasLotacao", E(ns, "fpas", p.Fpas), E(ns, "codTercs", p.CodTerceiros))))),
        ]);

    // ------------------------------------------------------------------ eventos não periódicos

    private static string UnidadeSalario(Funcionario f) => f.TipoSalario == "HORISTA" ? "1" : "5";

    private static XElement InfoContrato(XNamespace ns, EmpresaFolha e, Funcionario f, string nmCargo, string cbo, decimal salario, DateOnly? dtTermino)
        => E(ns, "infoContrato",
            E(ns, "nmCargo", nmCargo), E(ns, "CBOCargo", cbo), E(ns, "codCateg", f.Categoria),
            E(ns, "remuneracao", E(ns, "vrSalFx", Decimal(salario)), E(ns, "undSalFixo", UnidadeSalario(f))),
            E(ns, "duracao", E(ns, "tpContr", f.TipoContrato is "DETERMINADO" or "EXPERIENCIA" ? 2 : 1), dtTermino is { } t ? E(ns, "dtTerm", Data(t)) : null),
            E(ns, "localTrabalho", E(ns, "localTrabGeral", E(ns, "tpInsc", 1), E(ns, "nrInsc", Digitos(e.Cnpj)))),
            E(ns, "horContratual", E(ns, "qtdHrsSem", Decimal(f.JornadaSemanal)), E(ns, "tpJornada", f.TipoJornada), E(ns, "tmpParc", 0),
              E(ns, "dscJorn", f.DescricaoJornada ?? $"Jornada contratual de {f.JornadaSemanal:0.##} horas semanais")));

    /// <summary>S-2200 – Cadastramento inicial do vínculo e admissão do trabalhador.</summary>
    public static string S2200(string id, EmpresaFolha e, Funcionario f, int ambiente = 2)
        => Montar("evtAdmissao", id, ns =>
        [
            IdeEvento(ns, ambiente),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "trabalhador",
                E(ns, "cpfTrab", Digitos(f.Cpf)), E(ns, "nmTrab", f.Nome), E(ns, "sexo", f.Sexo), E(ns, "racaCor", f.RacaCor), E(ns, "estCiv", f.EstadoCivil),
                E(ns, "grauInstr", f.GrauInstrucao),
                E(ns, "nascimento", E(ns, "dtNascto", Data(f.Nascimento)), E(ns, "paisNascto", "105"), E(ns, "paisNac", "105")),
                f.Logradouro is null ? null : E(ns, "endereco", E(ns, "brasil",
                    f.TipoLogradouro is { Length: > 0 } tl ? E(ns, "tpLograd", tl) : null,
                    E(ns, "dscLograd", f.Logradouro), E(ns, "nrLograd", f.Numero ?? "S/N"),
                    f.Complemento is { Length: > 0 } c ? E(ns, "complemento", c) : null,
                    f.Bairro is { Length: > 0 } b ? E(ns, "bairro", b) : null,
                    E(ns, "cep", Digitos(f.Cep)), E(ns, "codMunic", f.CodMunicipioIbge ?? e.CodMunicipioIbge), E(ns, "uf", f.Uf ?? e.Uf))),
                f.Dependentes.Select(d => E(ns, "dependente", E(ns, "tpDep", d.Tipo), E(ns, "nmDep", d.Nome), E(ns, "dtNascto", Data(d.Nascimento)),
                    d.Cpf is { Length: > 0 } cd ? E(ns, "cpfDep", Digitos(cd)) : null, E(ns, "depIRRF", SN(d.DepIrrf)), E(ns, "depSF", SN(d.DepSalarioFamilia)),
                    E(ns, "incTrab", SN(d.Incapaz)))),
                f.Telefone is { Length: > 0 } || f.Email is { Length: > 0 }
                    ? E(ns, "contato", f.Telefone is { Length: > 0 } tel ? E(ns, "fonePrinc", Digitos(tel)) : null, f.Email is { Length: > 0 } em ? E(ns, "emailPrinc", em) : null)
                    : null),
            E(ns, "vinculo",
                E(ns, "matricula", f.Matricula), E(ns, "tpRegTrab", 1), E(ns, "tpRegPrev", 1), E(ns, "cadIni", "N"),
                E(ns, "infoRegimeTrab", E(ns, "infoCeletista", E(ns, "dtAdm", Data(f.Admissao)), E(ns, "tpAdmissao", 1), E(ns, "indAdmissao", 1),
                    E(ns, "tpRegJor", 1), E(ns, "natAtividade", 1), E(ns, "cnpjSindCategProf", Digitos(f.CnpjSindicato)))),
                InfoContrato(ns, e, f, f.CargoNome ?? "Não informado", f.Cbo ?? "000000", f.Salario, f.TerminoContrato)),
        ]);

    /// <summary>S-2206 – Alteração de contrato de trabalho (salário e/ou cargo).</summary>
    public static string S2206(string id, EmpresaFolha e, Funcionario f, AlteracaoContratual a, int ambiente = 2)
        => Montar("evtAltContratual", id, ns =>
        [
            IdeEvento(ns, ambiente),
            IdeEmpregador(ns, e.Cnpj),
            IdeVinculo(ns, f.Cpf, f.Matricula),
            E(ns, "altContratual", E(ns, "dtAlteracao", Data(a.Data)), E(ns, "dscAlt", a.Descricao),
                E(ns, "vinculo", E(ns, "tpRegPrev", 1),
                    E(ns, "infoRegimeTrab", E(ns, "infoCeletista", E(ns, "tpRegJor", 1), E(ns, "natAtividade", 1), E(ns, "cnpjSindCategProf", Digitos(f.CnpjSindicato)))),
                    InfoContrato(ns, e, f, a.CargoNovo ?? f.CargoNome ?? "Não informado", f.Cbo ?? "000000", a.SalarioNovo ?? f.Salario, f.TerminoContrato))),
        ]);

    /// <summary>S-2230 – Afastamento temporário (início ou término).</summary>
    public static string S2230(string id, EmpresaFolha e, Funcionario f, Afastamento a, bool termino, int ambiente = 2)
        => Montar("evtAfastTemp", id, ns =>
        [
            IdeEvento(ns, ambiente),
            IdeEmpregador(ns, e.Cnpj),
            IdeVinculo(ns, f.Cpf, f.Matricula),
            E(ns, "infoAfastamento", termino
                ? E(ns, "fimAfastamento", E(ns, "dtTermAfast", Data(a.Fim!.Value)))
                : E(ns, "iniAfastamento", E(ns, "dtIniAfast", Data(a.Inicio)), E(ns, "codMotAfast", a.CodigoMotivo),
                    a.Observacao is { Length: > 0 } o ? E(ns, "observacao", o) : null)),
        ]);

    /// <summary>S-2299 – Desligamento.</summary>
    public static string S2299(string id, EmpresaFolha e, Funcionario f, RescisaoRegistro r, DateOnly? dataProjetadaAviso, int ambiente = 2)
        => Montar("evtDeslig", id, ns =>
        [
            IdeEvento(ns, ambiente),
            IdeEmpregador(ns, e.Cnpj),
            IdeVinculo(ns, f.Cpf, f.Matricula),
            E(ns, "infoDeslig", E(ns, "mtvDeslig", r.Motivo.CodigoEsocial()), E(ns, "dtDeslig", Data(r.Desligamento)),
                E(ns, "indPagtoAPI", SN(r.Aviso == TipoAviso.Indenizado)),
                r.Aviso == TipoAviso.Indenizado && dataProjetadaAviso is { } dp ? E(ns, "dtProjFimAPI", Data(dp)) : null),
        ]);

    // ------------------------------------------------------------------ eventos periódicos

    private static string CodigoDemonstrativo(CalculoGravado c) => c.Tipo switch
    {
        TipoFolha.Mensal => "MENSAL",
        TipoFolha.Ferias => $"FER{c.OrigemId}",
        TipoFolha.Rescisao => $"RESC{c.OrigemId}",
        _ => "DECIMO",
    };

    /// <summary>S-1200 – Remuneração do trabalhador vinculado ao RGPS (um demonstrativo por cálculo do período).</summary>
    public static string S1200(string id, EmpresaFolha e, Funcionario f, DateOnly perApur, int indApuracao, IReadOnlyList<CalculoGravado> calculos,
                               string codLotacao, int ambiente = 2)
        => Montar("evtRemun", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: true, indApuracao, indApuracao == 2 ? perApur.Year.ToString(Inv) : Periodo(perApur)),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "ideTrabalhador", E(ns, "cpfTrab", Digitos(f.Cpf))),
            .. calculos.Select(c => E(ns, "dmDev", E(ns, "ideDmDev", CodigoDemonstrativo(c)), E(ns, "codCateg", f.Categoria),
                E(ns, "infoPerApur", E(ns, "ideEstabLot", E(ns, "tpInsc", 1), E(ns, "nrInsc", Digitos(e.Cnpj)), E(ns, "codLotacao", codLotacao),
                    E(ns, "remunPerApur", E(ns, "matricula", f.Matricula),
                        c.Itens.Where(i => i.Tipo is 'P' or 'D' && i.Valor > 0).Select(i => E(ns, "itensRemun", E(ns, "codRubr", i.Codigo), E(ns, "ideTabRubr", "TAB1"),
                            i.Referencia > 0 && i.Codigo != RubricasPadrao.Salario ? E(ns, "qtdRubr", i.Referencia.ToString("0.##", Inv)) : null,
                            E(ns, "vrRubr", Decimal(i.Valor))))))))),
        ]);

    /// <summary>S-1210 – Pagamentos de rendimentos do trabalho (um por trabalhador e mês; inclui folha mensal, férias, rescisão e 13º pagos no período).</summary>
    public static string S1210(string id, EmpresaFolha e, Funcionario f, DateOnly perApur, IReadOnlyList<CalculoGravado> calculos, DateOnly dataPagamentoPadrao,
                               int ambiente = 2)
        => Montar("evtPgtos", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: true, null, Periodo(perApur)),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "ideBenef", E(ns, "cpfBenef", Digitos(f.Cpf)),
                calculos.Select(c => E(ns, "infoPgto", E(ns, "dtPgto", Data(c.DataPagamento ?? dataPagamentoPadrao)), E(ns, "tpPgto", 1),
                    E(ns, "perRef", c.Tipo == TipoFolha.Decimo2 ? c.Competencia.Year.ToString(Inv) : Periodo(c.Competencia)),
                    E(ns, "ideDmDev", CodigoDemonstrativo(c)), E(ns, "vrLiq", Decimal(c.Liquido))))),
        ]);

    /// <summary>S-1299 – Fechamento dos eventos periódicos.</summary>
    public static string S1299(string id, EmpresaFolha e, ParametroEmpresa p, DateOnly perApur, bool temRemuneracao, bool temPagamentos, int ambiente = 2)
        => Montar("evtFechaEvPer", id, ns =>
        [
            IdeEvento(ns, ambiente, comRetif: false, 1, Periodo(perApur)),
            IdeEmpregador(ns, e.Cnpj),
            E(ns, "infoFech", E(ns, "evtRemun", SN(temRemuneracao)), E(ns, "evtPgtos", SN(temPagamentos)), E(ns, "evtComProd", "N"),
                E(ns, "evtContratAvNP", "N"), E(ns, "evtInfoComplPer", "N"), E(ns, "transDCTFWeb", "S")),
        ]);
}
