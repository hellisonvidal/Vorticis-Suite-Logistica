using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Contabil.Desktop.Servicos;
using Vorticis.Domain.Contabil.Lancamentos;

namespace Vorticis.Contabil.Desktop.ViewModels;

public enum TipoPartida { Simples, Dobrada }

public sealed partial class LancamentoViewModel : ObservableObject
{
    private readonly ILancamentoServico _servico;
    private readonly IPlanoContasServico _planoContasServico;

    public LancamentoViewModel(ILancamentoServico servico, IPlanoContasServico planoContasServico)
    {
        _servico = servico;
        _planoContasServico = planoContasServico;
        CarregarCommand = new AsyncRelayCommand(CarregarAsync);
        NovoCommand = new AsyncRelayCommand(NovoAsync);
        EditarCommand = new AsyncRelayCommand<LancamentoDto>(EditarAsync);
        ExcluirCommand = new AsyncRelayCommand<LancamentoDto>(ExcluirAsync);
        SalvarCommand = new AsyncRelayCommand(SalvarAsync, PodeSalvar);
        AdicionarItemCommand = new RelayCommand(AdicionarItem);
        RemoverItemCommand = new RelayCommand<LancamentoItemDto>(RemoverItem);
        AbrirContaCommand = new AsyncRelayCommand<LancamentoItemDto>(AbrirContaAsync);
    }

    [ObservableProperty] private ObservableCollection<LancamentoDto> _lancamentos = [];
    [ObservableProperty] private LancamentoDto? _selecionado;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private string _filtro = "";
    [ObservableProperty] private DateTime _dataInicial = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime _dataFinal = DateTime.Today;

    [ObservableProperty] private TipoPartida _tipoPartida = TipoPartida.Dobrada;
    [ObservableProperty] private bool _validandoPartida = true;

    public IAsyncRelayCommand CarregarCommand { get; }
    public IAsyncRelayCommand NovoCommand { get; }
    public IAsyncRelayCommand<LancamentoDto> EditarCommand { get; }
    public IAsyncRelayCommand<LancamentoDto> ExcluirCommand { get; }
    public IAsyncRelayCommand SalvarCommand { get; }
    public IRelayCommand AdicionarItemCommand { get; }
    public IRelayCommand<LancamentoItemDto> RemoverItemCommand { get; }
    public IAsyncRelayCommand<LancamentoItemDto> AbrirContaCommand { get; }

    public bool EhPartidaSimples => TipoPartida == TipoPartida.Simples;
    public bool EhPartidaDobrada => TipoPartida == TipoPartida.Dobrada;

    public string LabelTipoPartida => TipoPartida == TipoPartida.Simples ? "Partida Simples" : "Partida Dobrada";
    public string DescricaoTipoPartida => TipoPartida == TipoPartida.Simples
        ? "Apenas uma conta (débito OU crédito). Usado para ajustes, abertura, encerramento."
        : "Duas ou mais contas (débitos = créditos). Lançamento contábil padrão.";

    partial void OnTipoPartidaChanged(TipoPartida value)
    {
        OnPropertyChanged(nameof(EhPartidaSimples));
        OnPropertyChanged(nameof(EhPartidaDobrada));
        OnPropertyChanged(nameof(LabelTipoPartida));
        OnPropertyChanged(nameof(DescricaoTipoPartida));
        ValidarPartida();
    }

    private async Task CarregarAsync()
    {
        Carregando = true;
        try
        {
            var lista = await _servico.ListarAsync(DataInicial, DataFinal);
            Lancamentos = new(lista.Where(l => string.IsNullOrWhiteSpace(Filtro) ||
                l.Historico.Contains(Filtro, StringComparison.OrdinalIgnoreCase) ||
                l.Numero.Contains(Filtro)));
        }
        finally { Carregando = false; }
    }

    private async Task NovoAsync()
    {
        var vm = new LancamentoPopupViewModel(_servico, _planoContasServico, null, TipoPartida);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }

    private async Task EditarAsync(LancamentoDto? lanc)
    {
        if (lanc == null) return;
        var vm = new LancamentoPopupViewModel(_servico, _planoContasServico, lanc, TipoPartida);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }

