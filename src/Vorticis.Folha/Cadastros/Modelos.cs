using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Cadastros;

public sealed record Departamento(long Id, string Codigo, string Nome, bool Ativo = true, bool EmUso = false);

public sealed record Cargo(long Id, string Codigo, string Nome, string? Cbo, bool Ativo = true, bool EmUso = false);

public sealed record DependenteFolha(long Id, string Nome, string? Cpf, DateOnly Nascimento, string Tipo, bool Incapaz, bool DepIrrf, bool DepSalarioFamilia);

/// <summary>Funcionário (vínculo CLT) com os dados pessoais, contratuais e bancários.</summary>
public sealed record Funcionario(
    long Id, string Matricula, string Nome, string Cpf, string? PisNis, DateOnly Nascimento, string Sexo, int RacaCor, int EstadoCivil,
    string GrauInstrucao, string? Email, string? Telefone, string? Cep, string? Logradouro, string? Numero, string? Complemento, string? Bairro,
    string? CodMunicipioIbge, string? Uf,
    DateOnly Admissao, string Categoria, string TipoContrato, DateOnly? TerminoContrato, long? CargoId, long? DepartamentoId, string? Cbo,
    string TipoSalario, decimal Salario, decimal JornadaSemanal, decimal JornadaMensal, long? CentroCustoId, decimal Insalubridade, bool Periculosidade,
    string? Banco, string? Agencia, string? Conta, string? TipoConta, string? ChavePix,
    string Situacao, DateOnly? Desligamento, string? MotivoDesligamento, IReadOnlyList<DependenteFolha> Dependentes,
    string? CargoNome = null, string? DepartamentoNome = null, string? CentroCustoCodigo = null,
    string? TipoLogradouro = null, string? CnpjSindicato = null, int TipoJornada = 9, string? DescricaoJornada = null)
{
    public bool Ativo => Situacao != "DESLIGADO";

    public FuncionarioCalc ParaCalculo() => new(Id, Matricula, Nome, Admissao, Desligamento, TipoSalario, Salario, JornadaMensal, Insalubridade,
        Periculosidade, Categoria, Dependentes.Select(d => new DependenteCalc(d.Nome, d.Nascimento, d.Incapaz, d.DepIrrf, d.DepSalarioFamilia)).ToArray());
}

public sealed record Rubrica(long Id, string Codigo, string Descricao, char Tipo, string Natureza, string IncInss, string IncIrrf, string IncFgts,
                             bool Variavel, bool Sistema, bool Ativa = true)
{
    public RubricaCalc ParaCalculo() => new(Codigo, Descricao, Tipo, Natureza, IncInss, IncIrrf, IncFgts, Variavel);
}

public sealed record ParametroEmpresa(decimal RatAliquota, decimal Fap, decimal TerceirosAliquota, string Fpas, string CodTerceiros, string? SimplesAnexo,
                                      string ClassTrib, string? ResponsavelNome, string? ResponsavelCpf, string? ResponsavelEmail, string? ResponsavelTelefone)
{
    public static readonly ParametroEmpresa Padrao = new(2m, 1m, 5.8m, "515", "0079", null, "99", null, null, null, null);
}

/// <summary>Configuração da folha por empresa (folha.configuracao_empresa): dia de pagamento e padrões do eSocial.</summary>
public sealed record ConfiguracaoFolha(int DiaPagamento = 5, int AmbienteEsocial = 2, string TransmissorEsocial = "EMPREGADOR")
{
    public static readonly ConfiguracaoFolha Padrao = new();
}

public sealed record EventoVariavel(long Id, long FuncionarioId, string FuncionarioNome, DateOnly Competencia, string RubricaCodigo, string RubricaDescricao,
                                    decimal Referencia, decimal? Valor, string? Observacao);

public sealed record StatusCompetencia(DateOnly Competencia, bool Fechada, DateTimeOffset? FechadaEm);

/// <summary>Item gravado de um cálculo.</summary>
public sealed record ItemGravado(string Codigo, string Descricao, char Tipo, decimal Referencia, decimal Valor, string Memoria);

/// <summary>Cálculo gravado (um funcionário, uma competência, um tipo de folha).</summary>
public sealed record CalculoGravado(
    long Id, long FuncionarioId, string Matricula, string FuncionarioNome, string Cpf, DateOnly Competencia, TipoFolha Tipo, long? OrigemId, DateOnly? DataPagamento,
    long? CentroCustoId, string? CentroCustoCodigo, decimal TotalProventos, decimal TotalDescontos, decimal Liquido, decimal BaseInss, decimal BaseIrrf,
    decimal BaseFgts, decimal Inss, decimal Irrf, decimal Fgts, decimal SalarioFamilia, decimal EncInssPatronal, decimal EncRat, decimal EncTerceiros,
    IReadOnlyList<ItemGravado> Itens)
{
    public decimal EncargosPrevidenciarios => EncInssPatronal + EncRat + EncTerceiros;
}

/// <summary>Dados para gravar um cálculo: o resultado das regras mais os encargos patronais.</summary>
public sealed record NovoCalculo(long FuncionarioId, DateOnly Competencia, TipoFolha Tipo, long? OrigemId, DateOnly? DataPagamento, long? CentroCustoId,
                                 ResultadoFolha Resultado, EncargosPatronais Encargos);

public sealed record FeriasRegistro(long Id, long FuncionarioId, string FuncionarioNome, DateOnly AquisitivoInicio, DateOnly AquisitivoFim, DateOnly GozoInicio,
                                    int DiasGozo, int AbonoDias, int Faltas, DateOnly DataPagamento, string Status, decimal Liquido);

public sealed record RescisaoRegistro(long Id, long FuncionarioId, string FuncionarioNome, MotivoRescisao Motivo, DateOnly Desligamento, TipoAviso Aviso,
                                      int DiasAviso, decimal SaldoFgts, DateOnly DataPagamento, string Status, decimal Liquido, decimal MultaFgts);

public sealed record Afastamento(long Id, long FuncionarioId, string FuncionarioNome, DateOnly Inicio, DateOnly? Fim, string CodigoMotivo, string? Observacao);

public sealed record AlteracaoContratual(long Id, long FuncionarioId, DateOnly Data, string Descricao, decimal? SalarioAnterior, decimal? SalarioNovo,
                                         string? CargoAnterior, string? CargoNovo);

/// <summary>Sócio (core.socio) com a configuração de pró-labore; <see cref="Id"/> nulo = ainda não configurado.</summary>
public sealed record ProLaboreSocio(long SocioId, string Nome, string? Cpf, string Qualificacao, long? Id, bool Retira, decimal Valor,
                                    DateOnly? Inicio, DateOnly? Fim, int DependentesIrrf, decimal InssOutrasFontes, string? Observacao);

/// <summary>Pró-labore calculado de um sócio em uma competência.</summary>
public sealed record ProLaboreCalculo(long Id, long SocioId, string Nome, DateOnly Competencia, decimal Bruto, decimal Inss, decimal Irrf,
                                      decimal Liquido, decimal InssPatronal, string Memoria);
