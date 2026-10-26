using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Guias;
using Vorticis.Fiscal.Relatorios;
using Vorticis.Fiscal.Simples;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public static class RotulosSimples
{
    public static string Atividade(AtividadeSimples a) => a switch
    {
        AtividadeSimples.ComercioRevenda => "Revenda de mercadorias (Anexo I)",
        AtividadeSimples.IndustriaVenda => "Venda de produtos industrializados (Anexo II)",
        AtividadeSimples.ServicosAnexoIII => "Serviços do Anexo III",
        AtividadeSimples.ServicosAnexoIV => "Serviços do Anexo IV",
        AtividadeSimples.ServicosAnexoV => "Serviços do Anexo V",
        _ => "Serviços sujeitos ao Fator R (Anexo III ou V)",
    };

    public static string Tratamento(TratamentoSimples t)
    {
        if (t == TratamentoSimples.Normal) return "Normal";
        var partes = new List<string>();
        if (t.HasFlag(TratamentoSimples.SubstituicaoTributaria)) partes.Add("ICMS-ST");
        if (t.HasFlag(TratamentoSimples.Monofasico)) partes.Add("Monofásico");
        if (t.HasFlag(TratamentoSimples.IssRetido)) partes.Add("ISS retido");
        if (t.HasFlag(TratamentoSimples.Exportacao)) partes.Add("Exportação");
        return string.Join(" + ", partes);
    }
}

public partial class ReceitaSimplesLinha(ReceitaSimples receita) : ObservableObject
{
    public AtividadeSimples Atividade { get; } = receita.Atividade;
    public TratamentoSimples Tratamento { get; } = receita.Tratamento;
    public string Descricao => RotulosSimples.Atividade(Atividade);
    public string TratamentoTexto => RotulosSimples.Tratamento(Tratamento);
    [ObservableProperty] private string _valorTexto = Numeros.Moeda(receita.Valor);

    public ReceitaSimples ParaReceita() => new(Atividade, Numeros.LerValor(ValorTexto) ?? 0m, Tratamento);
}