    private async Task ExcluirAsync(LancamentoDto? lanc)
    {
        if (lanc == null) return;
        var confirm = await DialogHost.Show(
            new ConfirmacaoViewModel($"Excluir lançamento {lanc.Numero}?", "Esta ação não pode ser desfeita."),
            "RootDialog");
        if (confirm is true)
        {
            await _servico.ExcluirAsync(lanc.Id);
            await CarregarAsync();
        }
    }

    private void AdicionarItem()
    {
        if (Selecionado == null) return;
        var item = new LancamentoItemDto { Id = Guid.NewGuid(), Sequencia = Selecionado.Itens.Count + 1 };
        Selecionado.Itens.Add(item);
        ValidarPartida();
    }

    private void RemoverItem(LancamentoItemDto? item)
    {
        if (item == null || Selecionado == null) return;
        Selecionado.Itens.Remove(item);
        for (int i = 0; i < Selecionado.Itens.Count; i++)
            Selecionado.Itens[i].Sequencia = i + 1;
        ValidarPartida();
    }

    private async Task AbrirContaAsync(LancamentoItemDto? item)
    {
        if (item == null) return;
        var vm = new PlanoContasPopupViewModel(_planoContasServico, null);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is PlanoContaDto conta)
        {
            item.ContaId = conta.Id;
            item.ContaCodigo = conta.Codigo;
            item.ContaNome = conta.Nome;
            ValidarPartida();
        }
    }

    private bool PodeSalvar()
    {
        if (Selecionado == null || Selecionado.Itens.Count == 0) return false;
        return ValidarPartida();
    }

    private bool ValidarPartida()
    {
        if (Selecionado == null) return false;

        var debitos = Selecionado.Itens.Where(i => i.Valor > 0).Sum(i => i.Valor);
        var creditos = Selecionado.Itens.Where(i => i.Valor < 0).Sum(i => Math.Abs(i.Valor));

        if (TipoPartida == TipoPartida.Simples)
        {
            var contasComValor = Selecionado.Itens.Count(i => i.ContaId != null && i.Valor != 0);
            ValidandoPartida = contasComValor == 1;
            return ValidandoPartida;
        }
        else
        {
            var contasComValor = Selecionado.Itens.Count(i => i.ContaId != null && i.Valor != 0);
            ValidandoPartida = contasComValor >= 2 && Math.Abs(debitos - creditos) < 0.01m;
            return ValidandoPartida;
        }
    }

    private async Task SalvarAsync()
    {
        if (Selecionado == null) return;
        Carregando = true;
        try
        {
            if (Selecionado.Id == Guid.Empty)
                await _servico.CriarAsync(Selecionado);
            else
                await _servico.AtualizarAsync(Selecionado);

            DialogHost.Close("RootDialog", true);
        }
        finally { Carregando = false; }
    }
}

public sealed partial class LancamentoPopupViewModel : ObservableObject
{
    private readonly ILancamentoServico _servico;
    private readonly IPlanoContasServico _planoContasServico;
    private readonly LancamentoDto? _original;
    private readonly TipoPartida _tipoPartida;

    public LancamentoPopupViewModel(ILancamentoServico servico, IPlanoContasServico planoContasServico,
        LancamentoDto? original, TipoPartida tipoPartida)
    {
        _servico = servico;
        _planoContasServico = planoContasServico;
        _original = original;
        _tipoPartida = tipoPartida;

        if (original != null)
        {
            Id = original.Id;
            Numero = original.Numero;
            Data = original.Data;
            Historico = original.Historico;
            Itens = new(original.Itens.Select(i => new LancamentoItemDto
            {
                Id = i.Id,
                Sequencia = i.Sequencia,
                ContaId = i.ContaId,
                ContaCodigo = i.ContaCodigo,
                ContaNome = i.ContaNome,
                Valor = i.Valor,
                Historico = i.Historico
            }));
        }
        else
        {
            Data = DateTime.Today;
            Itens = [];
            AdicionarItem();
        }

        SalvarCommand = new AsyncRelayCommand(SalvarAsync, PodeSalvar);
        AdicionarItemCommand = new RelayCommand(AdicionarItem);
        RemoverItemCommand = new RelayCommand<LancamentoItemDto>(RemoverItem);
        AbrirContaCommand = new AsyncRelayCommand<LancamentoItemDto>(AbrirContaAsync);
    }

