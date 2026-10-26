using System.Diagnostics;
using Avalonia.Media.Imaging;
using Vorticis.Application.Relatorios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Modelos de plano de contas embutidos (cadastro da empresa e tela do plano de contas).</summary>
public static class CatalogoModelosPlano
{
    private static readonly PlanoModeloEmbutido Fonte = new();
    public static IReadOnlyList<ModeloPlano> Todos { get; } = Fonte.Listar();
    public static ModeloPlano? Padrao { get; } = Todos.FirstOrDefault(m => m.Id == Fonte.ModeloPadrao);
}

public sealed record OpcaoApuracaoIrpj(string Valor, string Nome)
{
    public static readonly IReadOnlyList<OpcaoApuracaoIrpj> Todas =
    [
        new(ConfiguracaoContabil.Trimestral, "Trimestral"),
        new(ConfiguracaoContabil.AnualEstimativa, "Anual, com estimativa mensal"),
    ];
}

/// <summary>
/// Cadastros › Empresas: incluir (com pré-preenchimento pela base pública do CNPJ), alterar e excluir;
/// inscrição estadual consultada nos cadastros estaduais com confirmação antes de gravar.
/// </summary>
public partial class CadastroEmpresaViewModel(MainViewModelBase main) : CadastroViewModel(main)
{
    private DadosCnpj? _consultado;

    public static IReadOnlyList<RegimeTributario> Regimes { get; } = Enum.GetValues<RegimeTributario>();

    public MainViewModelBase Main => main;

    /// <summary>Abas: 0 = dados cadastrais, 1 = sócios, 2 = contadores (da empresa selecionada), 3 = contabilidade, 4 = síndico (só no Condomínio).</summary>
    [ObservableProperty] private int _abaSelecionada;

    public const int AbaSocios = 1;
    public const int AbaContadores = 2;
    public const int AbaContabilidade = 3;
    public const int AbaSindico = 4;

    /// <summary>A aba Síndico só aparece no módulo Condomínio.</summary>
    public bool MostraAbaSindico => main.Modulo.UsaSindico;

    /// <summary>A aba Contabilidade vale para a empresa selecionada e também na inclusão (modelo de plano e demais opções da nova empresa).</summary>
    public bool PodeAbaContabilidade => TemEmpresaSelecionada || EmInclusao;

    public bool TemEmpresaSelecionada => main.EmpresaSelecionada is not null;

    public string EmpresaSelecionadaRotulo => main.EmpresaSelecionada is { } e ? $"Empresa: {e.RazaoSocial}" : "";

    partial void OnAbaSelecionadaChanged(int value) => main.CarregarAbaEmpresa();

    public void AoTrocarEmpresa()
    {
        OnPropertyChanged(nameof(TemEmpresaSelecionada));
        OnPropertyChanged(nameof(EmpresaSelecionadaRotulo));
        OnPropertyChanged(nameof(PodeAbaContabilidade));
        if (!TemEmpresaSelecionada && !EmInclusao) AbaSelecionada = 0;
        if (EmConsulta) MostrarSelecionado();
    }

    private EmpresaDetalhe? _carregada;

    [ObservableProperty] private string _cnpj = "";
    [ObservableProperty] private string _razaoSocial = "";
    [ObservableProperty] private string _nomeFantasia = "";
    [ObservableProperty] private string _naturezaJuridica = "2062";
    [ObservableProperty] private string _cnae = "";
    [ObservableProperty] private string _inscricaoEstadual = "";
    /// <summary>NIRE (Junta Comercial), 11 dígitos; exigido na ECD e na ECF.</summary>
    [ObservableProperty] private string _nire = "";
    [ObservableProperty] private string _uf = "SP";
    [ObservableProperty] private string _municipioIbge = "";
    [ObservableProperty] private string _municipio = "";
    [ObservableProperty] private string _cep = "";
    [ObservableProperty] private string _logradouro = "";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _complemento = "";
    [ObservableProperty] private string _bairro = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private DateTime? _inicioAtividades = new DateTime(DateTime.Today.Year, 1, 1);
    // Módulos em que a empresa está ativa (o do módulo em uso fica sempre marcado).
    [ObservableProperty] private bool _moduloContabil = true;
    [ObservableProperty] private bool _moduloFiscal = true;
    [ObservableProperty] private bool _moduloFolha = true;
    [ObservableProperty] private bool _moduloEscritorio = true;
    [ObservableProperty] private bool _moduloErp = true;
    [ObservableProperty] private bool _moduloCondominio;
    [ObservableProperty] private bool _moduloFrota;

