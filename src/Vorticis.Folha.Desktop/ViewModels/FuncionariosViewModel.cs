using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;

namespace Vorticis.Folha.Desktop.ViewModels;

public sealed record FuncionarioLinha(Funcionario F)
{
    public string Matricula => F.Matricula;
    public string Nome => F.Nome;
    public string Cpf => Cpf_(F.Cpf);
    public string Cargo => F.CargoNome ?? "";
    public string Admissao => F.Admissao.ToString("dd/MM/yyyy");
    public string Salario => Formato.Moeda(F.Salario) + (F.TipoSalario == "HORISTA" ? "/h" : "");
    public string Situacao => F.Situacao switch { "ATIVO" => "Ativo", "AFASTADO" => "Afastado", _ => $"Desligado {F.Desligamento:dd/MM/yyyy}" };
    private static string Cpf_(string c) => Domain.Cadastros.Cpf.Formatar(c);
}

/// <summary>Dependente em edição (IRRF e salário-família).</summary>
public partial class DependenteEdicao : ObservableObject
{
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cpf = "";
    [ObservableProperty] private DateTime? _nascimento;
    [ObservableProperty] private Opcao? _tipo = FuncionariosViewModel.TiposDependente[2];
    [ObservableProperty] private bool _incapaz;
    [ObservableProperty] private bool _depIrrf = true;
    [ObservableProperty] private bool _depSalarioFamilia;

    public static DependenteEdicao De(DependenteFolha d) => new()
    {
        Nome = d.Nome, Cpf = d.Cpf ?? "", Nascimento = d.Nascimento.ToDateTime(TimeOnly.MinValue),
        Tipo = FuncionariosViewModel.TiposDependente.FirstOrDefault(t => t.Codigo == d.Tipo) ?? new Opcao(d.Tipo, d.Tipo),
        Incapaz = d.Incapaz, DepIrrf = d.DepIrrf, DepSalarioFamilia = d.DepSalarioFamilia,
    };
}

