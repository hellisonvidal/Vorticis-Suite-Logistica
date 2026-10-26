using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Infra;

namespace Vorticis.Folha.Desktop.ViewModels;

/// <summary>Opção de lista (código do eSocial ou valor do banco + texto para o usuário).</summary>
public sealed record Opcao(string Codigo, string Texto)
{
    public override string ToString() => Texto;
}

public static class FolhaTelas
{
    public const string Funcionarios = "folha.funcionarios";
    public const string Cargos = "folha.cargos";
    public const string Departamentos = "folha.departamentos";
    public const string Rubricas = "folha.rubricas";
    public const string Parametros = "folha.parametros";
    public const string Eventos = "folha.eventos";
    public const string FolhaMensal = "folha.mensal";
    public const string Holerites = "folha.holerites";
    public const string Ferias = "folha.ferias";
    public const string Decimo = "folha.decimo";
    public const string Rescisoes = "folha.rescisoes";
    public const string Provisoes = "folha.provisoes";
    public const string Contabilizacao = "folha.contabilizacao";
    public const string ESocial = "folha.esocial";
    public const string AssistenteEsocial = "folha.esocial.assistente";
    public const string Transmissao = "folha.esocial.transmissao";
    public const string Resumo = "folha.resumo";
    public const string Encargos = "folha.encargos";
    public const string ProLabore = "folha.prolabore";
}

internal static class FolhaExtensoes
{
    public static ServicosFolha Folha(this VorticisApp app) => ServicosFolha.De(app);

    public static DateOnly InicioMes(DateTime? d) => d is { } v ? new DateOnly(v.Year, v.Month, 1) : new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

    public static DateTime? Mes(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public static DateOnly? Data(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : null;
}

// ---------------------------------------------------------------------------------------------------- departamentos

public sealed record DepartamentoLinha(Departamento D)
{
    public string Codigo => D.Codigo;
    public string Nome => D.Nome;
    public string Situacao => D.Ativo ? "Ativo" : "Inativo";
}

/// <summary>Folha › Funcionários › Departamentos (lotações do eSocial).</summary>
public partial class DepartamentosViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<DepartamentoLinha> Lista { get; } = [];

    [ObservableProperty] private DepartamentoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private bool _ativo = true;

    public bool PodeEditar => Contexto.Pode(Permissao.FolhaCadastrosManter);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "departamento";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Nome}" : null;

    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeEditar)); AtualizarBarra(); }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var itens = await app.Folha().Cadastros.ListarDepartamentosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.D.Id;
        Lista.Clear();
        foreach (var d in itens) Lista.Add(new DepartamentoLinha(d));
        Selecionado = Lista.FirstOrDefault(l => l.D.Id == id);
    }

    partial void OnSelecionadoChanged(DepartamentoLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        if (Selecionado is { } s) (Codigo, Nome, Ativo) = (s.Codigo, s.Nome, s.D.Ativo);
        else (Codigo, Nome, Ativo) = ("", "", true);
    }

    protected override void PrepararInclusao() => (Codigo, Nome, Ativo) = ("", "", true);

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var id = inclusao ? 0 : Selecionado?.D.Id ?? 0;
            var r = await app.Folha().Cadastros.GravarDepartamentoAsync(empresa, new Departamento(id, Codigo, Nome, Ativo), usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.D.Id == r.Valor);
            Mensagem = inclusao ? "Departamento incluído." : "Departamento alterado.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Folha().Cadastros.ExcluirDepartamentoAsync(empresa, Selecionado.D.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Departamento excluído.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }
}

// ---------------------------------------------------------------------------------------------------- cargos

public sealed record CargoLinha(Cargo C)
{
    public string Codigo => C.Codigo;
    public string Nome => C.Nome;
    public string Cbo => C.Cbo ?? "";
    public string Situacao => C.Ativo ? "Ativo" : "Inativo";
}

/// <summary>Folha › Funcionários › Cargos (com o CBO usado no eSocial).</summary>
public partial class CargosViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<CargoLinha> Lista { get; } = [];

    [ObservableProperty] private CargoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cbo = "";
    [ObservableProperty] private bool _ativo = true;

    public bool PodeEditar => Contexto.Pode(Permissao.FolhaCadastrosManter);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "cargo";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Nome}" : null;

    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeEditar)); AtualizarBarra(); }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var itens = await app.Folha().Cadastros.ListarCargosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.C.Id;
        Lista.Clear();
        foreach (var c in itens) Lista.Add(new CargoLinha(c));
        Selecionado = Lista.FirstOrDefault(l => l.C.Id == id);
    }

    partial void OnSelecionadoChanged(CargoLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        if (Selecionado is { } s) (Codigo, Nome, Cbo, Ativo) = (s.Codigo, s.Nome, s.Cbo, s.C.Ativo);
        else (Codigo, Nome, Cbo, Ativo) = ("", "", "", true);
    }

    protected override void PrepararInclusao() => (Codigo, Nome, Cbo, Ativo) = ("", "", "", true);

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var id = inclusao ? 0 : Selecionado?.C.Id ?? 0;
            var r = await app.Folha().Cadastros.GravarCargoAsync(empresa, new Cargo(id, Codigo, Nome, Cbo, Ativo), usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.C.Id == r.Valor);
            Mensagem = inclusao ? "Cargo incluído." : "Cargo alterado.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Folha().Cadastros.ExcluirCargoAsync(empresa, Selecionado.C.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Cargo excluído.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }
}

