using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vorticis.Application.Contabil;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Contabil;

namespace Vorticis.Desktop.Comum.Views;

/// <summary>
/// Janela de consulta do plano de contas para escolher uma conta analítica (campos de conta contábil dos cadastros, como as regras fiscais).
/// Busca por código, reduzido ou nome; <b>Incluir conta</b> cadastra uma conta nova no plano (com a permissão de manter o plano) e já a escolhe.
/// </summary>
public static class SeletorContaContabil
{
    /// <summary>Linha da lista: a conta e o texto mostrado.</summary>
    public sealed record ContaLista(ContaDetalhe Conta)
    {
        public override string ToString() =>
            $"{(Conta.CodigoReduzido is { } r ? $"{r,5}  " : "       ")}{Conta.Codigo}  {Conta.Descricao}{(Conta.Analitica ? "" : "  (sintética)")}";
    }

    /// <summary>Contas que casam com o filtro (código, reduzido ou parte do nome, sem diferenciar maiúsculas).</summary>
    public static IEnumerable<ContaDetalhe> Filtrar(IEnumerable<ContaDetalhe> contas, string filtro)
    {
        var f = filtro.Trim();
        return f.Length == 0 ? contas : contas.Where(c => c.Codigo.StartsWith(f, StringComparison.Ordinal) || c.CodigoReduzido?.ToString() == f
                                                          || c.Descricao.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Abre a consulta e devolve a conta escolhida (código e descrição), ou null se o usuário desistir.</summary>
    public static async Task<(string Codigo, string Descricao)?> EscolherAsync(Window dono, IContexto contexto, string? atual = null)
    {
        if (contexto.App is not { } app || contexto.EmpresaId is not { } empresa || contexto.Sessao is not { } sessao) return null;
        var contas = (await app.PlanoContas.ListarAsync(empresa, sessao.Id)).Where(c => c.Ativa).OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList();
        (string, string)? escolha = null;

        var filtro = new TextBox { Watermark = "Buscar: código, reduzido ou parte do nome", Text = "" };
        var lista = new ListBox { FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12.5, Height = 360 };
        var aviso = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#B42318")), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        void Atualizar() => lista.ItemsSource = Filtrar(contas, filtro.Text ?? "").Select(c => new ContaLista(c)).ToList();
        filtro.TextChanged += (_, _) => Atualizar();
        Atualizar();
        if (atual is { Length: > 0 } && contas.FirstOrDefault(c => c.Codigo == atual) is { } c0)
            lista.SelectedItem = ((IEnumerable<ContaLista>)lista.ItemsSource!).FirstOrDefault(l => l.Conta.Id == c0.Id);

        // conta nova, embaixo da conta sintética selecionada (ou com o código digitado)
        var novoCodigo = new TextBox { Width = 150, Watermark = "Código (ex.: 1.1.04.009)" };
        var novaDescricao = new TextBox { Width = 300, Watermark = "Descrição da conta nova" };
        var incluir = new Button { Content = "Incluir conta" };
        lista.SelectionChanged += (_, _) =>
        {
            if (lista.SelectedItem is ContaLista { Conta.Analitica: false } s && string.IsNullOrWhiteSpace(novoCodigo.Text))
                novoCodigo.Text = s.Conta.Codigo + ".";
        };

        var usar = new Button { Content = "Usar esta conta", MinWidth = 120 };
        usar.Classes.Add("accent");
        var cancelar = new Button { Content = "Cancelar", MinWidth = 90 };
        var janela = new Window
        {
            Title = "Plano de contas – escolher a conta",
            Width = 720, SizeToContent = SizeToContent.Height, CanResize = true, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = dono.Icon,
            Content = new Border
            {
                Padding = new Thickness(14), Background = Brushes.White,
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Escolha uma conta analítica (as sintéticas só agrupam e não recebem lançamentos).", TextWrapping = TextWrapping.Wrap },
                        filtro, lista,
                        new TextBlock { Text = "Não encontrou? Inclua a conta no plano:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { novoCodigo, novaDescricao, incluir } },
                        aviso,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { usar, cancelar } },
                    },
                },
            },
        };

        void Usar()
        {
            if (lista.SelectedItem is not ContaLista l) { aviso.Text = "Selecione uma conta na lista."; return; }
            if (!l.Conta.Analitica) { aviso.Text = $"A conta {l.Conta.Codigo} é sintética: escolha uma conta analítica, ou inclua uma conta nova embaixo dela."; return; }
            escolha = (l.Conta.Codigo, l.Conta.Descricao);
            janela.Close();
        }
        usar.Click += (_, _) => Usar();
        lista.DoubleTapped += (_, _) => Usar();
        cancelar.Click += (_, _) => janela.Close();
        janela.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { janela.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && lista.IsFocused) { Usar(); e.Handled = true; }
        };
        incluir.Click += async (_, _) =>
        {
            aviso.Text = "";
            var codigo = (novoCodigo.Text ?? "").Trim().TrimEnd('.');
            var pai = contas.Where(c => !c.Analitica && codigo.StartsWith(c.Codigo + ".", StringComparison.Ordinal)).MaxBy(c => c.Codigo.Length);
            var r = await app.PlanoContas.IncluirAsync(empresa, new NovaConta(codigo, (novaDescricao.Text ?? "").Trim(), pai?.CodNat ?? "04",
                pai?.NaturezaSaldo ?? IndicadorDC.D, false, false, DateOnly.FromDateTime(DateTime.Today)), sessao.Id);
            if (!r.Sucesso) { aviso.Text = string.Join(" ", r.Erros); return; }
            escolha = (codigo, (novaDescricao.Text ?? "").Trim().ToUpperInvariant());
            janela.Close();
        };
        await janela.ShowDialog(dono);
        return escolha;
    }
}