/// <summary>Folha › Funcionários › Cadastro de funcionários: dados pessoais, vínculo, jornada, centro de custo, banco e dependentes.</summary>
public partial class FuncionariosViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public static IReadOnlyList<Opcao> Sexos { get; } = [new("M", "Masculino"), new("F", "Feminino")];
    public static IReadOnlyList<Opcao> Racas { get; } =
        [new("1", "1 – Branca"), new("2", "2 – Preta"), new("3", "3 – Parda"), new("4", "4 – Amarela"), new("5", "5 – Indígena"), new("6", "6 – Não informado")];
    public static IReadOnlyList<Opcao> EstadosCivis { get; } =
        [new("1", "1 – Solteiro(a)"), new("2", "2 – Casado(a)"), new("3", "3 – Divorciado(a)"), new("4", "4 – Separado(a)"), new("5", "5 – Viúvo(a)")];
    public static IReadOnlyList<Opcao> Contratos { get; } =
        [new("INDETERMINADO", "Prazo indeterminado"), new("DETERMINADO", "Prazo determinado"), new("EXPERIENCIA", "Experiência"),
         new("APRENDIZ", "Aprendiz"), new("ESTAGIO", "Estágio")];
    public static IReadOnlyList<Opcao> Categorias { get; } =
        [new("101", "101 – Empregado geral (CLT)"), new("103", "103 – Aprendiz"), new("901", "901 – Estagiário")];
    public static IReadOnlyList<Opcao> TiposSalario { get; } = [new("MENSAL", "Mensalista"), new("HORISTA", "Horista (salário = valor da hora)")];
    public static IReadOnlyList<Opcao> Insalubridades { get; } = [new("0", "Sem insalubridade"), new("10", "10% (grau mínimo)"), new("20", "20% (grau médio)"), new("40", "40% (grau máximo)")];
    public static IReadOnlyList<Opcao> TiposConta { get; } = [new("CORRENTE", "Conta corrente"), new("POUPANCA", "Poupança"), new("SALARIO", "Conta salário")];
    public static IReadOnlyList<Opcao> Situacoes { get; } = [new("ATIVO", "Ativo"), new("AFASTADO", "Afastado"), new("DESLIGADO", "Desligado")];
    public static IReadOnlyList<Opcao> TiposDependente { get; } =
        [new("01", "01 – Cônjuge"), new("02", "02 – Companheiro(a)"), new("03", "03 – Filho(a) ou enteado(a)"), new("04", "04 – Filho(a)/enteado(a) universitário(a)"),
         new("06", "06 – Irmão, neto ou bisneto (guarda judicial)"), new("09", "09 – Pais, avós e bisavós"), new("10", "10 – Menor sob guarda"),
         new("11", "11 – Pessoa incapaz (tutela/curatela)"), new("12", "12 – Ex-cônjuge"), new("99", "99 – Agregado/outros")];

    public ObservableCollection<FuncionarioLinha> Lista { get; } = [];
    public ObservableCollection<Cargo> Cargos { get; } = [];
    public ObservableCollection<Departamento> Departamentos { get; } = [];
    public ObservableCollection<Application.Contabil.CentroCustoDetalhe> Centros { get; } = [];
    public ObservableCollection<DependenteEdicao> Dependentes { get; } = [];

    [ObservableProperty] private FuncionarioLinha? _selecionado;
    [ObservableProperty] private string _matricula = "";
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cpf = "";
    [ObservableProperty] private string _pis = "";
    [ObservableProperty] private DateTime? _nascimento;
    [ObservableProperty] private Opcao? _sexo = Sexos[1];
    [ObservableProperty] private Opcao? _raca = Racas[5];
    [ObservableProperty] private Opcao? _estadoCivil = EstadosCivis[0];
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _cep = "";
    [ObservableProperty] private string _logradouro = "";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _complemento = "";
    [ObservableProperty] private string _bairro = "";
    [ObservableProperty] private string _codMunicipio = "";
    [ObservableProperty] private string _uf = "";
    [ObservableProperty] private DateTime? _admissao = DateTime.Today;
    [ObservableProperty] private Opcao? _categoria = Categorias[0];
    [ObservableProperty] private Opcao? _contrato = Contratos[0];
    [ObservableProperty] private DateTime? _termino;
    [ObservableProperty] private Cargo? _cargo;
    [ObservableProperty] private Departamento? _departamento;
    [ObservableProperty] private string _cbo = "";
    [ObservableProperty] private string _tipoLogradouro = "";
    [ObservableProperty] private string _cnpjSindicato = "";
    [ObservableProperty] private Opcao? _tipoSalario = TiposSalario[0];
    [ObservableProperty] private string _salario = "";
    [ObservableProperty] private string _jornadaSemanal = "44";
    [ObservableProperty] private string _jornadaMensal = "220";
    [ObservableProperty] private Application.Contabil.CentroCustoDetalhe? _centro;
    [ObservableProperty] private Opcao? _insalubridade = Insalubridades[0];
    [ObservableProperty] private bool _periculosidade;
    [ObservableProperty] private string _banco = "";
    [ObservableProperty] private string _agencia = "";
    [ObservableProperty] private string _conta = "";
    [ObservableProperty] private Opcao? _tipoConta = TiposConta[0];
    [ObservableProperty] private string _chavePix = "";
    [ObservableProperty] private Opcao? _situacao = Situacoes[0];
    [ObservableProperty] private DateTime? _desligamento;
    [ObservableProperty] private string _filtro = "";

    private IReadOnlyList<Funcionario> _todos = [];

    public bool PodeEditar => Contexto.Pode(Permissao.FolhaCadastrosManter);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "funcionário";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Matricula} – {s.Nome}" : null;

    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeEditar)); AtualizarBarra(); }

    partial void OnFiltroChanged(string value) => Filtrar();

    private void Filtrar()
    {
        var id = Selecionado?.F.Id;
        Lista.Clear();
        var f = Filtro.Trim();
        foreach (var x in _todos.Where(x => f.Length == 0 || x.Nome.Contains(f, StringComparison.OrdinalIgnoreCase) || x.Matricula.Contains(f) || x.Cpf.Contains(new string(f.Where(char.IsDigit).ToArray()))))
            Lista.Add(new FuncionarioLinha(x));
        Selecionado = Lista.FirstOrDefault(l => l.F.Id == id);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var cad = app.Folha().Cadastros;
        var funcionarios = await cad.ListarFuncionariosAsync(empresa, sessao.Id);
        var cargos = await cad.ListarCargosAsync(empresa, sessao.Id);
        var deptos = await cad.ListarDepartamentosAsync(empresa, sessao.Id);
        var centros = await app.PlanoContas.ListarCentrosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        _todos = funcionarios;
        Cargos.Clear(); foreach (var c in cargos.Where(c => c.Ativo)) Cargos.Add(c);
        Departamentos.Clear(); foreach (var d in deptos.Where(d => d.Ativo)) Departamentos.Add(d);
        Centros.Clear(); foreach (var c in centros.Where(c => c.Ativo && c.Analitico)) Centros.Add(c);
        Filtrar();
    }

    partial void OnSelecionadoChanged(FuncionarioLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    private static Opcao Achar(IReadOnlyList<Opcao> lista, string codigo) => lista.FirstOrDefault(o => o.Codigo == codigo) ?? lista[0];
    private static DateTime? Dt(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

    protected override void MostrarSelecionado()
    {
        Dependentes.Clear();
        if (Selecionado?.F is not { } f) { PrepararInclusao(); return; }
        (Matricula, Nome, Cpf, Pis, Nascimento) = (f.Matricula, f.Nome, Domain.Cadastros.Cpf.Formatar(f.Cpf), f.PisNis ?? "", Dt(f.Nascimento));
        (Sexo, Raca, EstadoCivil) = (Achar(Sexos, f.Sexo), Achar(Racas, f.RacaCor.ToString()), Achar(EstadosCivis, f.EstadoCivil.ToString()));
        (Email, Telefone, Cep, Logradouro, Numero, Complemento, Bairro, CodMunicipio, Uf) =
            (f.Email ?? "", f.Telefone ?? "", f.Cep ?? "", f.Logradouro ?? "", f.Numero ?? "", f.Complemento ?? "", f.Bairro ?? "", f.CodMunicipioIbge ?? "", f.Uf ?? "");
        (Admissao, Categoria, Contrato, Termino) = (Dt(f.Admissao), Achar(Categorias, f.Categoria), Achar(Contratos, f.TipoContrato), Dt(f.TerminoContrato));
        (Cargo, Departamento, Cbo) = (Cargos.FirstOrDefault(c => c.Id == f.CargoId), Departamentos.FirstOrDefault(d => d.Id == f.DepartamentoId), f.Cbo ?? "");
        (TipoLogradouro, CnpjSindicato) = (f.TipoLogradouro ?? "", f.CnpjSindicato ?? "");
        (TipoSalario, Salario) = (Achar(TiposSalario, f.TipoSalario), f.Salario.ToString("N2", Formato.PtBr));
        (JornadaSemanal, JornadaMensal, Centro) = (Formato.Num(f.JornadaSemanal), Formato.Num(f.JornadaMensal), Centros.FirstOrDefault(c => c.Id == f.CentroCustoId));
        (Insalubridade, Periculosidade) = (Achar(Insalubridades, ((int)f.Insalubridade).ToString()), f.Periculosidade);
        (Banco, Agencia, Conta, TipoConta, ChavePix) = (f.Banco ?? "", f.Agencia ?? "", f.Conta ?? "", Achar(TiposConta, f.TipoConta ?? "CORRENTE"), f.ChavePix ?? "");
        (Situacao, Desligamento) = (Achar(Situacoes, f.Situacao), Dt(f.Desligamento));
        foreach (var d in f.Dependentes) Dependentes.Add(DependenteEdicao.De(d));
    }

    protected override void PrepararInclusao()
    {
        Dependentes.Clear();
        (Matricula, Nome, Cpf, Pis, Nascimento, Sexo, Raca, EstadoCivil) = ("", "", "", "", null, Sexos[1], Racas[5], EstadosCivis[0]);
        (Email, Telefone, Cep, Logradouro, Numero, Complemento, Bairro, CodMunicipio, Uf) = ("", "", "", "", "", "", "", "", "");
        (Admissao, Categoria, Contrato, Termino, Cargo, Departamento, Cbo) = (DateTime.Today, Categorias[0], Contratos[0], null, null, null, "");
        (TipoLogradouro, CnpjSindicato) = ("", "");
        (TipoSalario, Salario, JornadaSemanal, JornadaMensal, Centro, Insalubridade, Periculosidade) = (TiposSalario[0], "", "44", "220", null, Insalubridades[0], false);
        (Banco, Agencia, Conta, TipoConta, ChavePix, Situacao, Desligamento) = ("", "", "", TiposConta[0], "", Situacoes[0], null);
    }

    partial void OnCargoChanged(Cargo? value)
    {
        if (EmEdicao && value?.Cbo is { Length: > 0 } cbo && string.IsNullOrWhiteSpace(Cbo)) Cbo = cbo;
    }

    [RelayCommand]
    private void AdicionarDependente() { if (EmEdicao) Dependentes.Add(new DependenteEdicao()); }

    [RelayCommand]
    private void RemoverDependente(DependenteEdicao? d) { if (EmEdicao && d is not null) Dependentes.Remove(d); }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            decimal Num(string? t, decimal padrao) => Numeros.LerValor(t) ?? padrao;
            var salario = Numeros.LerValor(Salario);
            if (salario is null) { Erros.Add("Informe o salário (ou o valor da hora)."); return; }
            if (Nascimento is null || Admissao is null) { Erros.Add("Informe a data de nascimento e a de admissão."); return; }
            var atual = inclusao ? null : Selecionado?.F;
            if (!inclusao && atual is null) { Erros.Add("Selecione um funcionário."); return; }
            var deps = Dependentes.Select(d => d.Nascimento is null ? null : new DependenteFolha(0, d.Nome, d.Cpf, DateOnly.FromDateTime(d.Nascimento.Value), d.Tipo?.Codigo ?? "03",
                d.Incapaz, d.DepIrrf, d.DepSalarioFamilia)).ToList();
            if (deps.Any(d => d is null)) { Erros.Add("Informe a data de nascimento de todos os dependentes."); return; }
            var f = new Funcionario(atual?.Id ?? 0, Matricula, Nome, Cpf, Pis, DateOnly.FromDateTime(Nascimento.Value), Sexo?.Codigo ?? "F", int.Parse(Raca?.Codigo ?? "6"),
                int.Parse(EstadoCivil?.Codigo ?? "1"), atual?.GrauInstrucao ?? "09", Email, Telefone, Cep, Logradouro, Numero, Complemento, Bairro, CodMunicipio, Uf,
                DateOnly.FromDateTime(Admissao.Value), Categoria?.Codigo ?? "101", Contrato?.Codigo ?? "INDETERMINADO", FolhaExtensoes.Data(Termino), Cargo?.Id, Departamento?.Id, Cbo,
                TipoSalario?.Codigo ?? "MENSAL", salario.Value, Num(JornadaSemanal, 44), Num(JornadaMensal, 220), Centro?.Id, decimal.Parse(Insalubridade?.Codigo ?? "0"), Periculosidade,
                Banco, Agencia, Conta, TipoConta?.Codigo, ChavePix, Situacao?.Codigo ?? "ATIVO", FolhaExtensoes.Data(Desligamento), atual?.MotivoDesligamento, deps!,
                TipoLogradouro: TipoLogradouro, CnpjSindicato: CnpjSindicato, TipoJornada: atual?.TipoJornada ?? 9, DescricaoJornada: atual?.DescricaoJornada);
            var r = await app.Folha().Cadastros.GravarFuncionarioAsync(empresa, f, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.F.Id == r.Valor);
            Mensagem = inclusao ? "Funcionário incluído." : "Funcionário alterado.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }

    protected override string DetalheExclusao => "\n\nFuncionários com folha, férias ou rescisão não podem ser excluídos: registre o desligamento.";

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Folha().Cadastros.ExcluirFuncionarioAsync(empresa, Selecionado.F.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Funcionário excluído.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }
}
