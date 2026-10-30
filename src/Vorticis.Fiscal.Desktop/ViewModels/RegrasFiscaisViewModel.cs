using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Fiscal.Desktop.Servicos;
using Vorticis.Domain.Fiscal.Regras;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed partial class RegrasFiscaisViewModel : ObservableObject
{
    private readonly IRegraFiscalServico _servico;

    public RegrasFiscaisViewModel(IRegraFiscalServico servico)
    {
        _servico = servico;
        CarregarCommand = new AsyncRelayCommand(CarregarAsync);
        NovaCommand = new AsyncRelayCommand(NovaAsync);
        EditarCommand = new AsyncRelayCommand<RegraFiscalDto>(EditarAsync);
        ExcluirCommand = new AsyncRelayCommand<RegraFiscalDto>(ExcluirAsync);
        DuplicarCommand = new AsyncRelayCommand<RegraFiscalDto>(DuplicarAsync);
    }

    [ObservableProperty] private ObservableCollection<RegraFiscalDto> _regras = [];
    [ObservableProperty] private RegraFiscalDto? _selecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private string _filtro = "";

    public IAsyncRelayCommand CarregarCommand { get; }
    public IAsyncRelayCommand NovaCommand { get; }
    public IAsyncRelayCommand<RegraFiscalDto> EditarCommand { get; }
    public IAsyncRelayCommand<RegraFiscalDto> ExcluirCommand { get; }
    public IAsyncRelayCommand<RegraFiscalDto> DuplicarCommand { get; }

    public IReadOnlyList<CstItem> CstsIcms => CstCatalog.Icms;
    public IReadOnlyList<CstItem> CstsIpi => CstCatalog.Ipi;
    public IReadOnlyList<CstItem> CstsPis => CstCatalog.Pis;
    public IReadOnlyList<CstItem> CstsCofins => CstCatalog.Cofins;

    private async Task CarregarAsync()
    {
        Carregando = true;
        try
        {
            var lista = await _servico.ListarAsync();
            Regras = new(lista.Where(r => string.IsNullOrWhiteSpace(Filtro) ||
                r.Nome.Contains(Filtro, StringComparison.OrdinalIgnoreCase) ||
                r.CstIcms.Contains(Filtro) ||
                r.CstIpi.Contains(Filtro) ||
                r.CstPis.Contains(Filtro) ||
                r.CstCofins.Contains(Filtro)));
        }
        finally { Carregando = false; }
    }

    private async Task NovaAsync()
    {
        var vm = new RegraFiscalPopupViewModel(_servico, null);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }

    private async Task EditarAsync(RegraFiscalDto? regra)
    {
        if (regra == null) return;
        var vm = new RegraFiscalPopupViewModel(_servico, regra);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }

    private async Task ExcluirAsync(RegraFiscalDto? regra)
    {
        if (regra == null) return;
        var confirm = await DialogHost.Show(
            new ConfirmacaoViewModel($"Excluir regra '{regra.Nome}'?", "Esta ação não pode ser desfeita."),
            "RootDialog");
        if (confirm is true)
        {
            await _servico.ExcluirAsync(regra.Id);
            await CarregarAsync();
        }
    }

    private async Task DuplicarAsync(RegraFiscalDto? regra)
    {
        if (regra == null) return;
        var copia = regra with { Id = Guid.Empty, Nome = regra.Nome + " (Cópia)", Ativa = false };
        var vm = new RegraFiscalPopupViewModel(_servico, copia);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is true) await CarregarAsync();
    }
}

public static class CstCatalog
{
    public static readonly CstItem[] Icms =
    [
        new("00", "Tributada integralmente"),
        new("10", "Tributada com cobrança do ICMS por substituição tributária"),
        new("20", "Com redução de base de cálculo"),
        new("30", "Isenta ou não tributada e com cobrança do ICMS por ST"),
        new("40", "Isenta"),
        new("41", "Não tributada"),
        new("50", "Suspensão"),
        new("51", "Diferimento"),
        new("60", "ICMS cobrado anteriormente por ST"),
        new("70", "Com redução de base de cálculo e cobrança do ICMS por ST"),
        new("90", "Outras")
    ];

    public static readonly CstItem[] Ipi =
    [
        new("00", "Entrada com recuperação de crédito"),
        new("01", "Entrada tributada com alíquota zero"),
        new("02", "Entrada isenta"),
        new("03", "Entrada não tributada"),
        new("04", "Entrada imune"),
        new("05", "Entrada com suspensão"),
        new("49", "Outras entradas"),
        new("50", "Saída tributada"),
        new("51", "Saída tributada com alíquota zero"),
        new("52", "Saída isenta"),
        new("53", "Saída não tributada"),
        new("54", "Saída imune"),
        new("55", "Saída com suspensão"),
        new("99", "Outras saídas")
    ];