// ---------------------------------------------------------------------------------------------------- rubricas

public sealed record RubricaLinha(Rubrica R)
{
    public string Codigo => R.Codigo;
    public string Descricao => R.Descricao;
    public string Tipo => R.Tipo switch { 'P' => "Provento", 'D' => "Desconto", _ => "Informativa" };
    public string Natureza => R.Natureza;
    public string Incidencias => $"INSS {R.IncInss} · IRRF {R.IncIrrf} · FGTS {R.IncFgts}";
    public string Situacao => (R.Ativa ? "Ativa" : "Inativa") + (R.Sistema ? " · padrão" : "") + (R.Variavel ? " · variável" : "");
}

/// <summary>Folha › Funcionários › Rubricas (eventos): proventos, descontos e informativas, com incidências e natureza do eSocial.</summary>
public partial class RubricasViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public static IReadOnlyList<Opcao> Tipos { get; } = [new("P", "Provento"), new("D", "Desconto"), new("I", "Informativa")];
    public static IReadOnlyList<Opcao> IncidenciasInss { get; } =
        [new("00", "00 – Não é base de cálculo"), new("11", "11 – Base de cálculo mensal"), new("12", "12 – Base de cálculo do 13º"),
         new("21", "21 – Desconto do segurado (mensal)"), new("22", "22 – Desconto do segurado (13º)")];
    public static IReadOnlyList<Opcao> IncidenciasIrrf { get; } =
        [new("00", "00 – Não é base de cálculo"), new("01", "01 – Rendimento tributável (mensal)"), new("09", "09 – Rendimento tributável (13º)"),
         new("11", "11 – Dedução: previdência oficial"), new("31", "31 – Dedução: pensão alimentícia")];
    public static IReadOnlyList<Opcao> IncidenciasFgts { get; } =
        [new("00", "00 – Não é base de cálculo"), new("11", "11 – Base de cálculo mensal"), new("12", "12 – Base de cálculo do 13º")];

    public ObservableCollection<RubricaLinha> Lista { get; } = [];

    [ObservableProperty] private RubricaLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private Opcao? _tipo = Tipos[0];
    [ObservableProperty] private string _natureza = "1000";
    [ObservableProperty] private Opcao? _incInss = IncidenciasInss[1];
    [ObservableProperty] private Opcao? _incIrrf = IncidenciasIrrf[1];
    [ObservableProperty] private Opcao? _incFgts = IncidenciasFgts[1];
    [ObservableProperty] private bool _variavel;
    [ObservableProperty] private bool _ativa = true;

    public bool PodeEditar => Contexto.Pode(Permissao.FolhaCadastrosManter);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "rubrica";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Descricao}" : null;

    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeEditar)); AtualizarBarra(); }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var itens = await app.Folha().Cadastros.ListarRubricasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.R.Id;
        Lista.Clear();
        foreach (var r in itens) Lista.Add(new RubricaLinha(r));
        Selecionado = Lista.FirstOrDefault(l => l.R.Id == id);
    }

    partial void OnSelecionadoChanged(RubricaLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    private static Opcao Achar(IReadOnlyList<Opcao> lista, string codigo) => lista.FirstOrDefault(o => o.Codigo == codigo) ?? new Opcao(codigo, codigo);

    protected override void MostrarSelecionado()
    {
        if (Selecionado is { } s)
        {
            var r = s.R;
            (Codigo, Descricao, Natureza, Variavel, Ativa) = (r.Codigo, r.Descricao, r.Natureza, r.Variavel, r.Ativa);
            (Tipo, IncInss, IncIrrf, IncFgts) = (Achar(Tipos, r.Tipo.ToString()), Achar(IncidenciasInss, r.IncInss), Achar(IncidenciasIrrf, r.IncIrrf), Achar(IncidenciasFgts, r.IncFgts));
        }
        else PrepararInclusao();
    }

    protected override void PrepararInclusao()
    {
        (Codigo, Descricao, Natureza, Variavel, Ativa) = ("", "", "1000", false, true);
        (Tipo, IncInss, IncIrrf, IncFgts) = (Tipos[0], IncidenciasInss[1], IncidenciasIrrf[1], IncidenciasFgts[1]);
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var id = inclusao ? 0 : Selecionado?.R.Id ?? 0;
            var sistema = !inclusao && Selecionado?.R.Sistema == true;
            var rubrica = new Rubrica(id, Codigo, Descricao, (Tipo?.Codigo ?? "P")[0], Natureza, IncInss?.Codigo ?? "00", IncIrrf?.Codigo ?? "00",
                                      IncFgts?.Codigo ?? "00", Variavel, sistema, Ativa);
            var r = await app.Folha().Cadastros.GravarRubricaAsync(empresa, rubrica, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.R.Id == r.Valor);
            Mensagem = inclusao ? "Rubrica incluída." : "Rubrica alterada.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.Folha().Cadastros.ExcluirRubricaAsync(empresa, Selecionado.R.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Rubrica excluída.";
        }, Permissao.FolhaCadastrosManter);
        return ok;
    }
}