    [ObservableProperty] private Guid _id = Guid.Empty;
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private DateTime _data = DateTime.Today;
    [ObservableProperty] private string _historico = "";
    [ObservableProperty] private ObservableCollection<LancamentoItemDto> _itens = [];
    [ObservableProperty] private bool _carregando;

    public TipoPartida TipoPartida => _tipoPartida;
    public bool EhPartidaSimples => _tipoPartida == TipoPartida.Simples;
    public bool EhPartidaDobrada => _tipoPartida == TipoPartida.Dobrada;

    public IAsyncRelayCommand SalvarCommand { get; }
    public IRelayCommand AdicionarItemCommand { get; }
    public IRelayCommand<LancamentoItemDto> RemoverItemCommand { get; }
    public IAsyncRelayCommand<LancamentoItemDto> AbrirContaCommand { get; }

    public decimal TotalDebitos => Itens.Where(i => i.Valor > 0).Sum(i => i.Valor);
    public decimal TotalCreditos => Itens.Where(i => i.Valor < 0).Sum(i => Math.Abs(i.Valor));
    public bool PartidaValida => _tipoPartida == TipoPartida.Simples
        ? Itens.Count(i => i.ContaId != null && i.Valor != 0) == 1
        : Itens.Count(i => i.ContaId != null && i.Valor != 0) >= 2 && Math.Abs(TotalDebitos - TotalCreditos) < 0.01m;

    partial void OnItensChanged(ObservableCollection<LancamentoItemDto> value)
    {
        OnPropertyChanged(nameof(TotalDebitos));
        OnPropertyChanged(nameof(TotalCreditos));
        OnPropertyChanged(nameof(PartidaValida));
    }

    private bool PodeSalvar() => PartidaValida && !string.IsNullOrWhiteSpace(Historico);

    private void AdicionarItem()
    {
        var item = new LancamentoItemDto { Id = Guid.NewGuid(), Sequencia = Itens.Count + 1 };
        Itens.Add(item);
        OnPropertyChanged(nameof(TotalDebitos));
        OnPropertyChanged(nameof(TotalCreditos));
        OnPropertyChanged(nameof(PartidaValida));
    }

    private void RemoverItem(LancamentoItemDto? item)
    {
        if (item == null) return;
        Itens.Remove(item);
        for (int i = 0; i < Itens.Count; i++)
            Itens[i].Sequencia = i + 1;
        OnPropertyChanged(nameof(TotalDebitos));
        OnPropertyChanged(nameof(TotalCreditos));
        OnPropertyChanged(nameof(PartidaValida));
    }

    private async Task AbrirContaAsync(LancamentoItemDto? item)
    {
        if (item == null) return;
        var vm = new PlanoContasPopupViewModel(_planoContasServico, null);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is PlanoContaDto conta)
        {
            item.ContaId = conta.Id;
            item.ContaCodigo = conta.Codigo;
            item.ContaNome = conta.Nome;
            OnPropertyChanged(nameof(TotalDebitos));
            OnPropertyChanged(nameof(TotalCreditos));
            OnPropertyChanged(nameof(PartidaValida));
        }
    }

    private async Task SalvarAsync()
    {
        Carregando = true;
        try
        {
            var dto = new LancamentoDto
            {
                Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
                Numero = Numero,
                Data = Data,
                Historico = Historico,
                Itens = Itens.Where(i => i.ContaId != null && i.Valor != 0).ToList(),
                TipoPartida = _tipoPartida
            };

            if (_original == null)
                await _servico.CriarAsync(dto);
            else
                await _servico.AtualizarAsync(dto);

            DialogHost.Close("RootDialog", true);
        }
        finally { Carregando = false; }
    }
}