    public bool ContabilEditavel => main.ModuloAtual != ModulosEmpresa.Contabil;
    public bool FiscalEditavel => main.ModuloAtual != ModulosEmpresa.Fiscal;
    public bool FolhaEditavel => main.ModuloAtual != ModulosEmpresa.Folha;
    public bool EscritorioEditavel => main.ModuloAtual != ModulosEmpresa.Escritorio;
    public bool ErpEditavel => main.ModuloAtual != ModulosEmpresa.Erp;
    public bool CondominioEditavel => main.ModuloAtual != ModulosEmpresa.Condominio;
    public bool FrotaEditavel => main.ModuloAtual != ModulosEmpresa.Frota;

    public ModulosEmpresa Modulos
    {
        get => (ModuloContabil ? ModulosEmpresa.Contabil : 0) | (ModuloFiscal ? ModulosEmpresa.Fiscal : 0) | (ModuloFolha ? ModulosEmpresa.Folha : 0)
               | (ModuloEscritorio ? ModulosEmpresa.Escritorio : 0) | (ModuloErp ? ModulosEmpresa.Erp : 0) | (ModuloCondominio ? ModulosEmpresa.Condominio : 0)
               | (ModuloFrota ? ModulosEmpresa.Frota : 0);
        set
        {
            (ModuloContabil, ModuloFiscal, ModuloFolha) = (value.HasFlag(ModulosEmpresa.Contabil), value.HasFlag(ModulosEmpresa.Fiscal), value.HasFlag(ModulosEmpresa.Folha));
            (ModuloEscritorio, ModuloErp, ModuloCondominio) = (value.HasFlag(ModulosEmpresa.Escritorio), value.HasFlag(ModulosEmpresa.Erp), value.HasFlag(ModulosEmpresa.Condominio));
            ModuloFrota = value.HasFlag(ModulosEmpresa.Frota);
        }
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EhLucroReal), nameof(PermiteRegimeCaixa))]
    private RegimeTributario _regime = RegimeTributario.SimplesNacional;
    [ObservableProperty] private decimal? _anoExercicio = DateTime.Today.Year;
    [ObservableProperty] private bool _importarPlanoModelo = true;
    /// <summary>Plano de contas com participantes: clientes e fornecedores alocados dentro das contas contábeis.</summary>
    [ObservableProperty] private bool _usaParticipantes;
    [ObservableProperty] private string? _origemDados;
    [ObservableProperty] private string? _aviso;

    // ------------------------------------------------------------------ aba Contabilidade

    public static IReadOnlyList<ModeloPlano> ModelosPlano => CatalogoModelosPlano.Todos;
    public static IReadOnlyList<string> Mascaras => Domain.Contabil.MascaraPlano.Sugestoes;
    public static IReadOnlyList<OpcaoApuracaoIrpj> FormasApuracao => OpcaoApuracaoIrpj.Todas;

    /// <summary>Modelo do plano de contas: na inclusão é o que será criado; em empresa existente, escolher outro substitui o plano (se ainda sem lançamentos).</summary>
    [ObservableProperty] private ModeloPlano? _modeloSelecionado = CatalogoModelosPlano.Padrao;
    [ObservableProperty] private string _planoAtual = "";
    [ObservableProperty] private string _mascara = "";
    [ObservableProperty] private string? _mascaraModelo;
    [ObservableProperty] private DateTime? _inicioEscrituracao;
    [ObservableProperty] private bool _usaCentroCusto;
    [ObservableProperty] private bool _grandePorte;
    [ObservableProperty] private string _contaApuracao = "";
    [ObservableProperty] private string _contaLucros = "";
    [ObservableProperty] private string _contaPrejuizos = "";
    [ObservableProperty] private OpcaoApuracaoIrpj? _apuracaoIrpj;
    [ObservableProperty] private bool _regimeCaixa;
    /// <summary>Máscara do plano de centros de custo (vazia = códigos livres); vale quando a empresa usa centros de custo.</summary>
    [ObservableProperty] private string _mascaraCentroCusto = "";
    [ObservableProperty] private string? _mascaraCentroCustoModelo;
    /// <summary>Ao trocar a máscara de uma empresa com centros cadastrados, converte os códigos existentes (1.1 → 1.01).</summary>
    [ObservableProperty] private bool _converterCentros;
    /// <summary>Exige o nº do documento em todas as partidas dos lançamentos (ver ValidadorLancamento).</summary>
    [ObservableProperty] private bool _exigeDocumento;
    [ObservableProperty] private bool _usaEstorno = true;
    /// <summary>Lançamentos até esta data (inclusive) são recusados; vazio = sem bloqueio.</summary>
    [ObservableProperty] private DateTime? _dataBloqueio;

    public static IReadOnlyList<string> MascarasCentroCusto { get; } = ["9.99.999", "9.99.99.999", "9.9.99.999", "9.99.9999"];

    partial void OnMascaraCentroCustoModeloChanged(string? value)
    {
        if (value is not null) MascaraCentroCusto = value;
    }

    private ConfiguracaoContabil _configuracao = new();

    /// <summary>Apuração trimestral/anual do IRPJ só se aplica ao Lucro Real.</summary>
    public bool EhLucroReal => Regime == RegimeTributario.LucroReal;

    /// <summary>Regime de caixa: opção do Lucro Presumido e do Simples Nacional.</summary>
    public bool PermiteRegimeCaixa => Regime is RegimeTributario.LucroPresumido or RegimeTributario.SimplesNacional;

    partial void OnMascaraModeloChanged(string? value)
    {
        if (value is not null) Mascara = value;
    }

    /// <summary>Na inclusão, as contas de encerramento acompanham o modelo escolhido (podem ser trocadas).</summary>
    partial void OnModeloSelecionadoChanged(ModeloPlano? value)
    {
        if (EmInclusao && value is not null)
            (ContaApuracao, ContaLucros, ContaPrejuizos) = (value.Contas.Apuracao, value.Contas.LucrosAcumulados, value.Contas.PrejuizosAcumulados);
    }

    private ConfiguracaoContabil ConfiguracaoDoFormulario() => new(
        ModeloSelecionado?.Id, Mascara, InicioEscrituracao is { } d ? DateOnly.FromDateTime(d) : null, UsaCentroCusto, GrandePorte,
        ContaApuracao, ContaLucros, ContaPrejuizos, EhLucroReal ? ApuracaoIrpj?.Valor : null, PermiteRegimeCaixa && RegimeCaixa,
        UsaCentroCusto ? MascaraCentroCusto : _configuracao.MascaraCentroCusto, ExigeDocumento,
        DataBloqueio is { } b ? DateOnly.FromDateTime(b) : null, UsaEstorno, _configuracao.UsaGerencial);

    private void MostrarConfiguracao(ConfiguracaoContabil c)
    {
        _configuracao = c;
        ModeloSelecionado = CatalogoModelosPlano.Todos.FirstOrDefault(m => m.Id == c.ModeloPlano);
        PlanoAtual = ModeloSelecionado is { } m ? $"Plano de contas aplicado: {m.Nome}." : "Plano próprio (importado ou personalizado) ou ainda não criado.";
        (Mascara, InicioEscrituracao, UsaCentroCusto, GrandePorte) = (c.MascaraPlano ?? "", c.InicioEscrituracao?.ToDateTime(TimeOnly.MinValue), c.UsaCentroCusto, c.GrandePorte);
        (ContaApuracao, ContaLucros, ContaPrejuizos) = (c.ContaApuracao ?? "", c.ContaLucros ?? "", c.ContaPrejuizos ?? "");
        (ApuracaoIrpj, RegimeCaixa) = (OpcaoApuracaoIrpj.Todas.FirstOrDefault(o => o.Valor == c.ApuracaoIrpj), c.RegimeCaixa);
        (MascaraCentroCusto, ConverterCentros, ExigeDocumento) = (c.MascaraCentroCusto ?? "", false, c.ExigeDocumento);
        DataBloqueio = c.DataBloqueio?.ToDateTime(TimeOnly.MinValue);
        UsaEstorno = c.UsaEstorno;
    }

    public string TituloFormulario => Modo switch
    {
        ModoCadastro.Inclusao => "Nova empresa",
        ModoCadastro.Alteracao => "Alterando a empresa",
        _ => _carregada is null ? "Empresa" : "Dados da empresa",
    };

    protected override string NomeRegistro => "empresa";

    /// <summary>Código sequencial da empresa (atribuído na inclusão).</summary>
    public string NumeroRegistro => EmInclusao ? "Cód. (automático)" : _carregada is { } e ? $"Cód. {e.Id:0000}" : "";
    protected override string? DescricaoSelecionado => _carregada is null ? null : $"{_carregada.RazaoSocial} ({Domain.Cadastros.Cnpj.Formatar(_carregada.Cnpj)})";
    public override bool PodeManter => main.Pode(Permissao.AdministrarSistema);

    protected override string DetalheExclusao =>
        "\n\nATENÇÃO: a empresa e TODOS os seus dados serão apagados do banco de dados — lançamentos, plano de contas, " +
        "períodos, sócios e acessos. Não há como desfazer. Antes de excluir, o sistema gera automaticamente um backup completo " +
        "(a legislação exige a guarda da escrituração; guarde esse arquivo).";

    protected override void AoMudarModo()
    {
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(NumeroRegistro));
        OnPropertyChanged(nameof(PodeAbaContabilidade));
        if (!PodeAbaContabilidade && AbaSelecionada == AbaContabilidade) AbaSelecionada = 0;
    }

    // ------------------------------------------------------------------ incluir / alterar / excluir

    protected override void PrepararInclusao()
    {
        LimparFormulario();
        (NaturezaJuridica, Uf, Regime, ImportarPlanoModelo, UsaParticipantes) = ("2062", "SP", RegimeTributario.SimplesNacional, true, false);
        (InicioAtividades, AnoExercicio) = (new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today.Year);
        // Condomínio é outro tipo de cadastro: só vem marcado quando a empresa é incluída pelo próprio módulo Condomínio.
        Modulos = (ModulosEmpresa.Todos & ~(ModulosEmpresa.Condominio | ModulosEmpresa.Frota)) | main.Modulo.ModulosAoCadastrar;
        MostrarConfiguracao(new ConfiguracaoContabil());
        PlanoAtual = "";
        ModeloSelecionado = CatalogoModelosPlano.Padrao;
        if (ModeloSelecionado is { } padrao)
            (ContaApuracao, ContaLucros, ContaPrejuizos) = (padrao.Contas.Apuracao, padrao.Contas.LucrosAcumulados, padrao.Contas.PrejuizosAcumulados);
    }

    /// <summary>Mostra no formulário a empresa selecionada (lista à direita ou seletor do topo).</summary>
    protected override void MostrarSelecionado() => _ = CarregarSelecionadaAsync();

    public async Task CarregarSelecionadaAsync()
    {
        var geracao = NovaGeracao();
        EmpresaDetalhe? d = null;
        ConfiguracaoContabil? cfg = null;
        var usa = false;
        byte[]? logo = null;
        if (main.App is { } app && main.EmpresaSelecionada is { } sel && PodeManter)
        {
            d = await app.Empresas.ObterAsync(sel.Id);
            if (main.Sessao is { } s) logo = await app.LogoEmpresa.ObterAsync(sel.Id, s.Id);
            usa = await app.Participantes.UsaParticipantesAsync(sel.Id);
            if (main.Sessao is { } sessao) cfg = await app.PlanoContas.ObterConfiguracaoAsync(sel.Id, sessao.Id);
        }
        if (!EhAtual(geracao) || EmEdicao) return;
        UsaParticipantes = usa;
        MostrarLogo(logo);
        _carregada = d;
        if (d is null) LimparFormulario();
        else
        {
            (Cnpj, RazaoSocial, NomeFantasia, NaturezaJuridica, Cnae) = (Domain.Cadastros.Cnpj.Formatar(d.Cnpj), d.RazaoSocial, d.NomeFantasia ?? "", d.NaturezaJuridica, d.CnaePrincipal);
            (InscricaoEstadual, Nire, Uf, MunicipioIbge, Municipio) = (d.InscricaoEstadual ?? "", d.Nire ?? "", d.Uf, d.CodMunicipioIbge, d.Municipio ?? "");
            MostrarConfiguracao(cfg ?? new ConfiguracaoContabil());
            (Cep, Logradouro, Numero, Complemento, Bairro) = (Formato.Cep(d.Cep), d.Logradouro ?? "", d.Numero ?? "", d.Complemento ?? "", d.Bairro ?? "");
            (Email, Telefone) = (d.Email ?? "", d.Telefone ?? "");
            InicioAtividades = d.DataInicioAtividades?.ToDateTime(TimeOnly.MinValue);
            if (d.Regime is { } r) Regime = r;
            Modulos = d.Modulos;
            OrigemDados = d.FonteDados is null ? null : $"Dados obtidos de {d.FonteDados}. Situação cadastral: {d.SituacaoCadastral ?? "não informada"}.";
            Aviso = null;
        }
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(NumeroRegistro));
        AtualizarBarra();
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        if (main.App is not { } app) { Erros.Add("Sem conexão com o banco de dados."); return false; }
        if (!PodeManter) { Erros.Add("Somente administradores mantêm o cadastro de empresas."); return false; }
        try
        {
            Ocupado = true;
            return inclusao ? await IncluirEmpresaAsync(app) : await AlterarEmpresaAsync(app);
        }
        catch (Exception ex)
        {
            Erros.Add(ex.Message);
            return false;
        }
        finally
        {
            Ocupado = false;
        }
    }

    private ComplementoEmpresa Complementos(DadosCnpj? origem) =>
        new(Cep, Logradouro, Numero, Complemento, Bairro, Municipio, Email, Telefone, origem?.SituacaoCadastral, origem?.Fonte, origem?.ConsultadoEm);

    private async Task<bool> IncluirEmpresaAsync(Infra.VorticisApp app)
    {
        // A origem só é registrada se o CNPJ cadastrado for o mesmo que foi consultado.
        var origem = _consultado is not null && Domain.Cadastros.Cnpj.SomenteDigitos(_consultado.Cnpj) == Domain.Cadastros.Cnpj.SomenteDigitos(Cnpj)
            ? _consultado : null;
        // Confirma em quais módulos a empresa será cadastrada (o módulo atual é obrigatório; os demais vêm marcados).
        if (await main.EscolherModulosAsync(Modulos | main.ModuloAtual) is not { } modulos)
        {
            Erros.Add("Cadastro cancelado: nenhum módulo foi confirmado.");
            return false;
        }
        Modulos = modulos;
        var r = await app.Empresas.CadastrarAsync(new NovaEmpresa(
            Cnpj, RazaoSocial, string.IsNullOrWhiteSpace(NomeFantasia) ? null : NomeFantasia.Trim(),
            NaturezaJuridica.Trim(), Cnae, Uf.Trim(), MunicipioIbge.Trim(),
            DateOnly.FromDateTime(InicioAtividades ?? DateTime.Today), Regime, (int)(AnoExercicio ?? DateTime.Today.Year),
            ImportarPlanoModelo, Complementos(origem), InscricaoEstadual, Nire, ModeloSelecionado?.Id, ConfiguracaoDoFormulario(), modulos));
        MostrarResultado(r, $"Empresa cadastrada com exercício {AnoExercicio} e períodos mensais abertos.");
        if (!r.Sucesso) return false;
        if (UsaParticipantes && main.Sessao is { } s) await app.Participantes.DefinirUsaParticipantesAsync(r.Valor, true, s.Id);
        var socios = origem?.QuadroSocietario ?? [];
        if (socios.Count > 0 && main.Sessao is { } sessao
            && await app.Responsaveis.IncluirDoQsaAsync(r.Valor, socios, origem!.Fonte, sessao.Id) is { Sucesso: true, Valor: > 0 } incl)
            Mensagem += $" {incl.Valor} sócio(s) incluído(s) do quadro societário (complete CPF e representante na aba Sócios).";
        _consultado = null;
        Modo = ModoCadastro.Consulta;
        await main.RecarregarEmpresasAsync(r.Valor);
        return true;
    }

    private async Task<bool> AlterarEmpresaAsync(Infra.VorticisApp app)
    {
        if (_carregada is not { } atual) { Erros.Add("Selecione a empresa a alterar."); return false; }
        var r = await app.Empresas.AlterarAsync(new AlteracaoEmpresa(
            atual.Id, RazaoSocial, string.IsNullOrWhiteSpace(NomeFantasia) ? null : NomeFantasia.Trim(), NaturezaJuridica.Trim(), Cnae,
            Uf.Trim(), MunicipioIbge.Trim(), DateOnly.FromDateTime(InicioAtividades ?? DateTime.Today), InscricaoEstadual, Complementos(null), Nire, Modulos | main.ModuloAtual));
        MostrarResultado(r, "Alterações da empresa salvas.");
        if (!r.Sucesso) return false;
        if (main.Sessao is { } sess && !await SalvarContabilidadeAsync(app, atual.Id, sess.Id)) return false;
        if (main.Sessao is { } sessao && UsaParticipantes != await app.Participantes.UsaParticipantesAsync(atual.Id))
        {
            var p = await app.Participantes.DefinirUsaParticipantesAsync(atual.Id, UsaParticipantes, sessao.Id);
            if (!p.Sucesso) { MostrarResultado(p, ""); return false; }
        }
        Modo = ModoCadastro.Consulta;
        await main.RecarregarEmpresasAsync(atual.Id);
        await CarregarSelecionadaAsync();
        return true;
    }

    /// <summary>
    /// Aba Contabilidade de empresa existente: se outro modelo de plano foi escolhido, substitui o plano (com confirmação e só
    /// sem lançamentos); depois grava as demais configurações (mesmas conferências do serviço).
    /// </summary>
    private async Task<bool> SalvarContabilidadeAsync(Infra.VorticisApp app, long empresaId, long usuarioId)
    {
        var escolhido = ModeloSelecionado;
        string? aplicado = null;
        if (escolhido is not null && escolhido.Id != _configuracao.ModeloPlano)
        {
            if (!await Contexto.ConfirmarAsync("Trocar o plano de contas",
                    $"O plano de contas atual da empresa será substituído pelo modelo \"{escolhido.Nome}\" ({escolhido.QuantidadeContas} contas). " +
                    "Isso só é possível enquanto não houver lançamentos. Deseja continuar?", "Trocar o plano", "Manter o plano"))
                ModeloSelecionado = CatalogoModelosPlano.Todos.FirstOrDefault(m => m.Id == _configuracao.ModeloPlano);
            else
            {
                var m = await app.PlanoContas.AplicarModeloAsync(empresaId, escolhido.Id, usuarioId);
                if (!m.Sucesso) { MostrarResultado(m, ""); return false; }
                aplicado = $" Plano de contas substituído pelo modelo {escolhido.Nome} ({m.Valor} contas).";
                (ContaApuracao, ContaLucros, ContaPrejuizos) = (escolhido.Contas.Apuracao, escolhido.Contas.LucrosAcumulados, escolhido.Contas.PrejuizosAcumulados);
            }
        }
        var c = await app.PlanoContas.SalvarConfiguracaoAsync(empresaId, ConfiguracaoDoFormulario(),
                                                              DateOnly.FromDateTime(InicioAtividades ?? DateTime.Today), usuarioId,
                                                              converterCentros: ConverterCentros);
        if (!c.Sucesso) { MostrarResultado(c, ""); return false; }
        Mensagem += aplicado;
        return true;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        if (main.App is not { } app || _carregada is not { } atual || main.Sessao is not { } sessao) return false;
        // Backup de segurança antes de apagar (a escrituração tem guarda obrigatória).
        string? copia = null;
        try
        {
            Ocupado = true;
            var b = await app.Backup.GerarAsync(sessao, await app.ConfiguracoesGerais.PastaBackupAsync());
            if (b.Sucesso) copia = b.Valor;
        }
        catch (Exception)
        {
            // segue para a pergunta abaixo
        }
        finally
        {
            Ocupado = false;
        }
        if (copia is null && !await Contexto.ConfirmarAsync("Backup não gerado",
                "Não foi possível gerar o backup automático antes da exclusão. Excluir a empresa mesmo assim?", "Excluir sem backup", "Cancelar"))
            return false;

        var r = await app.Empresas.ExcluirAsync(atual.Id);
        if (!r.Sucesso) { MostrarResultado(r, ""); return false; }
        Mensagem = $"Empresa {atual.RazaoSocial} excluída do banco de dados ({r.Valor} lançamento(s) apagado(s))." +
                   (copia is null ? "" : $" Backup anterior à exclusão: {copia}");
        _carregada = null;
        await main.RecarregarEmpresasAsync();
        return true;
    }

    // ------------------------------------------------------------------ consultas externas

    /// <summary>Busca os dados do CNPJ na base pública da Receita e preenche o formulário (o usuário revisa antes de salvar).</summary>
    [RelayCommand]
    private async Task ConsultarCnpjAsync()
    {
        Erros.Clear();
        (Mensagem, Aviso, OrigemDados) = (null, null, null);
        if (main.App is not { } app) { Erros.Add("Sem conexão com o banco de dados."); return; }
        try
        {
            Ocupado = true;
            var r = await app.Empresas.ConsultarCnpjAsync(Cnpj);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            Preencher(r.Valor);
        }
        finally
        {
            Ocupado = false;
        }
        // Em seguida, a inscrição estadual na UF obtida (o usuário decide se usa).
        await BuscarInscricaoEstadualAsync();
    }

    private void Preencher(DadosCnpj d)
    {
        _consultado = d;
        RazaoSocial = d.RazaoSocial;
        NomeFantasia = d.NomeFantasia ?? "";
        NaturezaJuridica = d.NaturezaJuridica ?? NaturezaJuridica;
        Cnae = d.CnaePrincipal ?? "";
        Uf = d.Uf ?? Domain.Cadastros.Uf.DeCodigoIbge(d.CodMunicipioIbge) ?? Uf;
        MunicipioIbge = d.CodMunicipioIbge ?? "";
        Municipio = d.Municipio ?? "";
        Cep = Formato.Cep(d.Cep);
        Logradouro = d.Logradouro ?? "";
        Numero = d.Numero ?? "";
        Complemento = d.Complemento ?? "";
        Bairro = d.Bairro ?? "";
        Email = d.Email ?? "";
        Telefone = d.Telefone ?? "";
        if (d.DataInicioAtividades is { } ini)
        {
            InicioAtividades = ini.ToDateTime(TimeOnly.MinValue);
            if (AnoExercicio < ini.Year) AnoExercicio = ini.Year;
        }
        if (d.RegimeSugerido is { } regime) Regime = regime;

        OrigemDados = $"Dados de {d.Fonte} em {d.ConsultadoEm.ToLocalTime():dd/MM/yyyy HH:mm}. Situação cadastral: {d.SituacaoCadastral ?? "não informada"}." +
                      (d.RegimeSugerido is null ? " Regime não consta da base pública (Presumido/Real): confirme abaixo." : "");
        if (!d.Ativa) Aviso = $"Atenção: a situação cadastral do CNPJ é {d.SituacaoCadastral ?? "desconhecida"}.";
        Mensagem = "Dados preenchidos. Revise e clique em Salvar.";
    }

    /// <summary>
    /// Inscrição estadual na UF da empresa (UF do cadastro ou do código IBGE do município), nos cadastros estaduais.
    /// Sempre pergunta antes: em edição, preenche o campo (grava ao Salvar); em consulta, grava na empresa.
    /// </summary>
    [RelayCommand]
    private async Task BuscarInscricaoEstadualAsync()
    {
        Erros.Clear();
        if (main.App is not { } app) { Erros.Add("Sem conexão com o banco de dados."); return; }
        if (!EmEdicao && (_carregada is null || !PodeManter)) { Erros.Add("Selecione uma empresa (ou clique em Incluir)."); return; }
        var uf = Domain.Cadastros.Uf.Valida(Uf) ? Uf.Trim().ToUpperInvariant() : Domain.Cadastros.Uf.DeCodigoIbge(MunicipioIbge);
        Resultado<IReadOnlyList<InscricaoEstadualEncontrada>> r;
        try
        {
            Ocupado = true;
            r = await app.Empresas.ConsultarInscricaoEstadualAsync(Cnpj, Uf, MunicipioIbge);
        }
        finally
        {
            Ocupado = false;
        }
        if (!r.Sucesso) { foreach (var e in r.Erros) Erros.Add(e); return; }
        if (r.Valor.Count == 0)
        {
            // Sem inscrição na UF: a empresa é tratada como não contribuinte do ICMS (ISENTO), sem digitação manual.
            if (!string.IsNullOrWhiteSpace(InscricaoEstadual) && InscricaoEstadual != "ISENTO")
            {
                Aviso = $"Nenhuma inscrição estadual encontrada em {uf} para este CNPJ; o número já informado ({InscricaoEstadual}) foi mantido. " +
                        "Confira no SINTEGRA do estado.";
                return;
            }
            InscricaoEstadual = "ISENTO";
            if (EmEdicao)
            {
                Mensagem = $"Nenhuma inscrição estadual encontrada em {uf}: preenchido ISENTO (gravado ao Salvar).";
                return;
            }
            var isento = await app.Empresas.SalvarInscricaoEstadualAsync(_carregada!.Id, "ISENTO");
            if (!isento.Sucesso) { MostrarResultado(isento, ""); return; }
            Mensagem = $"Nenhuma inscrição estadual encontrada em {uf}: a empresa foi registrada como ISENTO.";
            return;
        }

        var ie = r.Valor[0];
        var outras = r.Valor.Count > 1 ? $"\nOutras inscrições em {uf}: {string.Join(", ", r.Valor.Skip(1).Select(i => i.Inscricao))}." : "";
        var atual = string.IsNullOrWhiteSpace(InscricaoEstadual) ? "" : $"\nNo cadastro hoje: {InscricaoEstadual}.";
        var gravarAgora = !EmEdicao;
        var ok = await Contexto.ConfirmarAsync("Inscrição estadual encontrada",
            $"{uf}: {ie.Inscricao} ({(ie.Ativa ? "ativa" : "INATIVA")}){outras}{atual}\n" +
            $"Fonte: {ie.Fonte}, consultada em {ie.ConsultadoEm.ToLocalTime():dd/MM/yyyy HH:mm}.\n\n" +
            (gravarAgora ? "Deseja salvar este número no cadastro da empresa?" : "Deseja usar este número? Ele será gravado quando você clicar em Salvar."),
            gravarAgora ? "Salvar" : "Usar", "Não salvar");
        if (!ok) { Mensagem = "Inscrição estadual não alterada."; return; }

        if (!gravarAgora)
        {
            InscricaoEstadual = ie.Inscricao;
            Mensagem = "Inscrição estadual preenchida. Clique em Salvar para gravar.";
            return;
        }
        var salvo = await app.Empresas.SalvarInscricaoEstadualAsync(_carregada!.Id, ie.Inscricao);
        if (!salvo.Sucesso) { MostrarResultado(salvo, ""); return; }
        InscricaoEstadual = ie.Inscricao;
        Mensagem = $"Inscrição estadual {ie.Inscricao} salva no cadastro da empresa.";
    }

    /// <summary>Abre o portal nacional do SINTEGRA (links para a consulta de cada estado) para conferência manual.</summary>
    [RelayCommand]
    private void AbrirSintegra()
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://www.sintegra.gov.br/") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Erros.Add($"Não foi possível abrir o navegador: {ex.Message}");
        }
    }

    // ---- Logo da empresa (impresso no cabeçalho de todos os relatórios em PDF) ----

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemLogo))] private Bitmap? _logo;
    public bool TemLogo => Logo is not null;

    private void MostrarLogo(byte[]? bytes)
    {
        Bitmap? b = null;
        if (LogoRelatorios.EhImagem(bytes))
            try { using var m = new MemoryStream(bytes!); b = new Bitmap(m); }
            catch (Exception) { b = null; }   // arquivo corrompido: fica sem prévia
        Logo = b;
    }

    /// <summary>Envia o logo escolhido no computador (PNG ou JPG, até 1 MB) e já passa a usá-lo nos relatórios.</summary>
    public async Task EnviarLogoAsync(string caminho)
    {
        Erros.Clear();
        Mensagem = null;
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(caminho); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Erros.Add($"Não foi possível ler o arquivo: {ex.Message}"); return; }
        await GravarLogoAsync(bytes, "Logo gravado: ele sai no cabeçalho de todos os relatórios em PDF desta empresa.");
    }

    [RelayCommand]
    private async Task RemoverLogoAsync()
    {
        Erros.Clear();
        if (!await main.ConfirmarAsync("Remover logo", "Remover o logo da empresa? Os relatórios voltam a sair sem logo.", "Remover", "Cancelar")) return;
        await GravarLogoAsync(null, "Logo removido.");
    }

    private async Task GravarLogoAsync(byte[]? bytes, string sucesso)
    {
        if (main.App is not { } app || main.Sessao is not { } s || main.EmpresaSelecionada is not { } e) { Erros.Add("Selecione a empresa."); return; }
        var r = await app.LogoEmpresa.GravarAsync(e.Id, bytes, s.Id);
        if (!r.Sucesso) { foreach (var x in r.Erros) Erros.Add(x); return; }
        MostrarLogo(bytes);
        LogoRelatorios.Atual = bytes;
        Mensagem = sucesso;
    }

    private void LimparFormulario()
    {
        (Cnpj, RazaoSocial, NomeFantasia, Cnae, MunicipioIbge, Municipio, InscricaoEstadual, Nire) = ("", "", "", "", "", "", "", "");
        (Cep, Logradouro, Numero, Complemento, Bairro, Email, Telefone) = ("", "", "", "", "", "", "");
        (OrigemDados, Aviso, _consultado) = (null, null, null);
    }
}