// ---------------------------------------------------------------------------------------------------- parâmetros e tabelas legais

public sealed record TabelaLegalLinha(string Tabela, string Vigencia, string Fundamento, string Resumo);

/// <summary>Folha › Parâmetros: RAT, FAP, terceiros, anexo do Simples Nacional, responsável pelo eSocial; consulta das tabelas legais vigentes.</summary>
public partial class ParametrosFolhaViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<Opcao> Anexos { get; } = [new("", "— (não é Simples Nacional)"), new("I", "Anexo I"), new("II", "Anexo II"), new("III", "Anexo III"),
                                                          new("IV", "Anexo IV (paga CPP fora do DAS)"), new("V", "Anexo V")];
    public static IReadOnlyList<Opcao> GrausRat { get; } = [new("1", "1% – risco leve"), new("2", "2% – risco médio"), new("3", "3% – risco grave")];

    public ObservableCollection<TabelaLegalLinha> Tabelas { get; } = [];

    [ObservableProperty] private Opcao? _rat = GrausRat[1];
    [ObservableProperty] private string _fap = "1,0000";
    [ObservableProperty] private string _terceiros = "5,80";
    [ObservableProperty] private string _fpas = "515";
    [ObservableProperty] private string _codTerceiros = "0079";
    [ObservableProperty] private Opcao? _anexo = Anexos[0];
    [ObservableProperty] private string _classTrib = "99";
    [ObservableProperty] private string _responsavelNome = "";
    [ObservableProperty] private string _responsavelCpf = "";
    [ObservableProperty] private string _responsavelEmail = "";
    [ObservableProperty] private string _responsavelTelefone = "";

    public bool PodeEditar => Contexto.Pode(Permissao.FolhaParametrosManter);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var p = await app.Folha().Cadastros.ObterParametrosAsync(empresa, sessao.Id);
        var legais = await app.Folha().Cadastros.ListarTabelasLegaisAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Rat = GrausRat.FirstOrDefault(o => o.Codigo == ((int)p.RatAliquota).ToString()) ?? GrausRat[1];
        Fap = p.Fap.ToString("0.0000", Formato.PtBr);
        Terceiros = p.TerceirosAliquota.ToString("0.00", Formato.PtBr);
        (Fpas, CodTerceiros, ClassTrib) = (p.Fpas, p.CodTerceiros, p.ClassTrib);
        Anexo = Anexos.FirstOrDefault(o => o.Codigo == (p.SimplesAnexo ?? "")) ?? Anexos[0];
        (ResponsavelNome, ResponsavelCpf, ResponsavelEmail, ResponsavelTelefone) = (p.ResponsavelNome ?? "", p.ResponsavelCpf ?? "", p.ResponsavelEmail ?? "", p.ResponsavelTelefone ?? "");
        Tabelas.Clear();
        foreach (var t in legais.OrderBy(t => t.Chave).ThenByDescending(t => t.VigenciaInicio))
            Tabelas.Add(new TabelaLegalLinha(Nome(t.Chave), $"{t.VigenciaInicio:dd/MM/yyyy} a {(t.VigenciaFim is { } f ? f.ToString("dd/MM/yyyy") : "vigente")}", t.Fundamento, Resumo(t)));
    }

    private static string Nome(string chave) => chave switch
    {
        Regras.TabelasLegais.ChaveInss => "INSS do empregado",
        Regras.TabelasLegais.ChaveIrrf => "IRRF mensal",
        Regras.TabelasLegais.ChaveSalarioFamilia => "Salário-família",
        Regras.TabelasLegais.ChaveSalarioMinimo => "Salário mínimo",
        Regras.TabelasLegais.ChaveFgts => "FGTS",
        _ => chave,
    };

    private static string Resumo(Regras.ParametroLegal t) => t.Valor.Length > 140 ? t.Valor[..140] + "…" : t.Valor;

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var fap = Numeros.LerValor(Fap);
        var terceiros = Numeros.LerValor(Terceiros);
        if (fap is null || terceiros is null) { Erros.Add("Informe o FAP e a alíquota de terceiros em números."); return; }
        var p = new ParametroEmpresa(decimal.Parse(Rat?.Codigo ?? "2"), fap.Value, terceiros.Value, Fpas.Trim(), CodTerceiros.Trim(),
                                     string.IsNullOrEmpty(Anexo?.Codigo) ? null : Anexo.Codigo, ClassTrib.Trim(), ResponsavelNome, ResponsavelCpf, ResponsavelEmail, ResponsavelTelefone);
        var r = await app.Folha().Cadastros.GravarParametrosAsync(empresa, p, usuario);
        MostrarResultado(r, "Parâmetros gravados.");
    }, Permissao.FolhaParametrosManter);
}
