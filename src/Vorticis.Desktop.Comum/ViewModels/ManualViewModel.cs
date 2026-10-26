using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.Ajuda;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Linha do índice do manual (arquivo ou seção), com recuo conforme o nível.</summary>
public sealed record ItemIndice(TopicoManual Topico, string Rotulo, double Recuo)
{
    public bool EhArquivo => Topico.Nivel == 1;
    public Thickness Margem => new(Recuo, 0, 0, 0);
    public FontWeight Peso => EhArquivo ? FontWeight.Bold : FontWeight.Normal;
}

/// <summary>
/// Ajuda › Manual do usuário: índice à esquerda (módulo atual e comuns, com opção de ver todos), busca por palavra e o
/// conteúdo à direita. F1 abre o tópico da tela em uso (mapa em docs/manual/mapa.txt).
/// </summary>
public sealed partial class ManualViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    private bool _montando;

    public ObservableCollection<ItemIndice> Itens { get; } = [];

    [ObservableProperty] private bool _todosModulos;
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private ItemIndice? _selecionado;
    [ObservableProperty] private TopicoManual? _topico;

    /// <summary>Nome curto do módulo em uso (filtra o índice).</summary>
    public string ModuloAtual => main.Modulo.NomeCurto;

    public string Resumo => Busca.Trim().Length > 0
        ? $"{Itens.Count} resultado(s) para \"{Busca.Trim()}\""
        : TodosModulos ? "Todos os módulos" : $"Módulo {ModuloAtual} e assuntos comuns";

    /// <summary>Substituível nos testes: abre o arquivo HTML gerado no navegador padrão.</summary>
    public Action<string> AbrirArquivo { get; set; } = caminho =>
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });

    /// <summary>Caminho do último HTML gerado em "Abrir no navegador".</summary>
    public string? UltimoHtml { get; private set; }

    /// <summary>Monta o índice; na primeira vez mostra a página inicial do manual.</summary>
    public void Preparar()
    {
        Reconstruir();
        if (Topico is null) IrPara(ManualUsuario.Arquivos[0].Topicos[0]);
    }

    /// <summary>F1: abre o tópico da tela de chave dada; sem tópico, mantém o que está aberto.</summary>
    public void IrParaTela(string? chave)
    {
        Preparar();
        if (ManualUsuario.TopicoDaTela(chave) is not { } t) return;
        if (Busca.Length > 0) Busca = "";
        var visiveis = ManualUsuario.Visiveis(main.Modulo.AreasDoManual, TodosModulos);
        if (!visiveis.Any(a => a.Nome == t.Arquivo)) TodosModulos = true;
        IrPara(t);
    }

    public void IrPara(TopicoManual t)
    {
        Topico = t;
        _montando = true;
        Selecionado = Itens.FirstOrDefault(i => i.Topico == t);
        _montando = false;
    }

    /// <summary>Link do conteúdo: tópico interno ou endereço externo.</summary>
    [RelayCommand]
    public void Navegar(string? destino)
    {
        if (string.IsNullOrWhiteSpace(destino)) return;
        if (destino.Contains("://")) { AbrirArquivo(destino); return; }
        if (ManualUsuario.Resolver(destino, Topico?.Arquivo) is { } t)
        {
            if (!ManualUsuario.Visiveis(main.Modulo.AreasDoManual, TodosModulos).Any(a => a.Nome == t.Arquivo)) TodosModulos = true;
            IrPara(t);
        }
    }

    /// <summary>Gera um HTML temporário do manual (arquivos visíveis) e o abre no navegador, no tópico atual.</summary>
    [RelayCommand]
    public void AbrirNoNavegador()
    {
        try
        {
            var pasta = Path.Combine(Path.GetTempPath(), "Vorticis-Manual");
            Directory.CreateDirectory(pasta);
            var caminho = Path.Combine(pasta, "manual.html");
            File.WriteAllText(caminho, ManualUsuario.GerarHtml(ManualUsuario.Visiveis(main.Modulo.AreasDoManual, TodosModulos)), new System.Text.UTF8Encoding(false));
            UltimoHtml = caminho;
            var alvo = Topico is { } t ? "#" + ManualUsuario.IdHtml(t.Arquivo, t.Ancora) : "";
            AbrirArquivo(new Uri(caminho).AbsoluteUri + alvo);
        }
        catch (Exception ex)
        {
            Erros.Clear();
            Erros.Add("Não foi possível abrir o manual no navegador: " + ex.Message);
        }
    }

    partial void OnTodosModulosChanged(bool value) => Reconstruir();
    partial void OnBuscaChanged(string value) => Reconstruir();

    partial void OnSelecionadoChanged(ItemIndice? value)
    {
        if (!_montando && value is not null) Topico = value.Topico;
    }

    private void Reconstruir()
    {
        var atual = Topico;
        _montando = true;
        Itens.Clear();
        var arquivos = ManualUsuario.Visiveis(main.Modulo.AreasDoManual, TodosModulos).ToList();
        if (Busca.Trim().Length > 0)
            foreach (var t in ManualUsuario.Buscar(Busca, arquivos))
                Itens.Add(new ItemIndice(t, $"{t.Titulo}  ({ManualUsuario.Arquivo(t.Arquivo)?.Titulo})", 0));
        else
            foreach (var a in arquivos)
                foreach (var t in a.Topicos)
                    Itens.Add(new ItemIndice(t, t.Titulo, t.Nivel == 1 ? 0 : 14));
        Selecionado = atual is null ? null : Itens.FirstOrDefault(i => i.Topico == atual);
        _montando = false;
        OnPropertyChanged(nameof(Resumo));
    }
}
