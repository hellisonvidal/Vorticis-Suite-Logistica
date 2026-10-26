using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Application.Cadastros;
using Vorticis.Desktop.ViewModels;
using Vorticis.Desktop.Views;

namespace Vorticis.Desktop.Tests;

/// <summary>Diálogo "Cadastrar esta empresa em quais módulos?" na janela real (headless).</summary>
public class ModulosDialogoTests(SessaoHeadless headless)
{
    [Fact]
    public async Task Dialogo_de_modulos_marca_o_atual_como_obrigatorio_e_devolve_a_escolha()
    {
        var ok = await Task.Run(() => headless.Sessao.Dispatch(async () =>
        {
            var vm = new MainViewModel();
            var janela = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            janela.Show();

            var tarefa = vm.EscolherModulosAsync(ModulosEmpresa.Todos);
            Dispatcher.UIThread.RunJobs();
            var dialogo = Assert.Single(janela.OwnedWindows);
            Assert.Equal("Cadastrar esta empresa em quais módulos?", dialogo.Title);

            var caixas = dialogo.GetVisualDescendants().OfType<CheckBox>().ToList();
            Assert.Equal(["Contábil", "Fiscal", "Folha de Pagamento", "Escritório", "Gerencial", "Condomínio", "Frota"], caixas.Select(c => c.Content as string));
            Assert.All(caixas, c => Assert.True(c.IsChecked));
            Assert.False(caixas[0].IsEnabled);            // módulo atual (Contábil): obrigatório
            Assert.True(caixas[1].IsEnabled);

            caixas[2].IsChecked = false;                  // desmarca a Folha
            vm.ResponderConfirmacaoCommand.Execute(true);
            Assert.Equal(ModulosEmpresa.Todos & ~ModulosEmpresa.Folha, await tarefa);

            // cancelar não cadastra
            var cancelada = vm.EscolherModulosAsync(ModulosEmpresa.Todos);
            vm.ResponderConfirmacaoCommand.Execute(false);
            Assert.Null(await cancelada);
            janela.Close();
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(ok);
    }
}
