using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record VinculoContabilLinha(VinculoContabil Vinculo)
{
    public string Numero => Vinculo.Id.ToString("0000");
    public string Tipo => Vinculo.Tipo;
    public string Nome => Vinculo.Nome;
    public string Documento => Vinculo.ContadorId is not null ? Cpf.Formatar(Vinculo.Documento) : ServicoLivros.FormatarCnpj(Vinculo.Documento);
    public string Crc => Vinculo.Crc ?? "";
    public string Inicio => Vinculo.Inicio.ToString("dd/MM/yyyy");
    public string Fim => Vinculo.Fim?.ToString("dd/MM/yyyy") ?? "vigente";
    public string Responsavel => Vinculo.ResponsavelTecnico ? "✓ responsável técnico" : "";
    public string AssinaEcdEcf => Vinculo.AssinaEcdEcf ? "✓ assina ECD/ECF" : "";
    public string Procuracao => Vinculo.Procuracao is { Procurador: true } p
        ? $"✓ procurador ({string.Join(", ", p.Abrangencia.Select(AbrangenciaProcuracao.Descricao))}) {p.Inicio:dd/MM/yyyy} a {(p.Fim is { } f ? f.ToString("dd/MM/yyyy") : "sem fim")}"
        : "";
}

/// <summary>Cadastros › Contadores (contador responsável e escritório contábil) da empresa selecionada; também é a aba Contadores de Cadastros › Empresas.</summary>
public partial class ContabilidadeEmpresaViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    /// <summary>Na inclusão: vincular um contador (true) ou um escritório contábil (false).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IncluindoContador), nameof(IncluindoEscritorio))] private bool _incluirContador = true;

    public bool IncluindoContador => EmInclusao && IncluirContador;
    public bool IncluindoEscritorio => EmInclusao && !IncluirContador;

    // alteração do vínculo selecionado
    [ObservableProperty] private bool _responsavelEdicao;
    [ObservableProperty] private bool _assinaEdicao;
    [ObservableProperty] private DateTime? _fimEdicao;

    /// <summary>Procuração eletrônica na inclusão de um contador e na alteração do vínculo selecionado.</summary>
    public ProcuracaoEditor ProcuracaoNova { get; } = new();
    public ProcuracaoEditor ProcuracaoAlterar { get; } = new();

    public bool SelecionadoEhContador => Selecionado?.Vinculo.ContadorId is not null;

    protected override string NomeRegistro => "vínculo";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Tipo.ToLowerInvariant()} {s.Nome}" : null;
    public override bool PodeManter => PodeEditar;
    protected override string DetalheExclusao =>
        "\n\nUse Excluir só para cadastro errado. Ao trocar de contador ou escritório, prefira Alterar e informar o fim do vínculo (mantém o histórico).";

    protected override void AoMudarModo()
    {
        OnPropertyChanged(nameof(IncluindoContador));
        OnPropertyChanged(nameof(IncluindoEscritorio));
    }

    protected override void PrepararInclusao()
    {
        (ContadorNome, ContadorCpf, ContadorCrc, ContadorEmail, ContadorTelefone, ContadorExistente) = ("", "", "", "", "", null);
        (EscritorioCnpj, EscritorioRazao, EscritorioCrc, EscritorioEmail, EscritorioTelefone, EscritorioExistente) = ("", "", "", "", "", null);
        (ContadorInicio, EscritorioInicio, ContadorResponsavel) = (new DateTime(DateTime.Today.Year, 1, 1), new DateTime(DateTime.Today.Year, 1, 1), true);
        ContadorAssinaEcdEcf = true;
        ProcuracaoNova.Limpar();
    }

    protected override void MostrarSelecionado()
    {
        ResponsavelEdicao = Selecionado?.Vinculo.ResponsavelTecnico ?? false;
        AssinaEdicao = Selecionado?.Vinculo.AssinaEcdEcf ?? false;
        FimEdicao = Selecionado?.Vinculo.Fim?.ToDateTime(TimeOnly.MinValue);
        ProcuracaoAlterar.Carregar(Selecionado?.Vinculo.Procuracao);
    }

    partial void OnSelecionadoChanged(VinculoContabilLinha? value)
    {
        MostrarSelecionado();
        OnPropertyChanged(nameof(SelecionadoEhContador));
        AtualizarBarra();
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        if (inclusao) return IncluirContador ? await VincularContadorAsync() : await VincularEscritorioAsync();
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is not { } linha) { Erros.Add("Selecione um vínculo na lista."); return; }
            var v = linha.Vinculo;
            if (ResponsavelEdicao && !v.ResponsavelTecnico)
            {
                if (v.ContadorId is null) { Erros.Add("Só um contador pode ser o responsável técnico."); return; }
                var r = await app.Responsaveis.DefinirResponsavelAsync(empresa, v.Id, usuario);
                if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            }
            if (AssinaEdicao != v.AssinaEcdEcf)
            {
                var r = await app.Responsaveis.DefinirAssinaEcdEcfAsync(empresa, v.Id, AssinaEdicao, usuario);
                if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            }
            if (v.ContadorId is not null && ProcuracaoAlterar.ParaProcuracao() is var nova && !SemMudanca(v.Procuracao, nova))
            {
                var r = await app.Responsaveis.DefinirProcuracaoAsync(empresa, v.Id, nova, usuario);
                if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            }
            if (FimEdicao is { } fim && v.Fim != DateOnly.FromDateTime(fim))
            {
                var r = await app.Responsaveis.EncerrarVinculoAsync(empresa, v.Id, DateOnly.FromDateTime(fim), usuario);
                if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Mensagem = "Vínculo alterado.";
        }, Permissao.EmpresaResponsaveisManter);
        return ok;
    }

    private static bool SemMudanca(Procuracao? atual, Procuracao nova)
        => atual is { Procurador: true } a
            ? nova.Procurador && a.Inicio == nova.Inicio && a.Fim == nova.Fim && (a.Observacao ?? "") == (nova.Observacao ?? "").Trim()
              && a.Abrangencia.Order().SequenceEqual(nova.Abrangencia.Order())
            : !nova.Procurador;

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is not { } linha) { Erros.Add("Selecione um vínculo na lista."); return; }
            var r = await app.Responsaveis.ExcluirVinculoAsync(empresa, linha.Vinculo.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Vínculo excluído.";
        }, Permissao.EmpresaResponsaveisManter);
        return ok;
    }

    public ObservableCollection<VinculoContabilLinha> Vinculos { get; } = [];
    public ObservableCollection<Contador> ContadoresCadastrados { get; } = [];
    public ObservableCollection<EscritorioContabil> EscritoriosCadastrados { get; } = [];
    public static IReadOnlyList<string> Ufs { get; } = Uf.Todas.Order().ToArray();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private VinculoContabilLinha? _selecionado;

    // contador
    [ObservableProperty] private Contador? _contadorExistente;
    [ObservableProperty] private string _contadorNome = "";
    [ObservableProperty] private string _contadorCpf = "";
    [ObservableProperty] private string _contadorCrc = "";
    [ObservableProperty] private string _contadorUf = "SP";
    [ObservableProperty] private string _contadorEmail = "";
    [ObservableProperty] private string _contadorTelefone = "";
    [ObservableProperty] private DateTime? _contadorInicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private bool _contadorResponsavel = true;
    [ObservableProperty] private bool _contadorAssinaEcdEcf = true;

    // escritório
    [ObservableProperty] private EscritorioContabil? _escritorioExistente;
    [ObservableProperty] private string _escritorioCnpj = "";
    [ObservableProperty] private string _escritorioRazao = "";
    [ObservableProperty] private string _escritorioCrc = "";
    [ObservableProperty] private string _escritorioUf = "SP";
    [ObservableProperty] private string _escritorioEmail = "";
    [ObservableProperty] private string _escritorioTelefone = "";
    [ObservableProperty] private DateTime? _escritorioInicio = new DateTime(DateTime.Today.Year, 1, 1);

    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.EmpresaResponsaveisManter);

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        AtualizarPermissoes();
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var vinculos = await app.Responsaveis.ListarVinculosAsync(empresa, sessao.Id);
        var contadores = await app.Responsaveis.ListarContadoresAsync();
        var escritorios = await app.Responsaveis.ListarEscritoriosAsync();
        if (!EhAtual(g)) return;
        if (!vinculos.Sucesso) { MostrarResultado(vinculos, ""); return; }
        var id = Selecionado?.Vinculo.Id;
        Vinculos.Clear();
        foreach (var v in vinculos.Valor) Vinculos.Add(new VinculoContabilLinha(v));
        Selecionado = Vinculos.FirstOrDefault(v => v.Vinculo.Id == id);
        ContadoresCadastrados.Clear();
        foreach (var c in contadores) ContadoresCadastrados.Add(c);
        EscritoriosCadastrados.Clear();
        foreach (var e in escritorios) EscritoriosCadastrados.Add(e);
    }

    partial void OnContadorExistenteChanged(Contador? value)
    {
        if (value is null) return;
        (ContadorNome, ContadorCpf, ContadorCrc, ContadorUf, ContadorEmail, ContadorTelefone) =
            (value.Nome, Cpf.Formatar(value.Cpf), value.Crc, value.UfCrc, value.Email ?? "", value.Telefone ?? "");
        ContadorAssinaEcdEcf = value.AssinaEcdEcf;
    }

    partial void OnEscritorioExistenteChanged(EscritorioContabil? value)
    {
        if (value is null) return;
        (EscritorioCnpj, EscritorioRazao, EscritorioCrc, EscritorioUf, EscritorioEmail, EscritorioTelefone) =
            (ServicoLivros.FormatarCnpj(value.Cnpj), value.RazaoSocial, value.Crc ?? "", value.UfCrc ?? "SP", value.Email ?? "", value.Telefone ?? "");
    }

    private async Task<bool> VincularContadorAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Responsaveis.VincularContadorAsync(empresa,
            new Contador(0, ContadorNome, ContadorCpf, ContadorCrc, ContadorUf, ContadorEmail, ContadorTelefone, null, ContadorAssinaEcdEcf),
            DateOnly.FromDateTime(ContadorInicio ?? DateTime.Today), ContadorResponsavel, usuario, procuracao: ProcuracaoNova.ParaProcuracao());
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        ok = true;
        Modo = ModoCadastro.Consulta;
        (ContadorNome, ContadorCpf, ContadorCrc, ContadorEmail, ContadorTelefone, ContadorExistente) = ("", "", "", "", "", null);
        await CarregarAsync();
        Mensagem = "Contador vinculado à empresa.";
    }, Permissao.EmpresaResponsaveisManter);
        return ok;
    }

    [RelayCommand]
    private async Task BuscarEscritorioAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app) return;
        var r = await app.Responsaveis.ConsultarCnpjAsync(EscritorioCnpj);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (EscritorioRazao, EscritorioEmail, EscritorioTelefone) = (r.Valor.RazaoSocial, r.Valor.Email ?? "", r.Valor.Telefone ?? "");
        EscritorioUf = r.Valor.Uf ?? EscritorioUf;
        Mensagem = $"Dados de {r.Valor.Fonte}. Informe o registro do escritório no CRC.";
    }

    private async Task<bool> VincularEscritorioAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Responsaveis.VincularEscritorioAsync(empresa,
            new EscritorioContabil(0, EscritorioCnpj, EscritorioRazao, EscritorioCrc, EscritorioUf, EscritorioEmail, EscritorioTelefone),
            DateOnly.FromDateTime(EscritorioInicio ?? DateTime.Today), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        ok = true;
        Modo = ModoCadastro.Consulta;
        (EscritorioCnpj, EscritorioRazao, EscritorioCrc, EscritorioEmail, EscritorioTelefone, EscritorioExistente) = ("", "", "", "", "", null);
        await CarregarAsync();
        Mensagem = "Escritório contábil vinculado à empresa.";
    }, Permissao.EmpresaResponsaveisManter);
        return ok;
    }
}

