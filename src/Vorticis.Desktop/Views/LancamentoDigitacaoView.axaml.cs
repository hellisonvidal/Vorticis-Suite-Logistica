using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Desktop.ViewModels;
using Vorticis.Domain.Contabil;

namespace Vorticis.Desktop.Views;

/// <summary>
/// Digitação na vertical: um formulário mostra a partida selecionada (os campos um embaixo do outro) e a lista abaixo mostra
/// todas as partidas do lançamento. Pelo teclado: Enter avança de campo (pula a conta quando o reduzido já a identificou), seta para baixo
/// desce de linha e, na última, cria outra; seta para cima sobe e cancela a linha recém-criada ainda vazia;
/// Ctrl+Delete exclui a linha; Enter no último campo salva quando o lançamento fecha. Valor formatado ao sair do campo;
/// nome da conta resolvido ao sair do campo da conta.
/// </summary>
public partial class LancamentoDigitacaoView : UserControl
{
    private static readonly string[] CamposLinha =
        ["data", "reduzido", "conta", "reduzidocredito", "contacredito", "valor", "documento", "codhistorico", "historico", "centrocusto", "participante"];

    /// <summary>Sugestões de conta: reduzido, início do código ou parte do nome.</summary>
    public static AutoCompleteFilterPredicate<object?> FiltroConta { get; } =
        (texto, item) => item is ContaSelecionavel c && LancamentoViewModel.Combina(c, texto);

    /// <summary>"Cancelar (Esc)" na digitação; "Fechar (Esc)" na consulta.</summary>
    public static Avalonia.Data.Converters.IValueConverter RotuloFechar { get; } =
        new Avalonia.Data.Converters.FuncValueConverter<bool, string>(somenteLeitura => somenteLeitura ? "Fechar (Esc)" : "Cancelar (Esc)");

    private LancamentoViewModel? _vm;

