using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Importacao;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Um campo do cadastro e a coluna da planilha escolhida para ele (o "de-para" do layout).</summary>
public sealed partial class MapeamentoCampo(CampoImportacao campo) : ObservableObject
{
    public CampoImportacao Campo { get; } = campo;
    public string Rotulo => Campo.Obrigatorio ? Campo.Rotulo + " *" : Campo.Rotulo;
    [ObservableProperty] private string? _coluna;
}

/// <summary>
/// Cadastros › Importar de planilha (Excel): escolhe o arquivo (.xlsx ou .csv), liga cada campo do cadastro a uma coluna da planilha
/// (o sistema sugere pelos nomes e lembra o layout gravado), mostra a prévia e importa linha a linha pelo serviço do cadastro.
/// </summary>
public partial class ImportacaoCadastrosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public const string NaoImportar = "(não importar)";
    private TabelaImportada? _tabela;

    public ObservableCollection<MapeamentoCampo> Mapeamentos { get; } = [];
    public ObservableCollection<string> Colunas { get; } = [NaoImportar];
    public ObservableCollection<string> Previa { get; } = [];
    [ObservableProperty] private DefinicaoImportacao? _definicao;
    [ObservableProperty] private string _titulo = "Importar de planilha";
    [ObservableProperty] private string _arquivo = "";
    [ObservableProperty] private string _resumo = "Escolha a planilha (Excel .xlsx ou .csv) com uma linha de cabeçalho.";

    public bool PodeImportar => _tabela is not null && Definicao is not null;

    /// <summary>Abre a tela para o cadastro escolhido no menu (limpa o arquivo anterior e lê o layout gravado da empresa).</summary>
    public async Task AbrirAsync(DefinicaoImportacao def)
    {
        if (Definicao?.Chave != def.Chave)
        {
            Definicao = def;
            Titulo = $"Importar de planilha: {def.Titulo}";
            _tabela = null;
            (Arquivo, Resumo) = ("", "Escolha a planilha (Excel .xlsx ou .csv) com uma linha de cabeçalho.");
            Colunas.Clear();
            Colunas.Add(NaoImportar);
            Previa.Clear();
            Mapeamentos.Clear();
            foreach (var c in def.Campos) Mapeamentos.Add(new MapeamentoCampo(c) { Coluna = NaoImportar });
            Erros.Clear();
            Mensagem = null;
            OnPropertyChanged(nameof(PodeImportar));
        }
        await AplicarLayoutGravadoAsync();
    }

    private async Task AplicarLayoutGravadoAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Definicao is not { } def) return;
        if (await new ServicoLayoutsPlanilha(app.DataSource).LerAsync(empresa, def.Chave) is not { } salvo) return;
        foreach (var m in Mapeamentos)
            if (salvo.TryGetValue(m.Campo.Chave, out var col))
            {
                if (!Colunas.Contains(col)) Colunas.Add(col);
                m.Coluna = col;
            }
    }

    private Dictionary<string, string> Mapa() =>
        Mapeamentos.Where(m => m.Coluna is { } c && c != NaoImportar).ToDictionary(m => m.Campo.Chave, m => m.Coluna!);

    /// <summary>Lê a planilha escolhida, oferece as colunas e sugere o de-para (o layout gravado tem preferência).</summary>
    public async Task CarregarArquivoAsync(string caminho)
    {
        Erros.Clear();
        Mensagem = null;
        if (Definicao is not { } def) { Erros.Add("Escolha o cadastro no menu Cadastros › Importar de planilha."); return; }
        var r = LeitorPlanilha.Ler(caminho);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _tabela = r.Valor;
        Arquivo = caminho;
        Colunas.Clear();
        Colunas.Add(NaoImportar);
        foreach (var c in _tabela.Colunas) Colunas.Add(c);
        var sugestao = ImportacaoPlanilha.Sugerir(_tabela, def.Campos);
        foreach (var m in Mapeamentos) m.Coluna = sugestao.TryGetValue(m.Campo.Chave, out var c) ? c : NaoImportar;
        await AplicarLayoutGravadoAsync();
        foreach (var m in Mapeamentos.Where(m => m.Coluna is { } c && !_tabela.Colunas.Contains(c))) m.Coluna = NaoImportar;   // coluna do layout que não está neste arquivo
        AtualizarPrevia();
        OnPropertyChanged(nameof(PodeImportar));
    }

    [RelayCommand]
    private void AtualizarPrevia()
    {
        Previa.Clear();
        if (_tabela is null || Definicao is not { } def) return;
        var linhas = ImportacaoPlanilha.Linhas(_tabela, Mapa());
        foreach (var l in linhas.Take(8))
            Previa.Add($"Linha {l.Numero}: " + string.Join(" · ", def.Campos.Where(c => l.Texto(c.Chave) is not null).Select(c => $"{c.Rotulo}: {l.Texto(c.Chave)}")));
        var faltam = ImportacaoPlanilha.Faltam(def.Campos, Mapa());
        Resumo = $"{System.IO.Path.GetFileName(Arquivo)}: {linhas.Count} linha(s) com dados." +
                 (faltam.Count > 0 ? $" Falta escolher a coluna de: {string.Join(", ", faltam)}." : " Confira a prévia e clique em Importar.");
    }

    [RelayCommand]
    private Task SalvarLayoutAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Definicao is not { } def) return;
        await new ServicoLayoutsPlanilha(app.DataSource).GravarAsync(empresa, def.Chave, Mapa());
        Mensagem = "Layout gravado: na próxima planilha deste cadastro as colunas já vêm escolhidas.";
    });

    [RelayCommand]
    private void GerarModelo()
    {
        Erros.Clear();
        if (Definicao is not { } def) return;
        try
        {
            var arq = ArquivosGerados.Salvar($"modelo_{def.Chave}.xlsx", LeitorPlanilha.Modelo([.. def.Campos.Select(c => c.Rotulo)], [.. def.Campos.Select(c => c.Exemplo)]));
            Mensagem = $"Planilha modelo salva em: {arq}. Preencha uma linha por cadastro (os campos com * são obrigatórios) e importe.";
        }
        catch (Exception ex) { Erros.Add($"Não foi possível gerar a planilha: {ex.Message}"); }
    }

    [RelayCommand]
    private Task ImportarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (_tabela is null || Definicao is not { } def) { Erros.Add("Escolha a planilha primeiro."); return; }
        if (ImportacaoPlanilha.Faltam(def.Campos, Mapa()) is { Count: > 0 } faltam) { Erros.Add($"Escolha a coluna de: {string.Join(", ", faltam)}."); return; }
        var linhas = ImportacaoPlanilha.Linhas(_tabela, Mapa());
        if (!await Contexto.ConfirmarAsync("Importar planilha", $"Importar {linhas.Count} linha(s) para {def.Titulo}?")) return;
        var ctx = new ContextoImportacao(app, empresa, usuario);
        var (ok, falhas) = (0, new List<string>());
        foreach (var l in linhas)
        {
            Resultado r;
            try { r = await def.Gravar(ctx, l); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { r = Resultado.Falha(ex.Message); }
            if (r.Sucesso) ok++; else falhas.Add($"Linha {l.Numero}: {string.Join(" ", r.Erros)}");
        }
        await new ServicoLayoutsPlanilha(app.DataSource).GravarAsync(empresa, def.Chave, Mapa());   // o layout usado fica gravado
        foreach (var f in falhas.Take(50)) Erros.Add(f);
        if (falhas.Count > 50) Erros.Add($"... e mais {falhas.Count - 50} linha(s) com erro.");
        Mensagem = $"{ok} de {linhas.Count} linha(s) importada(s) para {def.Titulo}." + (falhas.Count > 0 ? $" {falhas.Count} linha(s) com erro ficaram de fora (veja abaixo)." : "");
    });
}
