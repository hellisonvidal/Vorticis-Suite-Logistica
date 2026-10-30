using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;
using Vorticis.Domain.Contabil.PlanoContas;

namespace Vorticis.Contabil.Desktop.ViewModels;

public sealed partial class PlanoContasViewModel : ObservableObject
{
    private readonly IPlanoContasServico _servico;

    public PlanoContasViewModel(IPlanoContasServico servico)
    {
        _servico = servico;
        CarregarCommand = new AsyncRelayCommand(CarregarAsync);
        NovaCommand = new AsyncRelayCommand(NovaAsync);
        EditarCommand = new AsyncRelayCommand<PlanoContaDto>(EditarAsync);
        ExcluirCommand = new AsyncRelayCommand<PlanoContaDto>(ExcluirAsync);
        ImportarReferencialCommand = new AsyncRelayCommand(ImportarReferencialAsync);
    }

    [ObservableProperty] private ObservableCollection<PlanoContaDto> _contas = [];
    [ObservableProperty] private PlanoContaDto? _selecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private string _filtro = "";

    public IAsyncRelayCommand CarregarCommand { get; }
    public IAsyncRelayCommand NovaCommand { get; }
    public IAsyncRelayCommand<PlanoContaDto> EditarCommand { get; }
    public IAsyncRelayCommand<PlanoContaDto> ExcluirCommand { get; }
    public IAsyncRelayCommand ImportarReferencialCommand { get; }

    private async Task CarregarAsync()
    {
        Carregando = true;
        try
        {
            var lista = await _servico.ListarAsync();
            Contas = new(lista.Where(c => string.IsNullOrWhiteSpace(Filtro) ||
                c.Codigo.Contains(Filtro) || c.Nome.Contains(Filtro, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Codigo));
        }
        finally { Carregando = false; }
    }

    private async Task NovaAsync()
    {
        var vm = new PlanoContasPopupViewModel(_servico, null);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }

    private async Task EditarAsync(PlanoContaDto? conta)
    {
        if (conta == null) return;
        var vm = new PlanoContasPopupViewModel(_servico, conta);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }

    private async Task ExcluirAsync(PlanoContaDto? conta)
    {
        if (conta == null) return;
        var confirm = await DialogHost.Show(
            new ConfirmacaoViewModel($"Excluir conta '{conta.Codigo} - {conta.Nome}'?", "Esta ação não pode ser desfeita."),
            "RootDialog");
        if (confirm is true)
        {
            await _servico.ExcluirAsync(conta.Id);
            await CarregarAsync();
        }
    }

    private async Task ImportarReferencialAsync()
    {
        var opcoes = new[]
        {
            "Plano de Contas Referencial - Pequena Empresa",
            "Plano de Contas Referencial - Média Empresa",
            "Plano de Contas Referencial - Grande Empresa",
            "Plano de Contas Contábil - Simples Nacional",
            "Plano de Contas Contábil - Lucro Presumido",
            "Plano de Contas Contábil - Lucro Real"
        };

        var vm = new SelecaoListaViewModel("Importar Plano de Contas Referencial", opcoes);
        var escolha = await DialogHost.Show(vm, "RootDialog");
        if (escolha is string selecionado)
        {
            await _servico.ImportarReferencialAsync(selecionado);
            await CarregarAsync();
        }
    }
}

public sealed partial class PlanoContasPopupViewModel : ObservableObject
{
    private readonly IPlanoContasServico _servico;
    private readonly PlanoContaDto? _original;

    public PlanoContasPopupViewModel(IPlanoContasServico servico, PlanoContaDto? original = null)
    {
        _servico = servico;
        _original = original;

        if (original != null)
        {
            Id = original.Id;
            Codigo = original.Codigo;
            Nome = original.Nome;
            Tipo = original.Tipo;
            Natureza = original.Natureza;
            AceitaLancamento = original.AceitaLancamento;
            ContaPaiId = original.ContaPaiId;
            ContaPaiDescricao = original.ContaPai != null ? $"{original.ContaPai.Codigo} - {original.ContaPai.Nome}" : "";
        }

        SalvarCommand = new AsyncRelayCommand(SalvarAsync, PodeSalvar);
        AbrirPaiCommand = new AsyncRelayCommand(AbrirPaiAsync);
    }

    [ObservableProperty] private Guid _id = Guid.Empty;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private TipoConta _tipo = TipoConta.Analitica;
    [ObservableProperty] private NaturezaConta _natureza = NaturezaConta.Devedora;
    [ObservableProperty] private bool _aceitaLancamento = true;
    [ObservableProperty] private Guid? _contaPaiId;
    [ObservableProperty] private string _contaPaiDescricao = "";
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand SalvarCommand { get; }
    public IAsyncRelayCommand AbrirPaiCommand { get; }

    private bool PodeSalvar() => !string.IsNullOrWhiteSpace(Codigo) && !string.IsNullOrWhiteSpace(Nome);

    private async Task SalvarAsync()
    {
        Carregando = true;
        try
        {
            var dto = new PlanoContaDto
            {
                Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
                Codigo = Codigo,
                Nome = Nome,
                Tipo = Tipo,
                Natureza = Natureza,
                AceitaLancamento = AceitaLancamento,
                ContaPaiId = ContaPaiId
            };

            if (_original == null)
                await _servico.CriarAsync(dto);
            else
                await _servico.AtualizarAsync(dto);

            DialogHost.Close("RootDialog", true);
        }
        finally { Carregando = false; }
    }

    private async Task AbrirPaiAsync()
    {
        var vm = new PlanoContasPopupViewModel(_servico, null);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is PlanoContaDto conta)
        {
            ContaPaiId = conta.Id;
            ContaPaiDescricao = $"{conta.Codigo} - {conta.Nome}";
        }
    }
}