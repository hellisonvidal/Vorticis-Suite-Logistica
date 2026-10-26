using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Importacao;

namespace Vorticis.Desktop.ViewModels;

public sealed record ContaPreviaLinha(string Linha, string Original, string Codigo, string Descricao, string Tipo, string Natureza, string Saldo);

/// <summary>
/// Integrações › Importar › Plano de contas (planilha e máscara). Lê um plano de Excel/CSV, escolhe as colunas,
/// converte os códigos para a máscara desejada e substitui o plano (antes do primeiro lançamento). Também converte
/// os códigos do plano atual para outra máscara.
/// </summary>
public partial class ImportacaoPlanoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public const string SemColuna = "(deduzir)";
    private TabelaImportada? _tabela;
    private PlanoImportado? _plano;
    private bool _lendo;

    public static IReadOnlyList<string> Mascaras => MascaraPlano.Sugestoes;

    [ObservableProperty] private string _arquivo = "";
    public ObservableCollection<string> Colunas { get; } = [];
    public ObservableCollection<string> ColunasOpcionais { get; } = [];
    [ObservableProperty] private int _colunaCodigo = -1;
    [ObservableProperty] private int _colunaDescricao = -1;
    [ObservableProperty] private int _colunaNatureza;
    [ObservableProperty] private int _colunaTipo;
    [ObservableProperty] private int _colunaSaldo;

    [ObservableProperty] private bool _converterParaMascara = true;
    [ObservableProperty] private string _mascara = MascaraPlano.Sugestoes[0];
    [ObservableProperty] private string _mascaraAtual = "";
    [ObservableProperty] private string? _mascaraModelo;

    partial void OnMascaraModeloChanged(string? value)
    {
        if (value is not null) Mascara = value;
    }

    public ObservableCollection<ContaPreviaLinha> Previa { get; } = [];
    public ObservableCollection<string> Problemas { get; } = [];
    [ObservableProperty] private string _resumoPrevia = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ImportarCommand))] private bool _previaValida;

    public bool PodeEditar => Contexto.Pode(Permissao.PlanoContasManter);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    /// <summary>Ao abrir: máscara do plano atual (sugerida também como destino).</summary>
    public async Task CarregarAsync()
    {
        AtualizarPermissoes();
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var plano = await app.PlanoContas.ListarAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        MascaraAtual = MascaraPlano.Deduzir(plano.Select(c => c.Codigo)) ?? "";
        if (MascaraAtual.Length > 0 && _tabela is null) Mascara = MascaraAtual;
    }

    partial void OnArquivoChanged(string value)
    {
        if (File.Exists(value)) LerArquivo();
    }

    [RelayCommand]
    private void LerArquivo()
    {
        Erros.Clear();
        Mensagem = null;
        var r = LeitorPlanilha.Ler(Arquivo);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _tabela = r.Valor;
        Colunas.Clear();
        ColunasOpcionais.Clear();
        ColunasOpcionais.Add(SemColuna);
        foreach (var c in _tabela.Colunas) { Colunas.Add(c); ColunasOpcionais.Add(c); }
        var det = ColunasPlano.Detectar(_tabela);
        _lendo = true;
        try
        {
            // As listas foram refeitas: força a notificação mesmo quando o índice não muda.
            (ColunaCodigo, ColunaDescricao, ColunaNatureza, ColunaTipo, ColunaSaldo) = (-1, -1, -1, -1, -1);
            (ColunaCodigo, ColunaDescricao) = (det.Codigo, det.Descricao);
            (ColunaNatureza, ColunaTipo, ColunaSaldo) = (det.Natureza + 1, det.Tipo + 1, det.NaturezaSaldo + 1);
        }
        finally { _lendo = false; }
        Mensagem = $"Planilha lida: {_tabela.Linhas.Count} linha(s), {_tabela.Colunas.Count} coluna(s). Confira as colunas e a máscara.";
        Atualizar();
    }

    partial void OnColunaCodigoChanged(int value) => Atualizar();
    partial void OnColunaDescricaoChanged(int value) => Atualizar();
    partial void OnColunaNaturezaChanged(int value) => Atualizar();
    partial void OnColunaTipoChanged(int value) => Atualizar();
    partial void OnColunaSaldoChanged(int value) => Atualizar();
    partial void OnConverterParaMascaraChanged(bool value) => Atualizar();
    partial void OnMascaraChanged(string value) => Atualizar();

    /// <summary>Refaz a pré-visualização com as colunas e a máscara escolhidas.</summary>
    private void Atualizar()
    {
        if (_lendo) return;
        Previa.Clear();
        Problemas.Clear();
        _plano = null;
        PreviaValida = false;
        ResumoPrevia = "";
        if (_tabela is null || ColunaCodigo < 0 || ColunaDescricao < 0) return;
        MascaraPlano? mascara = null;
        if (ConverterParaMascara)
        {
            var m = MascaraPlano.Ler(Mascara);
            if (!m.Sucesso) { Problemas.Add(m.Erros[0]); return; }
            mascara = m.Valor;
        }
        _plano = LeitorPlanoContas.Ler(_tabela, new ColunasPlano(ColunaCodigo, ColunaDescricao, ColunaNatureza - 1, ColunaTipo - 1, ColunaSaldo - 1), mascara);
        foreach (var c in _plano.Contas)
            Previa.Add(new ContaPreviaLinha(c.Linha.ToString(), c.CodigoOriginal, new string(' ', (c.Nivel - 1) * 2) + c.Codigo, c.Descricao,
                c.Analitica ? "Analítica" : "Sintética", OpcaoNatureza.Todas.FirstOrDefault(n => n.Codigo == c.CodNat)?.Nome ?? c.CodNat,
                c.NaturezaSaldo == IndicadorDC.D ? "Devedora" : "Credora"));
        foreach (var e in _plano.Erros) Problemas.Add(e);
        PreviaValida = _plano.Valido;
        var analiticas = _plano.Contas.Count(c => c.Analitica);
        var convertidos = _plano.Contas.Count(c => c.Codigo != c.CodigoOriginal.Trim());
        ResumoPrevia = $"{_plano.Contas.Count} conta(s): {_plano.Contas.Count - analiticas} sintética(s), {analiticas} analítica(s)" +
                       (convertidos > 0 ? $" · {convertidos} código(s) ajustado(s) para a máscara" : "") +
                       (_plano.Erros.Count > 0 ? $" · {_plano.Erros.Count} problema(s) a corrigir na planilha" : " · pronto para importar");
    }

    [RelayCommand(CanExecute = nameof(PreviaValida))]
    private Task ImportarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (_plano is not { Valido: true } plano) return;
        if (!await Contexto.ConfirmarAsync("Importar plano de contas",
                $"O plano de contas atual da empresa será SUBSTITUÍDO pelas {plano.Contas.Count} contas da planilha.\n\n" +
                "Os vínculos com o plano referencial do plano atual serão descartados. Só é possível enquanto não houver lançamentos.",
                "Substituir plano", "Cancelar")) return;
        var r = await app.PlanoContas.ImportarAsync(empresa, plano, usuario);
        MostrarResultado(r, r.Sucesso ? $"Plano de contas importado: {r.Valor} conta(s)." : "");
        if (r.Sucesso) MascaraAtual = MascaraPlano.Deduzir(plano.Contas.Select(c => c.Codigo)) ?? "";
    }, Permissao.PlanoContasManter);

    /// <summary>Converte os códigos do plano atual para a máscara escolhida (lançamentos e hierarquia não mudam).</summary>
    [RelayCommand]
    private Task AplicarMascaraAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var m = MascaraPlano.Ler(Mascara);
        if (!m.Sucesso) { MostrarResultado(m, ""); return; }
        var sim = await app.PlanoContas.SimularMascaraAsync(empresa, m.Valor, usuario);
        if (!sim.Sucesso) { MostrarResultado(sim, ""); return; }
        var mudancas = sim.Valor.Where(x => x.Conta.Codigo != x.Novo).ToList();
        if (mudancas.Count == 0) { Mensagem = $"O plano atual já está na máscara {m.Valor.Texto}."; return; }
        var exemplos = string.Join("\n", mudancas.Take(5).Select(x => $"   {x.Conta.Codigo}  →  {x.Novo}"));
        if (!await Contexto.ConfirmarAsync("Converter máscara do plano",
                $"{mudancas.Count} conta(s) terão o código convertido para a máscara {m.Valor.Texto}, por exemplo:\n{exemplos}\n\n" +
                "Os lançamentos continuam nas mesmas contas. Arquivos já entregues (ECD de anos anteriores) mantêm os códigos antigos.",
                "Converter", "Cancelar")) return;
        var r = await app.PlanoContas.AplicarMascaraAsync(empresa, m.Valor, usuario);
        MostrarResultado(r, r.Sucesso ? $"{r.Valor} código(s) convertido(s) para a máscara {m.Valor.Texto}." : "");
        if (r.Sucesso) MascaraAtual = m.Valor.Texto;
    }, Permissao.PlanoContasManter);
}
