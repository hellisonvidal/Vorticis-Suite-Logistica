using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

public enum TipoFolha { Mensal, Ferias, Decimo1, Decimo2, Rescisao }

public static class TipoFolhaExtensoes
{
    public static string Sql(this TipoFolha t) => t switch
    {
        TipoFolha.Mensal => "MENSAL",
        TipoFolha.Ferias => "FERIAS",
        TipoFolha.Decimo1 => "DECIMO_1",
        TipoFolha.Decimo2 => "DECIMO_2",
        TipoFolha.Rescisao => "RESCISAO",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    public static TipoFolha DeSql(string s) => Enum.GetValues<TipoFolha>().Single(t => t.Sql() == s);

    public static string Descricao(this TipoFolha t) => t switch
    {
        TipoFolha.Mensal => "Folha mensal",
        TipoFolha.Ferias => "Férias",
        TipoFolha.Decimo1 => "13º salário – 1ª parcela",
        TipoFolha.Decimo2 => "13º salário – 2ª parcela",
        TipoFolha.Rescisao => "Rescisão",
        _ => "",
    };
}

/// <summary>Dependente do funcionário, com o que importa para o cálculo.</summary>
public sealed record DependenteCalc(string Nome, DateOnly Nascimento, bool Incapaz, bool DepIrrf, bool DepSalarioFamilia);

/// <summary>Funcionário com os dados que entram no cálculo.</summary>
public sealed record FuncionarioCalc(
    long Id, string Matricula, string Nome, DateOnly Admissao, DateOnly? Desligamento, string TipoSalario, decimal Salario,
    decimal JornadaMensalHoras, decimal InsalubridadePercentual, bool Periculosidade, string Categoria,
    IReadOnlyList<DependenteCalc> Dependentes)
{
    public bool Horista => TipoSalario == "HORISTA";

    /// <summary>Salário mensal cheio (horista: valor da hora x jornada mensal).</summary>
    public decimal SalarioMensal => Dinheiro.Arredondar(Horista ? Salario * JornadaMensalHoras : Salario);

    public decimal ValorHora => Horista ? Salario : Salario / (JornadaMensalHoras <= 0 ? 220m : JornadaMensalHoras);

    public bool Aprendiz => Categoria == "103";

    /// <summary>Estagiário (categoria 901) não é segurado empregado nem tem FGTS.</summary>
    public bool Estagiario => Categoria == "901";

    public int DependentesIrrf => Dependentes.Count(d => d.DepIrrf);
}

/// <summary>Definição de rubrica usada no cálculo (cadastro da empresa).</summary>
public sealed record RubricaCalc(string Codigo, string Descricao, char Tipo, string NaturezaEsocial, string IncInss, string IncIrrf, string IncFgts,
                                 bool Variavel = false)
{
    public bool EhProvento => Tipo == 'P';
    public bool EhDesconto => Tipo == 'D';
}

/// <summary>Evento variável do mês: referência (horas/dias) e/ou valor informado.</summary>
public sealed record EventoCalc(string RubricaCodigo, decimal Referencia, decimal? Valor);

/// <summary>Item calculado, com a memória de cálculo (como o valor foi obtido).</summary>
public sealed record ItemCalculo(string Codigo, string Descricao, char Tipo, decimal Referencia, decimal Valor, string Memoria,
                                 string IncInss, string IncIrrf, string IncFgts, bool Variavel = false);

/// <summary>O que já foi tributado no mesmo mês por outros cálculos (férias): INSS e IRRF são calculados sobre o acumulado.</summary>
public sealed record AcumuladoMes(decimal BaseInss = 0, decimal Inss = 0, decimal Rendimento = 0, decimal IrrfRetido = 0)
{
    public static readonly AcumuladoMes Nenhum = new();
}

/// <summary>Resultado de um cálculo de folha (mensal, férias, 13º ou rescisão) de um funcionário.</summary>
public sealed record ResultadoFolha(
    IReadOnlyList<ItemCalculo> Itens, decimal BaseInss, decimal BaseIrrf, decimal BaseFgts, decimal Inss, decimal Irrf, decimal Fgts,
    decimal SalarioFamilia, string MemoriaInss, string MemoriaIrrf, decimal MultaFgts = 0)
{
    public decimal TotalProventos => Itens.Where(i => i.Tipo == 'P').Sum(i => i.Valor);
    public decimal TotalDescontos => Itens.Where(i => i.Tipo == 'D').Sum(i => i.Valor);
    public decimal Liquido => TotalProventos - TotalDescontos;
}