    public LancamentoDigitacaoView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, AoTeclar, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, AoEntrarNoCampo, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, AoSairDoCampo, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) { _vm.Linhas.CollectionChanged -= AoMudarLinhas; _vm.FocoSolicitado -= Focar; }
            _vm = DataContext as LancamentoViewModel;
            if (_vm is not null) { _vm.Linhas.CollectionChanged += AoMudarLinhas; _vm.FocoSolicitado += Focar; }
        };
    }

    /// <summary>Ordem dos campos no Enter; o participante só existe no plano com participantes.</summary>
    private string[] Campos => _vm?.UsaParticipantes == true ? CamposLinha : CamposLinha[..^1];

    /// <summary>Linhas recomeçadas (abrir, Limpar, Salvar): foco no primeiro campo.</summary>
    private void AoMudarLinhas(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) Focar(0, Campos[0]);
    }

    /// <summary>Campo (TextBox ou AutoCompleteBox com Tag) que contém o elemento.</summary>
    private static (Control? Campo, string? Tag) CampoDe(object? origem)
    {
        for (var v = origem as Visual; v is not null; v = v.GetVisualParent())
            if (v is Control { Tag: string tag } c && (c is TextBox or AutoCompleteBox)) return (c, tag);
        return (null, null);
    }

    private void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        var (campo, tag) = CampoDe(e.Source);
        if (campo?.DataContext is not LinhaLancamentoViewModel linha || tag is null) return;
        var i = _vm.Linhas.IndexOf(linha);
        if (i < 0) return;
        var listaAberta = campo is AutoCompleteBox { IsDropDownOpen: true };

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && !listaAberta:
                e.Handled = true;
                ConcluirCampo(linha, tag, campo);
                Avancar(linha, i, tag);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.Shift && !listaAberta:      // Shift+Enter volta ao campo anterior
                e.Handled = true;
                var anterior = Array.IndexOf(Campos, tag) - 1;
                if (anterior >= 0) Focar(i, Campos[anterior]);
                else if (i > 0) Focar(i - 1, Campos[^1]);
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.None && !listaAberta:
                e.Handled = true;
                ConcluirCampo(linha, tag, campo);
                if (i == _vm.Linhas.Count - 1)
                {
                    _vm.AdicionarLinha();
                    Focar(i + 1, Campos[0]);
                }
                else Focar(i + 1, tag);
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.None && !listaAberta:
                e.Handled = true;
                if (i == 0) break;
                ConcluirCampo(linha, tag, campo);
                _vm.CancelarLinhaNova(linha);
                Focar(i - 1, tag);
                break;
            case Key.Delete when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                _vm.ExcluirLinhaCommand.Execute(null);
                Focar(Math.Min(i, _vm.Linhas.Count - 1), tag);
                break;
        }
    }

    /// <summary>Enter: próximo campo; no último, salva (se o lançamento fecha) ou vai à próxima linha (criada se preciso).</summary>
    private void Avancar(LinhaLancamentoViewModel linha, int i, string tag)
    {
        var campos = Campos;
        var p = Array.IndexOf(campos, tag) + 1;
        // Reduzido já identificou a conta: pula o campo da conta.
        if (tag == "reduzido" && linha.NomeConta.Length > 0 && linha.Reduzido.Trim().Length > 0) p++;
        if (tag == "reduzidocredito" && linha.NomeContaCredito.Length > 0 && linha.ReduzidoCredito.Trim().Length > 0) p++;
        if (p < campos.Length)
        {
            Focar(i, campos[p]);
            return;
        }
        if (_vm!.SalvarPeloEnter()) return;
        if (i == _vm.Linhas.Count - 1) _vm.AdicionarLinha();
        Focar(i + 1, campos[0]);
    }

    /// <summary>Seleciona a partida (o formulário passa a mostrá-la) e foca o campo.</summary>
    private void Focar(int linha, string tag)
    {
        if (_vm is null || linha < 0 || linha >= _vm.Linhas.Count) return;
        _vm.LinhaSelecionada = _vm.Linhas[linha];
        Dispatcher.UIThread.Post(() => FocarCampo(tag), DispatcherPriority.Loaded);
    }

    private void FocarCampo(string tag)
    {
        var alvo = Detalhes.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Tag as string == tag && (c is TextBox || c is AutoCompleteBox));
        if (alvo is AutoCompleteBox acb) alvo = acb.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() ?? alvo;
        alvo?.Focus(NavigationMethod.Tab);
        if (alvo is TextBox tb && tag != "historico") tb.SelectAll();
    }

    /// <summary>
    /// O que acontece ao sair de um campo (formatar o valor, resolver a conta, completar a data), feito já ao trocar de partida pelo
    /// teclado: no formulário único, o campo perde o foco quando ele já mostra a próxima partida.
    /// </summary>
    private void ConcluirCampo(LinhaLancamentoViewModel linha, string tag, Control campo)
    {
        if (_vm is null) return;
        switch (tag)
        {
            case "valor": linha.FormatarValor(); break;
            case "conta" when campo is AutoCompleteBox { IsDropDownOpen: false }: _vm.ConfirmarConta(linha, credito: false); break;
            case "contacredito" when campo is AutoCompleteBox { IsDropDownOpen: false }: _vm.ConfirmarConta(linha, credito: true); break;
            case "data" when linha.Data.Trim().Length > 0: linha.Data = _vm.DataCompleta(linha.Data); break;
        }
    }

    private void AoEntrarNoCampo(object? sender, GotFocusEventArgs e)
    {
        var (campo, tag) = CampoDe(e.Source);
        if (_vm is null || campo?.DataContext is not LinhaLancamentoViewModel linha) return;
        _vm.LinhaSelecionada = linha;
        // Histórico: seleciona a primeira variável {X} ainda sem preencher (digita-se por cima); senão, cursor no fim para completar.
        if (tag == "historico" && campo is TextBox tb)
            Dispatcher.UIThread.Post(() =>
            {
                if (VariaveisHistorico.PrimeiroPendente(tb.Text) is var (inicio, tamanho)) { tb.SelectionStart = inicio; tb.SelectionEnd = inicio + tamanho; }
                else { tb.ClearSelection(); tb.CaretIndex = tb.Text?.Length ?? 0; }
            });
    }

    private void AoSairDoCampo(object? sender, RoutedEventArgs e)
    {
        var (campo, tag) = CampoDe(e.Source);
        if (campo?.DataContext is LinhaLancamentoViewModel linha && tag is not null) ConcluirCampo(linha, tag, campo);
    }
}