/// <summary>Apuração › Simples Nacional: segregação da receita por anexo, RBT12, Fator R, DAS com repartição por tributo e memória de cálculo.</summary>
public partial class ApuracaoSimplesViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    private long? _apuracaoId;
    private bool _provisaoContabilizada;
    private DateOnly _competencia = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public static IReadOnlyList<AtividadeSimples> AtividadesServicos { get; } =
        [AtividadeSimples.ServicosFatorR, AtividadeSimples.ServicosAnexoIII, AtividadeSimples.ServicosAnexoIV, AtividadeSimples.ServicosAnexoV];
    public static IReadOnlyList<string> RotulosAtividadesServicos { get; } = AtividadesServicos.Select(RotulosSimples.Atividade).ToList();
    public static IReadOnlyList<AtividadeSimples> Atividades { get; } = Enum.GetValues<AtividadeSimples>();
    public static IReadOnlyList<string> RotulosAtividades { get; } = Atividades.Select(RotulosSimples.Atividade).ToList();

    public ObservableCollection<ReceitaSimplesLinha> Receitas { get; } = [];

    [ObservableProperty] private DateTime? _competenciaData = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private int _atividadeServicos;
    [ObservableProperty] private string _rbt12Texto = "";
    [ObservableProperty] private string _folha12Texto = "";
    [ObservableProperty] private int _novaAtividade;
    [ObservableProperty] private string _novoValor = "";
    [ObservableProperty] private bool _novaST;
    [ObservableProperty] private bool _novoMonofasico;
    [ObservableProperty] private bool _novoIssRetido;
    [ObservableProperty] private bool _novaExportacao;
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private string _avisosTexto = "";

    public bool PodeApurar => Contexto.Pode(Permissao.FiscalApurar);
    public bool PodeContabilizar => _apuracaoId is not null && !_provisaoContabilizada;
    public bool PodeDesfazer => _apuracaoId is not null && _provisaoContabilizada;

    protected override string TituloRelatorio => "Apuração do Simples Nacional";
    protected override string Parametros => $"Competência {_competencia:MM/yyyy}";
    protected override string NomeArquivo => $"simples_nacional_{_competencia:yyyyMM}";

    public override void AtualizarPermissoes()
    {
        base.AtualizarPermissoes();
        OnPropertyChanged(nameof(PodeApurar));
    }

    private DateOnly Competencia() => Data(CompetenciaData, _competencia) is var d ? new DateOnly(d.Year, d.Month, 1) : _competencia;

    private void AtualizarEstado()
    {
        OnPropertyChanged(nameof(PodeContabilizar));
        OnPropertyChanged(nameof(PodeDesfazer));
        Situacao = _apuracaoId is null ? "Apuração ainda não salva." : _provisaoContabilizada ? "Apuração salva · provisão contabilizada." : "Apuração salva · provisão não contabilizada.";
    }

    partial void OnCompetenciaDataChanged(DateTime? value)
    {
        _apuracaoId = null;
        _provisaoContabilizada = false;
        AtualizarEstado();
    }

    private IReadOnlyList<ReceitaSimples> ReceitasInformadas() => Receitas.Select(r => r.ParaReceita()).Where(r => r.Valor != 0).ToList();

    private void MostrarCalculo(ResultadoSimples r)
    {
        _competencia = r.Competencia;
        Mostrar(TabelasFiscais.Simples(r));
        AvisosTexto = string.Join(Environment.NewLine, r.Avisos);
    }

    /// <summary>Sugere a segregação da receita pelas notas e preenche o RBT12 e a folha dos 12 meses.</summary>
    [RelayCommand]
    private Task SugerirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var comp = Competencia();
        var r = await app.Fiscal().Simples.PrepararAsync(empresa, comp, AtividadesServicos[Math.Max(0, AtividadeServicos)], usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Receitas.Clear();
        foreach (var s in r.Valor.Sugestao) Receitas.Add(new ReceitaSimplesLinha(s));
        (Rbt12Texto, Folha12Texto) = (Numeros.Moeda(r.Valor.Rbt12), Numeros.Moeda(r.Valor.Folha12));
        AvisosTexto = string.Join(Environment.NewLine, r.Valor.Avisos);
        var salva = (await app.Fiscal().Simples.ListarAsync(empresa, comp.Year, usuario)).Valor?.FirstOrDefault(a => a.Competencia == comp);
        (_apuracaoId, _provisaoContabilizada) = (salva?.Id, salva?.LancamentoId is not null);
        if (salva is not null) MostrarCalculo(salva.Resultado); else Limpar();
        AtualizarEstado();
        Mensagem = "Receitas sugeridas pelas notas fiscais: revise a segregação, ajuste e clique em Calcular.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private void AdicionarReceita()
    {
        Erros.Clear();
        var valor = Numeros.LerValor(NovoValor);
        if (valor is null or 0) { Erros.Add("Informe o valor da receita."); return; }
        var t = (NovaST ? TratamentoSimples.SubstituicaoTributaria : 0) | (NovoMonofasico ? TratamentoSimples.Monofasico : 0)
                | (NovoIssRetido ? TratamentoSimples.IssRetido : 0) | (NovaExportacao ? TratamentoSimples.Exportacao : 0);
        Receitas.Add(new ReceitaSimplesLinha(new ReceitaSimples(Atividades[Math.Max(0, NovaAtividade)], valor.Value, t)));
        NovoValor = "";
        Mensagem = "Receita incluída. Clique em Calcular e depois em Salvar apuração.";
    }

    [ObservableProperty] private ReceitaSimplesLinha? _receitaSelecionada;

    [RelayCommand]
    private void RemoverReceita()
    {
        if (ReceitaSelecionada is { } linha) Receitas.Remove(linha);
    }

    [RelayCommand]
    private void LimparReceitas() => Receitas.Clear();

    private (decimal? Rbt, decimal? Folha) Acumulados() =>
        (string.IsNullOrWhiteSpace(Rbt12Texto) ? null : Numeros.LerValor(Rbt12Texto), string.IsNullOrWhiteSpace(Folha12Texto) ? null : Numeros.LerValor(Folha12Texto));

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (rbt, folha) = Acumulados();
        var r = await app.Fiscal().Simples.CalcularAsync(empresa, Competencia(), ReceitasInformadas(), rbt, folha, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MostrarCalculo(r.Valor);
        Mensagem = $"DAS calculado: {Numeros.Moeda(r.Valor.ValorDas)}. Clique em Salvar para gravar a apuração.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (rbt, folha) = Acumulados();
        var r = await app.Fiscal().Simples.SalvarAsync(empresa, Competencia(), ReceitasInformadas(), rbt, folha, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (_apuracaoId, _provisaoContabilizada) = (r.Valor.Id, r.Valor.LancamentoId is not null);
        MostrarCalculo(r.Valor.Resultado);
        AtualizarEstado();
        Mensagem = $"Apuração de {r.Valor.Competencia:MM/yyyy} salva. DAS {Numeros.Moeda(r.Valor.ValorDas)}, vencimento {r.Valor.Vencimento:dd/MM/yyyy}.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task ContabilizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Provisoes.ContabilizarSimplesAsync(empresa, Competencia(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _provisaoContabilizada = true;
        AtualizarEstado();
        Mensagem = "Provisão do DAS contabilizada.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task DesfazerAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (_apuracaoId is not { } id) return;
        var r = await app.Fiscal().Provisoes.DesfazerAsync(empresa, TipoApuracao.Simples, id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _provisaoContabilizada = false;
        AtualizarEstado();
        Mensagem = "Provisão estornada.";
    }, Permissao.FiscalApurar);
}

public partial class LinhaBaseMensal(BaseMensal origem) : ObservableObject
{
    public DateOnly Competencia { get; } = origem.Competencia;
    public string Mes => Competencia.ToString("MM/yyyy");
    public string Origem { get; } = origem.OrigemReceita == "APURACAO" ? "Apuração" : origem.Receita is null && origem.Folha is null ? "" : "Manual";
    [ObservableProperty] private string _receitaTexto = origem.Receita is { } r ? Numeros.Moeda(r) : "";
    [ObservableProperty] private string _folhaTexto = origem.Folha is { } f ? Numeros.Moeda(f) : "";
    public string ReceitaOriginal { get; } = origem.Receita is { } r2 ? Numeros.Moeda(r2) : "";
    public string FolhaOriginal { get; } = origem.Folha is { } f2 ? Numeros.Moeda(f2) : "";
    public bool Alterada => ReceitaTexto.Trim() != ReceitaOriginal || FolhaTexto.Trim() != FolhaOriginal;
}

/// <summary>Apuração › Histórico de receita e folha: a base mensal do RBT12 e do Fator R (meses anteriores ao uso do sistema são digitados aqui).</summary>
public partial class BaseMensalViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<LinhaBaseMensal> Linhas { get; } = [];
    [ObservableProperty] private int _ano = DateTime.Today.Year;
    public bool PodeManter => Contexto.Pode(Permissao.FiscalRetencoesManter);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Simples.BaseMensalAsync(empresa, Ano, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Linhas.Clear();
        for (var m = 1; m <= 12; m++)
        {
            var comp = new DateOnly(Ano, m, 1);
            Linhas.Add(new LinhaBaseMensal(r.Valor.FirstOrDefault(b => b.Competencia == comp) ?? new BaseMensal(comp, null, null)));
        }
    });

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var n = 0;
        foreach (var l in Linhas.Where(l => l.Alterada))
        {
            decimal? Ler(string t) => string.IsNullOrWhiteSpace(t) ? null : Numeros.LerValor(t);
            var (rec, fol) = (Ler(l.ReceitaTexto), Ler(l.FolhaTexto));
            if ((!string.IsNullOrWhiteSpace(l.ReceitaTexto) && rec is null) || (!string.IsNullOrWhiteSpace(l.FolhaTexto) && fol is null)) { Erros.Add($"{l.Mes}: valor inválido."); continue; }
            var r = await app.Fiscal().Simples.GravarBaseMensalAsync(empresa, new BaseMensal(l.Competencia, rec, fol), usuario);
            if (!r.Sucesso) { foreach (var e in r.Erros) Erros.Add($"{l.Mes}: {e}"); continue; }
            n++;
        }
        if (Erros.Count == 0) { await CarregarAsync(); Mensagem = n == 0 ? "Nada foi alterado." : $"{n} mês(es) gravado(s)."; }
    }, Permissao.FiscalRetencoesManter);
}
