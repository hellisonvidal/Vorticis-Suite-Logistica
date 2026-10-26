using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Relatorios;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Relatórios › Resumos › Resumo de vendas / de compras: notas autorizadas do período, por nota, participante ou mês.</summary>
public abstract partial class ResumoNotasViewModel(IContexto contexto, TipoOperacao operacao) : RelatorioFiscalViewModel(contexto)
{
    private (DateOnly Ini, DateOnly Fim) _periodo;

    public static IReadOnlyList<string> Agrupamentos => TabelasFiscais.AgrupamentosResumo;

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _agrupamento;

    public string Titulo => operacao == TipoOperacao.Saida ? "Resumo de vendas" : "Resumo de compras";
    protected override string TituloRelatorio => Titulo;
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy} · {Agrupamentos[Agrupamento]}";
    protected override string NomeArquivo => $"{(operacao == TipoOperacao.Saida ? "resumo_vendas" : "resumo_compras")}_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var fim = Data(Fim, ini);
        var r = await app.Fiscal().Documentos.ConsultarAsync(empresa, new FiltroDocumentos(ini, fim, operacao, Limite: 100000), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        _periodo = (ini, fim);
        Mostrar(TabelasFiscais.ResumoNotas(r.Valor, operacao, Agrupamento));
        Mensagem = r.Valor.Count == 0 ? "Nenhuma nota no período." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}

public sealed class ResumoVendasViewModel(IContexto contexto) : ResumoNotasViewModel(contexto, TipoOperacao.Saida);

public sealed class ResumoComprasViewModel(IContexto contexto) : ResumoNotasViewModel(contexto, TipoOperacao.Entrada);

/// <summary>Relatório de recebimentos (ou de pagamentos): baixas das duplicatas feitas no período.</summary>
public abstract partial class RelatorioBaixasViewModel(IContexto contexto, TipoDuplicata tipo) : RelatorioFiscalViewModel(contexto)
{
    private (DateOnly Ini, DateOnly Fim) _periodo;

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;

    public string Titulo => tipo == TipoDuplicata.Receber ? "Relatório de recebimentos" : "Relatório de pagamentos";
    protected override string TituloRelatorio => Titulo;
    protected override string Parametros => $"Baixas de {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"{(tipo == TipoDuplicata.Receber ? "recebimentos" : "pagamentos")}_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var fim = Data(Fim, ini);
        var dups = await app.Fiscal().Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(tipo, Limite: 100000), usuario);
        if (!dups.Sucesso) { MostrarResultado(dups, ""); Limpar(); return; }
        var porId = dups.Valor.ToDictionary(d => d.Id);
        var baixas = await app.Fiscal().Duplicatas.BaixasAsync(empresa, porId.Keys.ToList(), usuario);
        if (!baixas.Sucesso) { MostrarResultado(baixas, ""); Limpar(); return; }
        var linhas = baixas.Valor.Where(b => b.Data >= ini && b.Data <= fim && porId.ContainsKey(b.DuplicataId))
            .OrderBy(b => b.Data).Select(b => (porId[b.DuplicataId], b)).ToList();
        _periodo = (ini, fim);
        Mostrar(TabelasFiscais.Baixas(linhas, tipo));
        Mensagem = linhas.Count == 0 ? "Nenhuma baixa no período." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}

public sealed class RelRecebimentosViewModel(IContexto contexto) : RelatorioBaixasViewModel(contexto, TipoDuplicata.Receber);

public sealed class RelPagamentosViewModel(IContexto contexto) : RelatorioBaixasViewModel(contexto, TipoDuplicata.Pagar);