    public static readonly CstItem[] Pis =
    [
        new("01", "Operação Tributável (base de cálculo = valor da operação)"),
        new("02", "Operação Tributável (base de cálculo = valor da operação) - Alíquota Diferenciada"),
        new("03", "Operação Tributável (base de cálculo = quantidade vendida)"),
        new("04", "Operação Tributável (tributação monofásica - alíquota zero)"),
        new("05", "Operação Tributável (substituição tributária)"),
        new("06", "Operação Tributável (alíquota zero)"),
        new("07", "Operação Isenta da Contribuição"),
        new("08", "Operação Sem Incidência da Contribuição"),
        new("09", "Operação com Suspensão da Contribuição"),
        new("49", "Outras Operações de Saída"),
        new("50", "Operação com Direito a Crédito (vinculada exclusivamente a receitas tributadas)"),
        new("51", "Operação com Direito a Crédito (vinculada exclusivamente a receitas não tributadas)"),
        new("52", "Operação com Direito a Crédito (vinculada exclusivamente a receitas de exportação)"),
        new("53", "Operação com Direito a Crédito (vinculada a receitas tributadas e não tributadas)"),
        new("54", "Operação com Direito a Crédito (vinculada a receitas tributadas e de exportação)"),
        new("55", "Operação com Direito a Crédito (vinculada a receitas não tributadas e de exportação)"),
        new("56", "Operação com Direito a Crédito (vinculada a receitas tributadas, não tributadas e de exportação)"),
        new("60", "Crédito Presumido"),
        new("61", "Crédito Presumido (Lei 10.925/2004)"),
        new("62", "Crédito Presumido (Lei 10.637/2002 - aquisição de insumos)"),
        new("70", "Operação de Aquisição Sem Direito a Crédito"),
        new("71", "Operação de Aquisição com Isenção"),
        new("72", "Operação de Aquisição com Suspensão"),
        new("73", "Operação de Aquisição a Alíquota Zero"),
        new("74", "Operação de Aquisição Sem Incidência"),
        new("75", "Operação de Aquisição por Substituição Tributária"),
        new("98", "Outras Operações de Entrada"),
        new("99", "Outras Operações")
    ];

    public static readonly CstItem[] Cofins =
    [
        new("01", "Operação Tributável (base de cálculo = valor da operação)"),
        new("02", "Operação Tributável (base de cálculo = valor da operação) - Alíquota Diferenciada"),
        new("03", "Operação Tributável (base de cálculo = quantidade vendida)"),
        new("04", "Operação Tributável (tributação monofásica - alíquota zero)"),
        new("05", "Operação Tributável (substituição tributária)"),
        new("06", "Operação Tributável (alíquota zero)"),
        new("07", "Operação Isenta da Contribuição"),
        new("08", "Operação Sem Incidência da Contribuição"),
        new("09", "Operação com Suspensão da Contribuição"),
        new("49", "Outras Operações de Saída"),
        new("50", "Operação com Direito a Crédito (vinculada exclusivamente a receitas tributadas)"),
        new("51", "Operação com Direito a Crédito (vinculada exclusivamente a receitas não tributadas)"),
        new("52", "Operação com Direito a Crédito (vinculada exclusivamente a receitas de exportação)"),
        new("53", "Operação com Direito a Crédito (vinculada a receitas tributadas e não tributadas)"),
        new("54", "Operação com Direito a Crédito (vinculada a receitas tributadas e de exportação)"),
        new("55", "Operação com Direito a Crédito (vinculada a receitas não tributadas e de exportação)"),
        new("56", "Operação com Direito a Crédito (vinculada a receitas tributadas, não tributadas e de exportação)"),
        new("60", "Crédito Presumido"),
        new("61", "Crédito Presumido (Lei 10.925/2004)"),
        new("62", "Crédito Presumido (Lei 10.833/2003 - aquisição de insumos)"),
        new("70", "Operação de Aquisição Sem Direito a Crédito"),
        new("71", "Operação de Aquisição com Isenção"),
        new("72", "Operação de Aquisição com Suspensão"),
        new("73", "Operação de Aquisição a Alíquota Zero"),
        new("74", "Operação de Aquisição Sem Incidência"),
        new("75", "Operação de Aquisição por Substituição Tributária"),
        new("98", "Outras Operações de Entrada"),
        new("99", "Outras Operações")
    ];
}

public sealed record CstItem(string Codigo, string Descricao)
{
    public string Exibicao => $"{Codigo} - {Descricao}";
}