public sealed record SocioLinha(Socio Socio)
{
    public string Numero => Socio.Id.ToString("0000");
    public string Nome => Socio.Nome;
    public string Documento => Socio.CpfCnpj is { } d ? (d.Length == 11 ? Cpf.Formatar(d) : ServicoLivros.FormatarCnpj(d)) : Socio.DocumentoMascarado ?? "";
    public string Qualificacao => Socio.Qualificacao;
    public string Participacao => Socio.Participacao is { } p ? $"{p:0.##}%" : "";
    public string Entrada => Socio.DataEntrada?.ToString("dd/MM/yyyy") ?? "";
    public string Situacao => Socio.DataSaida is { } s ? $"saiu em {s:dd/MM/yyyy}" : "ativo";
    public string Representante => Socio.RepresentanteLegal ? "✓" : "";
}

public sealed record OpcaoTipoPessoa(char Codigo, string Nome)
{
    public static readonly IReadOnlyList<OpcaoTipoPessoa> Todas = [new('F', "Pessoa física"), new('J', "Pessoa jurídica"), new('E', "Estrangeiro")];
}

/// <summary>Cadastros › Empresas, aba Sócios: sócios da empresa selecionada (Incluir / Alterar / Excluir).</summary>
public partial class SociosViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<SocioLinha> Lista { get; } = [];
    public static IReadOnlyList<string> Qualificacoes { get; } =
        ["Sócio-Administrador", "Sócio", "Administrador", "Titular", "Diretor", "Presidente", "Procurador", "Sócio Comanditário"];

    [ObservableProperty] private SocioLinha? _selecionado;
    [ObservableProperty] private long _editandoId;
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private OpcaoTipoPessoa _tipo = OpcaoTipoPessoa.Todas[0];
    [ObservableProperty] private string _documento = "";
    [ObservableProperty] private string _documentoMascarado = "";
    [ObservableProperty] private string _qualificacao = "Sócio-Administrador";
    [ObservableProperty] private string _participacao = "";
    [ObservableProperty] private DateTime? _entrada;
    [ObservableProperty] private DateTime? _saida;
    [ObservableProperty] private bool _representante;
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _resumo = "";

    private Socio? _original;

    public bool PodeEditar => Contexto.Pode(Permissao.EmpresaResponsaveisManter);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "sócio";
    protected override string? DescricaoSelecionado => Selecionado?.Socio.Nome;
    protected override string DetalheExclusao => "\n\nPara registrar a saída de um sócio, prefira Alterar e informar a data de saída.";

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        AtualizarPermissoes();
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var r = await app.Responsaveis.ListarSociosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var id = Selecionado?.Socio.Id;
        Lista.Clear();
        foreach (var s in r.Valor) Lista.Add(new SocioLinha(s));
        Selecionado = Lista.FirstOrDefault(l => l.Socio.Id == id);
        if (Selecionado is null) { Limpar(); Modo = ModoCadastro.Consulta; }
        var ativos = r.Valor.Where(s => s.Ativo).ToArray();
        Resumo = $"{ativos.Length} sócio(s) ativo(s) · participação informada: {ativos.Sum(s => s.Participacao ?? 0):0.##}%" +
                 (ativos.Any(s => s.RepresentanteLegal) ? "" : " · sem representante legal definido");
    }

    partial void OnSelecionadoChanged(SocioLinha? value)
    {
        MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        if (Selecionado is not { } linha) { Limpar(); return; }
        var s = _original = linha.Socio;
        (EditandoId, Nome, Documento, DocumentoMascarado, Qualificacao) = (s.Id, s.Nome, s.CpfCnpj ?? "", s.DocumentoMascarado ?? "", s.Qualificacao);
        Tipo = OpcaoTipoPessoa.Todas.First(t => t.Codigo == s.TipoPessoa);
        Participacao = s.Participacao?.ToString("0.####") ?? "";
        (Entrada, Saida) = (s.DataEntrada?.ToDateTime(TimeOnly.MinValue), s.DataSaida?.ToDateTime(TimeOnly.MinValue));
        (Representante, Email, Telefone) = (s.RepresentanteLegal, s.Email ?? "", s.Telefone ?? "");
    }

    protected override void PrepararInclusao() => Limpar();

    private void Limpar()
    {
        _original = null;
        (EditandoId, Nome, Documento, DocumentoMascarado, Qualificacao, Participacao) = (0, "", "", "", "Sócio-Administrador", "");
        (Entrada, Saida, Representante, Email, Telefone) = (null, null, false, "", "");
        Tipo = OpcaoTipoPessoa.Todas[0];
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            decimal? participacao = null;
            if (Participacao.Trim().Length > 0)
            {
                if (Numeros.LerValor(Participacao.Replace("%", "")) is not { } p) { Erros.Add("Participação inválida."); return; }
                participacao = p;
            }
            var original = inclusao ? null : _original;
            var socio = new Socio(inclusao ? 0 : EditandoId, Nome, Tipo.Codigo, Documento, original?.DocumentoMascarado, Qualificacao,
                original?.QualificacaoCodigo, participacao, Entrada is { } e ? DateOnly.FromDateTime(e) : null,
                Saida is { } s ? DateOnly.FromDateTime(s) : null, Representante, Email, Telefone, original?.Origem ?? "MANUAL");
            var r = await app.Responsaveis.SalvarSocioAsync(empresa, socio, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            Selecionado = null;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.Socio.Id == r.Valor);
            Mensagem = inclusao ? "Sócio incluído." : "Sócio alterado.";
        }, Permissao.EmpresaResponsaveisManter);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is not { } linha) { Erros.Add("Selecione um sócio."); return; }
            var r = await app.Responsaveis.ExcluirSocioAsync(empresa, linha.Socio.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Sócio excluído.";
        }, Permissao.EmpresaResponsaveisManter);
        return ok;
    }

    [RelayCommand]
    private Task ImportarDaReceitaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Responsaveis.ImportarDaReceitaAsync(empresa, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = r.Valor == 0 ? "Nenhum sócio novo no quadro societário público."
            : $"{r.Valor} sócio(s) incluído(s) do quadro societário público. A base pública mascara o CPF: complete-o e indique o representante legal.";
    }, Permissao.EmpresaResponsaveisManter);
}
