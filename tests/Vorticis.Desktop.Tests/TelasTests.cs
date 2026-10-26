using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;
using Vorticis.Desktop.ViewModels;
using Vorticis.Desktop.Views;
using Vorticis.Infra.Cnpj;
using Vorticis.Infra.Tests;

[assembly: AssemblyFixture(typeof(Vorticis.Desktop.Tests.SessaoHeadless))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Vorticis.Desktop.Tests;

public static class AppDeTeste
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// O Avalonia admite uma única aplicação por processo: todas as classes de teste usam a mesma sessão headless
/// (fixture do assembly) e rodam em sequência.
/// </summary>
public sealed class SessaoHeadless : IDisposable
{
    public HeadlessUnitTestSession Sessao { get; } = HeadlessUnitTestSession.StartNew(typeof(AppDeTeste));

    public void Dispose() => Sessao.Dispose();
}

/// <summary>
/// Fluxo completo pela camada de apresentação (ViewModels + janela real renderizada sem tela).
/// Se VORTICIS_SCREENSHOTS apontar para uma pasta, salva PNGs das telas.
/// </summary>
public class TelasTests(BancoDeTeste db, SessaoHeadless headless) : IClassFixture<BancoDeTeste>
{
    private static readonly string? Pasta = Environment.GetEnvironmentVariable("VORTICIS_SCREENSHOTS");

    private static void Foto(Window janelaPrincipal, string nome)
    {
        // Com uma função aberta, fotografa a janela flutuante dela; senão, a janela principal.
        var janela = (janelaPrincipal as MainWindow)?.JanelaAtiva ?? janelaPrincipal;
        // Alguns controles (ex.: CalendarDatePicker) atualizam o texto em uma passada posterior do dispatcher.
        Dispatcher.UIThread.RunJobs();
        janela.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        var quadro = janela.CaptureRenderedFrame();
        Assert.NotNull(quadro);
        if (Pasta is not null) quadro.Save(Path.Combine(Pasta, nome + ".png"));
    }

    /// <summary>
    /// Garante que o restante do teste (inclusive o Dispose da sessão) não rode na thread da sessão headless:
    /// se uma asserção falhar lá dentro, a continuação síncrona chamaria Dispose na própria thread e travaria,
    /// escondendo a falha real.
    /// </summary>
    /// <summary>Aguarda o comando terminar respondendo "Sim" a confirmações adicionais que ele pedir.</summary>
    private static async Task ConcluirConfirmando(MainViewModel vm, Task comando)
    {
        for (var i = 0; i < 3000 && !comando.IsCompleted; i++)
        {
            if (vm.Confirmacao is not null) vm.ResponderConfirmacaoCommand.Execute(true);
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        await comando;
    }

    /// <summary>Espera o diálogo de confirmação (Sim/Não) aparecer.</summary>
    private static async Task<PedidoConfirmacao> AguardarConfirmacao(MainViewModel vm)
    {
        for (var i = 0; i < 200 && vm.Confirmacao is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        return vm.Confirmacao ?? throw new Xunit.Sdk.XunitException("A confirmação não foi pedida.");
    }

    private static async Task<T> ForaDaSessao<T>(Task<T> tarefa)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tarefa.ContinueWith(t =>
        {
            if (t.IsFaulted) tcs.SetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled) tcs.SetCanceled();
            else tcs.SetResult(t.Result);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return await tcs.Task;
    }

    [Fact]
    public async Task Fluxo_completo_com_perfil_personalizado_definido_pelo_administrador()
    {
        BancoDeTeste.ExigirBanco();
        var sessao = headless.Sessao;
        // Retornar um valor força a sobrecarga Dispatch<T>(Func<Task<T>>), que aguarda o corpo assíncrono;
        // com Func<Task> a sobrecarga escolhida devolveria Task<Task> e as asserções não seriam aguardadas.
        var concluido = await ForaDaSessao(sessao.Dispatch(async () =>
        {
            // Consulta de CNPJ com o parser real e resposta HTTP simulada (os testes não acessam a internet).
            var http = ConsultaCnpjTests.HttpCnpjEInscricoes();
            var vm = new MainViewModel();
            await vm.InicializarAsync(db.ConnectionString, new ConsultaCnpjPublica(new HttpClient(http)),
                                      new ConsultaInscricaoEstadualPublica(new HttpClient(http)));
            var janela = new MainWindow { DataContext = vm, Width = 1280, Height = 780 };
            janela.Show();

            Assert.Equal(WindowState.Maximized, janela.WindowState);   // inicia sempre maximizada, já no login

            // 1. Instalação nova: só entra o usuário de implantação (vorticis/vorticis) e o único caminho é cadastrar o administrador
            var login = Assert.IsType<LoginViewModel>(vm.Conteudo);
            Assert.True(login.ImplantacaoAtiva);
            Foto(janela, "01_implantacao");
            (login.Usuario, login.Senha) = ("maria", "Senha1234");
            await login.EntrarCommand.ExecuteAsync(null);
            Assert.False(vm.Logado);                                    // outro usuário/senha não entra
            Assert.NotEmpty(login.Erros);
            (login.Usuario, login.Senha) = ("vorticis", "outra");
            await login.EntrarCommand.ExecuteAsync(null);
            Assert.False(vm.Logado);
            LoginSalvo.Esquecer();
            (login.Usuario, login.Senha, login.SalvarLogin) = ("vorticis", "vorticis", true);
            await login.EntrarCommand.ExecuteAsync(null);
            Assert.True(vm.Logado, string.Join("; ", login.Erros));
            Assert.True(vm.EmImplantacao);
            Assert.Null(LoginSalvo.Obter());                            // o acesso de implantação nunca fica salvo
            Assert.Equal(WindowState.Maximized, janela.WindowState);

            // 1a. Abre sozinho o cadastro de usuários, só para incluir o administrador; o resto fica bloqueado
            Assert.Same(vm.CentralUsuarios, vm.Conteudo);
            for (var i = 0; i < 100 && !vm.Usuarios.EmInclusao; i++) await Task.Delay(50);   // carga em segundo plano
            Assert.True(vm.Usuarios.EmInclusao);
            Assert.Equal(["Administrador"], vm.Usuarios.PerfisDisponiveis.Select(p => p.Nome));
            Assert.True(vm.Usuarios.PerfilEdicao?.Administrador);
            Assert.False(vm.Usuarios.MostrarAcessos);                   // administrador acessa todas as empresas
            foreach (var bloqueada in new[] { Telas.Empresas, Telas.Lancamento, Telas.Inicio, Telas.Backup, TelasComuns.Perfis, TelasComuns.Certificados })
                Assert.False(vm.AbrirCommand.CanExecute(bloqueada), bloqueada);
            Assert.False(vm.Usuarios.AlterarCommand.CanExecute(null));
            Assert.False(vm.Usuarios.ExcluirCommand.CanExecute(null));
            Foto(janela, "01a_implantacao_cadastro_administrador");
            (vm.Usuarios.Login, vm.Usuarios.NomeEdicao, vm.Usuarios.NovaSenha) = ("vorticis", "Maria Contadora", "Senha1234");
            await vm.Usuarios.SalvarCommand.ExecuteAsync(null);
            Assert.Contains(vm.Usuarios.Erros, e => e.Contains("reservado"));   // login da implantação não serve para o administrador
            vm.Usuarios.Login = "maria";
            await vm.Usuarios.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(vm.Usuarios.Erros);

            // 1b. O acesso de implantação termina: volta ao login (sem aviso de implantação) com o novo administrador
            Assert.False(vm.Logado);
            Assert.Same(vm.Login, vm.Conteudo);
            Assert.Equal("maria", vm.Login.Usuario);
            Assert.False(vm.Login.ImplantacaoAtiva);
            (vm.Login.Usuario, vm.Login.Senha) = ("vorticis", "vorticis");
            await vm.Login.EntrarCommand.ExecuteAsync(null);
            Assert.False(vm.Logado);
            (login.Usuario, login.Senha) = ("maria", "Senha1234");
            LoginSalvo.Esquecer();
            login.SalvarLogin = true;                                   // "Salvar login neste computador"
            await login.EntrarCommand.ExecuteAsync(null);
            Assert.True(vm.Logado, string.Join("; ", login.Erros));
            Assert.False(vm.EmImplantacao);
            Assert.Equal(WindowState.Maximized, janela.WindowState);   // continua maximizada após o login
            Assert.Equal("maria", LoginSalvo.Obter()?.Usuario);

            // 1c. Configuração inicial obrigatória: perfis de acesso (o cadastro de empresas não é exigido)
            Assert.Same(vm.CentralUsuarios, vm.Conteudo);
            Assert.Equal(CentralUsuariosViewModel.AbaPerfis, vm.CentralUsuarios.AbaSelecionada);
            Assert.True(vm.Perfis.ConfiguracaoInicial);
            Assert.False(vm.CentralUsuarios.PodeAdministrar);           // aba Usuários só depois de concluir os perfis
            foreach (var bloqueada in new[] { Telas.Empresas, Telas.Lancamento, Telas.Inicio, Telas.Backup })
                Assert.False(vm.AbrirCommand.CanExecute(bloqueada), bloqueada);
            await vm.Perfis.CarregarAsync();
            Foto(janela, "01b_configuracao_inicial_perfis");
            await vm.Perfis.ConcluirConfiguracaoInicialCommand.ExecuteAsync(null);
            Assert.Empty(vm.Perfis.Erros);
            Assert.False(vm.Perfis.ConfiguracaoInicial);

            // 1c. Tela de descanso, sem obrigar o cadastro de empresa
            Assert.Same(vm.Inicio, vm.Conteudo);
            Assert.Contains("Cadastros › Empresas", vm.Inicio.Dica);
            Foto(janela, "01c_tela_inicial");
            Assert.False(vm.AbrirCommand.CanExecute(Telas.Lancamento));
            Assert.True(vm.AbrirCommand.CanExecute(Telas.Empresas));
            vm.AbrirCommand.Execute(Telas.Empresas);

            // 2. Cadastros › Empresas: Incluir, busca na base pública do CNPJ e a inscrição estadual (com confirmação), Salvar
            var emp = vm.Empresas;
            Assert.True(emp.EmConsulta);
            Assert.False(emp.AlterarCommand.CanExecute(null));                // nada selecionado
            emp.IncluirCommand.Execute(null);
            Assert.True(emp.EmInclusao);
            emp.Cnpj = "11.222.333/0001-81";
            var consultaCnpj = emp.ConsultarCnpjCommand.ExecuteAsync(null);
            var pergunta = await AguardarConfirmacao(vm);                      // IE encontrada: usa?
            Assert.Contains("SP: 110042490114 (ativa)", pergunta.Mensagem);
            Foto(janela, "02a_confirma_inscricao_estadual");
            vm.ResponderConfirmacaoCommand.Execute(true);
            await consultaCnpj;
            Assert.Empty(emp.Erros);
            Assert.Equal("110042490114", emp.InscricaoEstadual);
            Assert.Equal("EXEMPLO COMERCIO DE INFORMATICA LTDA", emp.RazaoSocial);
            Assert.Equal(("4751201", "3550308", "AVENIDA PAULISTA"), (emp.Cnae, emp.MunicipioIbge, emp.Logradouro));
            Assert.Equal(RegimeTributario.SimplesNacional, emp.Regime);
            Assert.Equal(new DateTime(2015, 3, 2), emp.InicioAtividades);
            emp.AnoExercicio = 2026;
            Foto(janela, "02_empresa_dados_receita");
            Assert.NotNull(janela.JanelaAtiva);                               // a função abriu em janela flutuante
            var picker = janela.JanelaAtiva!.GetVisualDescendants().OfType<CalendarDatePicker>().Single();
            Assert.Equal((new DateTime(2015, 3, 2), (DateTime?)new DateTime(2015, 3, 2), "02/03/2015"), (emp.InicioAtividades!.Value, picker.SelectedDate, picker.Text));
            var salvandoEmpresa = emp.SalvarCommand.ExecuteAsync(null);
            var modulos = await AguardarConfirmacao(vm);                      // cadastrar em quais módulos?
            Assert.Equal("Cadastrar esta empresa em quais módulos?", modulos.Titulo);
            Assert.Equal(["Contábil", "Fiscal", "Folha de Pagamento", "Escritório", "Gerencial", "Condomínio", "Frota"], modulos.Opcoes.Select(o => o.Texto));
            Assert.All(modulos.Opcoes.Take(5), o => Assert.True(o.Marcada));
            Assert.False(modulos.Opcoes[5].Marcada);                          // Condomínio só se incluída pelo próprio módulo
            Assert.False(modulos.Opcoes[6].Marcada);                          // Frota (piloto) também
            modulos.Opcoes[2].Marcada = false;                                // sem Folha
            vm.ResponderConfirmacaoCommand.Execute(true);
            await salvandoEmpresa;
            Assert.Empty(emp.Erros);
            Assert.True(emp.EmConsulta);
            Assert.False(emp.ModuloFolha);
            var empresa = Assert.Single(vm.EmpresasPermitidas);
            Assert.True(await db.EscalarAsync<bool>(
                """
                SELECT fonte_dados LIKE 'BrasilAPI%' AND cep = '01310100' AND situacao_cadastral = 'ATIVA'
                       AND data_inicio_ativ = '2015-03-02' AND cnae_principal = '4751201' AND inscricao_estadual = '110042490114'
                  FROM core.empresa WHERE id = @id
                """,
                ("id", empresa.Id)));

            // Alterar: telefone
            await emp.CarregarSelecionadaAsync();
            Assert.Equal("EXEMPLO COMERCIO DE INFORMATICA LTDA", emp.RazaoSocial);
            emp.AlterarCommand.Execute(null);
            Assert.True(emp.EmAlteracao);
            emp.Telefone = "1144445555";
            await emp.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(emp.Erros);
            Assert.Equal("1144445555", await db.EscalarAsync<string>("SELECT telefone FROM core.empresa WHERE id = @id", ("id", empresa.Id)));
            Foto(janela, "02b_empresa_consulta");

            // Em consulta, a busca da IE pergunta se deve salvar; "Não salvar" mantém o cadastro
            var buscaIe = emp.BuscarInscricaoEstadualCommand.ExecuteAsync(null);
            Assert.Contains("Deseja salvar", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(false);
            await buscaIe;
            Assert.Equal("Inscrição estadual não alterada.", emp.Mensagem);

            // Excluir (com confirmação) uma segunda empresa
            emp.IncluirCommand.Execute(null);
            (emp.Cnpj, emp.RazaoSocial, emp.Cnae, emp.MunicipioIbge, emp.Uf) = ("06.990.590/0001-23", "EMPRESA A EXCLUIR LTDA", "6920601", "3550308", "SP");
            emp.ImportarPlanoModelo = false;
            await ConcluirConfirmando(vm, emp.SalvarCommand.ExecuteAsync(null));         // aceita os módulos sugeridos (todos)
            Assert.Empty(emp.Erros);
            Assert.Equal(2, vm.EmpresasPermitidas.Count);
            vm.EmpresaSelecionada = vm.EmpresasPermitidas.Single(e => e.RazaoSocial == "EMPRESA A EXCLUIR LTDA");
            await emp.CarregarSelecionadaAsync();
            var exclusao = emp.ExcluirCommand.ExecuteAsync(null);
            var aviso = await AguardarConfirmacao(vm);
            Assert.Contains("EMPRESA A EXCLUIR LTDA", aviso.Mensagem);
            Assert.Contains("TODOS os seus dados serão apagados", aviso.Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await ConcluirConfirmando(vm, exclusao);                         // (sem pg_dump, confirma excluir sem backup)
            Assert.Contains("excluída do banco de dados", emp.Mensagem);
            Assert.Empty(emp.Erros);
            Assert.Equal(empresa.Id, Assert.Single(vm.EmpresasPermitidas).Id);
            vm.EmpresaSelecionada = vm.EmpresasPermitidas[0];

            // Cadastros › Históricos: texto fixo com código sequencial
            vm.AbrirCommand.Execute(Telas.Historicos);
            var hist = vm.Historicos;
            await hist.CarregarAsync();
            hist.IncluirCommand.Execute(null);
            Assert.Equal("1", hist.Codigo);
            hist.Descricao = "Venda à vista NF";
            await hist.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(hist.Erros);
            Assert.Equal("1", Assert.Single(hist.Lista).Codigo);
            hist.IncluirCommand.Execute(null);                                  // histórico com variáveis de preenchimento automático
            Assert.Equal("2", hist.Codigo);
            hist.Descricao = "Pgto {DOCUMENTO} ref. {MES_ANTERIOR} {COMPLEMENTO}";
            await hist.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(hist.Erros);
            hist.IncluirCommand.Execute(null);
            hist.Descricao = "Pgto {INEXISTENTE}";
            await hist.SalvarCommand.ExecuteAsync(null);
            Assert.Contains("Variável desconhecida", Assert.Single(hist.Erros));
            hist.CancelarCommand.Execute(null);
            Assert.Contains(hist.Variaveis, v => v.Marcador == "{DOCUMENTO}");

            // Cadastros › Clientes (dados da Receita pelo CNPJ, como no cadastro de empresas) e › Fornecedores
            vm.AbrirCommand.Execute(Telas.Clientes);
            var cli = vm.Clientes;
            await cli.CarregarAsync();
            cli.IncluirCommand.Execute(null);
            Assert.Equal("1", cli.Codigo);
            cli.CpfCnpj = "11222333000181";
            await cli.BuscarCnpjCommand.ExecuteAsync(null);
            Assert.Empty(cli.Erros);
            Assert.Equal(("EXEMPLO COMERCIO DE INFORMATICA LTDA", "SP", "3550308", "110042490114"), (cli.Nome, cli.Uf, cli.MunicipioIbge, cli.InscricaoEstadual));
            await cli.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(cli.Erros);
            Assert.Equal("Cliente", Assert.Single(cli.Lista).Tipo);
            Assert.Equal("Cód. 1", cli.NumeroRegistro);
            Assert.False(string.IsNullOrEmpty(cli.Logradouro));                // endereço completo, como no cadastro de empresas
            Assert.Matches(@"^\d{5}-\d{3}$", cli.Cep);
            Foto(janela, "02c_clientes");
            vm.AbrirCommand.Execute(Telas.Fornecedores);
            var forn = vm.Fornecedores;
            await forn.CarregarAsync();
            Assert.Empty(forn.Lista);
            forn.IncluirCommand.Execute(null);
            Assert.Equal((false, true), (forn.Cliente, forn.Fornecedor));
            (forn.Nome, forn.Uf, forn.Municipio, forn.Cep, forn.Logradouro) = ("Distribuidora Beta", "SP", "Campinas", "13015-904", "Rua Barão de Jaguara");
            await forn.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(forn.Erros);
            Assert.Equal(("2", "Fornecedor"), (Assert.Single(forn.Lista).Codigo, forn.Lista[0].Tipo));
            Assert.Equal(("13015904", "Rua Barão de Jaguara"), (forn.Lista[0].P.Cep, forn.Lista[0].P.Logradouro));
            Foto(janela, "02c_fornecedores");

            // Imobilizado: regras (grupos da RFB), cadastro do bem e cálculo da depreciação
            vm.AbrirCommand.Execute(Telas.GruposDepreciacao);
            await vm.GruposDepreciacao.CarregarAsync();
            Assert.Contains(vm.GruposDepreciacao.Lista, g => g.Descricao == "Móveis e utensílios" && g.Taxa == "10%" && g.VidaUtil == "10 anos");
            Foto(janela, "02d_regras_depreciacao");
            vm.AbrirCommand.Execute(Telas.Bens);
            var bens = vm.Bens;
            await bens.CarregarAsync();
            bens.IncluirCommand.Execute(null);
            Assert.Equal("1", bens.Codigo);
            bens.Descricao = "Notebook do financeiro";
            bens.Grupo = bens.Grupos.Single(g => g.Codigo == "08");
            Assert.Equal(("60", "1.2.03.004", "1.2.03.007"), (bens.VidaUtil, bens.ContaBem, bens.ContaAcumulada));
            (bens.DataAquisicao, bens.InicioUso, bens.Valor, bens.Turnos) = (new DateTime(2026, 2, 5), new DateTime(2026, 2, 5), "6.000,00", 2);
            await bens.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(bens.Erros);
            Assert.Equal("Ativo", Assert.Single(bens.Lista).Situacao);
            Foto(janela, "02e_bens");
            vm.AbrirCommand.Execute(Telas.Depreciacao);
            var dep = vm.Depreciacao;
            (dep.Mes, dep.Ano) = (2, 2026);                                     // março/2026
            await dep.CalcularCommand.ExecuteAsync(null);
            var linhaDep = Assert.Single(dep.Linhas);
            Assert.Equal(("100,00", "150,00", "50,00"), (linhaDep.Contabil, linhaDep.Fiscal, linhaDep.Diferenca));   // 2 turnos: fiscal × 1,5
            Assert.True(dep.LancarCommand.CanExecute(null));
            Foto(janela, "02f_depreciacao");

            // 3. Lançamentos › Lançamento contábil (uma linha com débito e crédito = um lançamento, digitado pelo teclado)
            Assert.True(vm.AbrirCommand.CanExecute(Telas.Lancamento));
            vm.AbrirCommand.Execute(Telas.Lancamento);
            var l = vm.Lancamento;
            await l.AoTrocarEmpresaAsync();
            Assert.Single(l.Linhas);
            var lin = l.Linhas[0];
            lin.Data = l.DataCompleta("10/03/2026");
            var reduzidoBancos = l.TodasContas.Single(c => c.Codigo == "1.1.01.002").Reduzido!.Value;
            lin.Reduzido = reduzidoBancos.ToString();                           // reduzido preenche a conta
            Assert.Equal(("1.1.01.002", "Bancos Conta Movimento"), (lin.Conta, lin.NomeConta));
            lin.ContaCredito = "venda de mercad";                               // nome da conta: resolvido ao sair do campo
            l.ConfirmarConta(lin, credito: true);
            Assert.Equal("3.1.01.001", lin.ContaCredito);
            lin.Valor = "1500";
            lin.FormatarValor();
            Assert.Equal("1.500,00", lin.Valor);
            lin.CodigoHistorico = "1";                                          // histórico padrão + complemento livre
            Assert.Equal("Venda à vista NF ", lin.Historico);
            lin.Historico += "123";
            lin.Documento = "NF 123";                                          // nº do documento (conciliação)
            var nova = l.AdicionarLinha();                                      // seta para baixo: nova linha
            Assert.Equal(2, l.Linhas.Count);
            Assert.Equal("10/03/2026", nova.Data);
            Assert.True(l.CancelarLinhaNova(nova));                             // seta para cima: cancela a linha criada
            Assert.Single(l.Linhas);
            await l.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(l.Erros);
            Assert.Equal("Lançamento nº 1 salvo.", l.Mensagem);
            var efetuado = Assert.Single(l.Efetuados);                         // lista dos lançamentos efetuados
            Assert.Equal((1L, "NF 123", ""), (efetuado.Numero, efetuado.Documento, efetuado.Situacao));
            Assert.Equal("10/03/2026", l.Linhas[0].Data);                     // a data segue para o próximo
            (l.Linhas[0].Conta, l.Linhas[0].ContaCredito, l.Linhas[0].Valor, l.Linhas[0].Historico) = ("4.2.02.001", "1.1.01.002", "2.300,00", "Aluguel de março");
            Foto(janela, "03_lancamento");

            // Várias linhas com uma conta só (partidas simples) fecham um único lançamento; o tipo é inferido
            l.LimparCommand.Execute(null);
            var l1 = Assert.Single(l.Linhas);
            (l1.Data, l1.Reduzido, l1.Valor, l1.Documento) = ("11/03/2026", reduzidoBancos.ToString(), "100,00", "NF 77");
            Assert.Equal("1.1.01.002", l1.Conta);
            Assert.False(l.Balanceado);
            Assert.Equal(("100,00", "0,00", "100,00"), (l.TotalDebito, l.TotalCredito, l.Diferenca));
            l1.CodigoHistorico = "2";                                           // variáveis do histórico preenchidas pela linha
            Assert.Equal("Pgto NF 77 ref. 02/2026 {COMPLEMENTO}", l1.Historico);
            l1.Documento = "NF 78";                                             // texto automático acompanha os dados da linha
            Assert.Equal("Pgto NF 78 ref. 02/2026 {COMPLEMENTO}", l1.Historico);
            await l.SalvarCommand.ExecuteAsync(null);                           // variável pendente: o usuário precisa completar
            Assert.Contains("{COMPLEMENTO}", Assert.Single(l.Erros));
            l1.Historico = "Pgto NF 78 ref. 02/2026 parcela 1";
            var salvando = l.SalvarCommand.ExecuteAsync(null);                  // só débito: pede confirmação, não grava
            Assert.Contains("falta o crédito", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(false);
            await salvando;
            Assert.Single(l.Linhas);
            Assert.Single(l.Efetuados);
            salvando = l.SalvarCommand.ExecuteAsync(null);                      // aceita: cria a linha do crédito já com o valor que fecha
            await AguardarConfirmacao(vm);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await salvando;
            Assert.Equal(2, l.Linhas.Count);
            Assert.Equal("100,00", l.Linhas[1].Valor);
            Assert.Equal("NF 78", l.Linhas[1].Documento);
            l.Linhas[1].ContaCredito = "3.1.01.001";
            Assert.True(l.Balanceado);
            Assert.Equal("0,00", l.Diferenca);
            l.Linhas[1].Valor = "90,00";                                        // mais de uma linha e não fecha: erro, sem confirmação
            await l.SalvarCommand.ExecuteAsync(null);
            Assert.Contains("não fecham", Assert.Single(l.Erros));
            Assert.Null(vm.Confirmacao);
            l.Linhas[1].Valor = "100,00";
            Foto(janela, "03b_lancamento_partidas_simples");
            l.LimparCommand.Execute(null);
            vm.FecharFuncaoCommand.Execute(vm.JanelasAbertas.Single(j => j.Tela == Telas.Lancamento));

            // Configurações gerais não tem mais o tipo de lançamento: só senha de administrador e limpeza
            vm.AbrirCommand.Execute(Telas.ConfiguracoesGerais);
            await vm.ConfiguracoesGerais.CarregarAsync();
            vm.FecharFuncaoCommand.Execute(vm.JanelasAbertas.Single(j => j.Tela == Telas.ConfiguracoesGerais));

            // Menu aberto (submenu Lançamentos)
            var menuLancamentos = janela.GetVisualDescendants().OfType<MenuItem>().First(m => (m.Header as string) == "_Lançamentos");
            menuLancamentos.IsSubMenuOpen = true;
            Foto(janela, "04_menu_lancamentos");
            menuLancamentos.IsSubMenuOpen = false;

            // 4. Relatórios
            vm.AbrirCommand.Execute(Telas.Balancete);
            (vm.Balancete.Inicio, vm.Balancete.Fim) = (new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
            await vm.Balancete.GerarCommand.ExecuteAsync(null);
            Assert.Contains("✓ fechado", vm.Balancete.Totais);
            Foto(janela, "05_balancete");

            // Exportação do que está na tela: PDF, Excel (.xlsx) e CSV
            var pastaExport = Path.Combine(Path.GetTempPath(), "vorticis_export_" + Guid.NewGuid().ToString("N"));
            (ArquivosGerados.AbrirAposGerar, ArquivosGerados.Pasta) = (false, pastaExport);
            Assert.True(vm.Balancete.ExportarPdfCommand.CanExecute(null));
            vm.Balancete.ExportarPdfCommand.Execute(null);
            Assert.Empty(vm.Balancete.Erros);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(vm.Balancete.UltimoArquivo!), 0, 4));
            vm.Balancete.ExportarExcelCommand.Execute(null);
            var planilha = Vorticis.Infra.Relatorios.ExportadorExcel.Ler(File.ReadAllBytes(vm.Balancete.UltimoArquivo!));
            var bancos = planilha.Single(l => (l["Conta"] as string) == "1.1.01.002");
            Assert.Equal(1500m, Convert.ToDecimal(bancos["Débitos"]));             // número de verdade no Excel
            vm.Balancete.ExportarCsvCommand.Execute(null);
            var csv = File.ReadAllText(vm.Balancete.UltimoArquivo!);
            Assert.StartsWith("Conta;Descrição;Tipo;Saldo anterior;D/C;Débitos", csv.TrimStart('\uFEFF'));
            Assert.Contains("1.1.01.002;Bancos Conta Movimento;A;0,00;;1.500,00;", csv);

            // Integrações › Exportar › Lançamentos contábeis: uma linha por partida, em PDF, Excel e CSV
            vm.AbrirCommand.Execute(Telas.ExportarLancamentos);
            var expl = vm.ExportacaoLancamentos;
            (expl.Inicio, expl.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            await expl.GerarCommand.ExecuteAsync(null);
            Assert.Empty(expl.Erros);
            Assert.Equal(2, expl.Linhas.Count);
            Assert.StartsWith("1 lançamento(s), 2 partida(s)", expl.Resumo);
            expl.ExportarExcelCommand.Execute(null);
            var lancXlsx = Vorticis.Infra.Relatorios.ExportadorExcel.Ler(File.ReadAllBytes(expl.UltimoArquivo!));
            Assert.Equal(("1.1.01.002", "NF 123"), ((string)lancXlsx[0]["Conta"]!, (string)lancXlsx[0]["Documento"]!));
            expl.ExportarCsvCommand.Execute(null);
            Assert.StartsWith("Nº;Data;Conta;Descrição da conta;D/C;Valor;Histórico;Documento", File.ReadAllText(expl.UltimoArquivo!).TrimStart('\uFEFF'));
            expl.ExportarPdfCommand.Execute(null);
            Assert.Empty(expl.Erros);
            Foto(janela, "05f_exportar_lancamentos");

            // Consultas: saldos e lançamentos sem abrir relatório
            vm.AbrirCommand.Execute(Telas.ConsultaSaldos);
            var cons = vm.Consultas;
            Assert.Equal(ConsultasViewModel.AbaSaldos, cons.AbaSelecionada);
            cons.DataSaldo = new DateTime(2026, 3, 31);
            await cons.ConsultarSaldosCommand.ExecuteAsync(null);
            Assert.Empty(cons.Erros);
            Assert.Equal("1.500,00 D", cons.Saldos.Single(s => s.Linha.Codigo == "1.1.01.002").Saldo);
            cons.FiltroConta = "3";
            Assert.All(cons.Saldos, s => Assert.StartsWith("3", s.Linha.Codigo));
            Foto(janela, "05b_consulta_saldos");
            vm.AbrirCommand.Execute(Telas.ConsultaLancamentos);
            Assert.Equal(ConsultasViewModel.AbaLancamentos, cons.AbaSelecionada);
            (cons.Inicio, cons.Fim) = (new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
            await cons.ConsultarLancamentosCommand.ExecuteAsync(null);
            Assert.Equal(2, cons.Lancamentos.Count);
            cons.TextoHistorico = "inexistente";
            Assert.Empty(cons.Lancamentos);
            cons.TextoHistorico = "venda";
            Assert.Equal(2, cons.Lancamentos.Count);
            Assert.Contains("1 lançamento(s)", cons.ResumoLancamentos);
            Foto(janela, "05c_consulta_lancamentos");

            // Consultas › Diferenças de saldos
            vm.AbrirCommand.Execute(Telas.DiferencasSaldos);
            var dif = vm.DiferencasSaldos;
            (dif.Inicio, dif.Fim) = (new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
            await dif.AnalisarCommand.ExecuteAsync(null);
            Assert.Empty(dif.Erros);
            Assert.Contains("nenhuma inconsistência", dif.Resumo);
            Assert.Equal("+1.500,00", dif.Linhas.Single(d => d.D.Linha.Codigo == "1.1.01.002").Diferenca);
            Foto(janela, "05d_diferencas_saldos");

            // 5. Cadastros › Plano de contas: inclui subconta, herdando natureza
            vm.AbrirCommand.Execute(Telas.PlanoContas);
            var plano = vm.PlanoContas;
            await plano.CarregarAsync();
            plano.Selecionada = plano.Lista.Single(c => c.Conta.Codigo == "1.1.01.003");
            Assert.Equal("1.1.01.003", plano.Codigo);                // consulta: mostra a conta selecionada
            plano.IncluirCommand.Execute(null);
            Assert.Equal("1.1.01.004", plano.Codigo);                // sugestão da próxima irmã
            Assert.Contains("Subconta de 1.1.01", plano.DicaPai);
            plano.Descricao = "Caixa da filial";
            await plano.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(plano.Erros);
            Assert.True(plano.EmConsulta);
            Assert.Contains(plano.Lista, c => c.Conta.Codigo == "1.1.01.004" && c.Conta.Analitica);
            Assert.Equal("1.1.01.004", plano.Selecionada?.Conta.Codigo);
            plano.AlterarCommand.Execute(null);                     // Alterar: descrição
            plano.Descricao = "Caixa da filial Centro";
            await plano.SalvarCommand.ExecuteAsync(null);
            Assert.Equal("Caixa da filial Centro", plano.Selecionada?.Conta.Descricao);
            Foto(janela, "06_plano_contas");

            // Integrações › Importar › Plano de contas (planilha e máscara)
            vm.AbrirCommand.Execute(Telas.ImportarPlano);
            var impPlano = vm.ImportacaoPlano;
            Assert.Same(impPlano, vm.Conteudo);
            await impPlano.CarregarAsync();
            Assert.Equal("9.9.99.999", impPlano.MascaraAtual);
            var csvPlano = Path.Combine(Path.GetTempPath(), $"plano_{Guid.NewGuid():N}.csv");
            File.WriteAllText(csvPlano, "Classificação;Nome da conta\n1;ATIVO\n11;CIRCULANTE\n111;DISPONÍVEL\n1111;Caixa\n2;PASSIVO\n21;CIRCULANTE\n211;Fornecedores\n");
            impPlano.Mascara = "9.9.9.99";
            impPlano.Arquivo = csvPlano;
            Assert.Equal((0, 1), (impPlano.ColunaCodigo, impPlano.ColunaDescricao));
            Assert.Equal(7, impPlano.Previa.Count);
            Assert.Contains(impPlano.Previa, p => p.Original == "1111" && p.Codigo.Trim() == "1.1.1.01" && p.Tipo == "Analítica");
            Assert.True(impPlano.PreviaValida);
            Foto(janela, "06b_importar_plano");
            var importacao = impPlano.ImportarCommand.ExecuteAsync(null);
            Assert.Contains("SUBSTITUÍDO", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await importacao;
            Assert.Contains("já tem lançamentos", impPlano.Erros[0]);                // empresa com movimento: plano preservado
            impPlano.Mascara = "9.9.99.9999";                                        // converter o plano atual (e voltar)
            var conversao = impPlano.AplicarMascaraCommand.ExecuteAsync(null);
            Assert.Contains("1.1.01.0001", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await conversao;
            Assert.Empty(impPlano.Erros);
            Assert.Equal("9.9.99.9999", impPlano.MascaraAtual);
            impPlano.Mascara = "9.9.99.999";
            conversao = impPlano.AplicarMascaraCommand.ExecuteAsync(null);
            await AguardarConfirmacao(vm);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await conversao;
            Assert.Contains("convertido(s) para a máscara 9.9.99.999", impPlano.Mensagem);
            vm.AbrirCommand.Execute(Telas.PlanoContas);
            await plano.CarregarAsync();
            Assert.Contains(plano.Lista, c => c.Conta.Codigo == "1.1.01.002");

            // Integrações › Importar › Planilhas: layout personalizado salvo e importação (só contas patrimoniais)
            vm.AbrirCommand.Execute(Telas.ImportarPlanilhas);
            Assert.Same(vm.ImportacaoPlanilhas, vm.Conteudo);
            var planilhas = vm.ImportacaoPlanilhas;
            var csvLanc = Path.Combine(Path.GetTempPath(), $"lanc_{Guid.NewGuid():N}.csv");
            File.WriteAllText(csvLanc, "Data;Débito;Crédito;Valor;Histórico\n15/03/2026;1.1.01.001;1.1.02.001;250,00;Recebimento cliente balcão\n");
            planilhas.Arquivo = csvLanc;
            Assert.Equal(("Data", "Débito", "Crédito", "Valor"), (planilhas.ColunaData, planilhas.ColunaContaDebito, planilhas.ColunaContaCredito, planilhas.ColunaValor));
            var linhaPlanilha = Assert.Single(planilhas.LinhasPlanilha);              // a planilha carregada aparece como está no arquivo
            Assert.Equal((2, "1.1.01.001"), (linhaPlanilha.Numero, linhaPlanilha.Celulas[1]));
            Assert.Equal("Conta débito", planilhas.PapelDaColuna("Débito"));
            planilhas.CarregarPlanilhaCommand.Execute(null);                       // "Carregar" lê de novo o arquivo
            Assert.Single(planilhas.LinhasPlanilha);
            planilhas.NomeLayout = "Recebimentos do balcão";
            await planilhas.SalvarLayoutCommand.ExecuteAsync(null);
            Assert.Empty(planilhas.Erros);
            Assert.Equal("Recebimentos do balcão", planilhas.LayoutSelecionado?.Nome);
            await planilhas.PreVisualizarCommand.ExecuteAsync(null);
            Assert.Empty(planilhas.Erros);
            Assert.Equal("OK", Assert.Single(planilhas.Previa).Situacao);
            Foto(janela, "06c_importar_planilha");
            var importando = planilhas.ImportarCommand.ExecuteAsync(null);
            await AguardarConfirmacao(vm);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await importando;
            Assert.Empty(planilhas.Erros);
            Assert.Equal("1 lançamento(s) importado(s): nº 2 a 2.", planilhas.Mensagem);

            // Extratos bancários (OFX): reimportar não duplica
            vm.AbrirCommand.Execute(Telas.ImportarExtratos);
            var ext = vm.ImportacaoExtratos;
            Assert.Same(ext, vm.Conteudo);
            var ofx = Path.Combine(Path.GetTempPath(), $"extrato_{Guid.NewGuid():N}.ofx");
            File.WriteAllText(ofx, "OFXHEADER:100\n<OFX><BANKACCTFROM><BANKID>341<BRANCHID>0001<ACCTID>5555-1</BANKACCTFROM><BANKTRANLIST>" +
                                   "\n<STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260320<TRNAMT>400.00<FITID>T1<MEMO>PIX CLIENTE ABC\n</BANKTRANLIST></OFX>");
            (ext.ContaBanco, ext.ContrapartidaEntradas, ext.PrefixoHistorico) = ("1.1.01.004", "1.1.02.001", "Extrato:");
            ext.Arquivo = ofx;
            Assert.Contains("1 movimento(s)", ext.DadosExtrato);
            Assert.Equal("1.1.02.001", Assert.Single(ext.Transacoes).Contrapartida);
            await ext.PreVisualizarCommand.ExecuteAsync(null);
            Assert.True(ext.ImportarCommand.CanExecute(null), string.Join("; ", ext.Erros.Concat(ext.Previa.Select(p => p.Situacao))));
            Foto(janela, "06d_importar_extrato");
            importando = ext.ImportarCommand.ExecuteAsync(null);
            await AguardarConfirmacao(vm);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await importando;
            Assert.Equal("1 lançamento(s) importado(s): nº 3 a 3.", ext.Mensagem);
            await ext.PreVisualizarCommand.ExecuteAsync(null);
            Assert.Equal("Já importado", Assert.Single(ext.Previa).Situacao);
            Assert.False(ext.ImportarCommand.CanExecute(null));
            Assert.False(ext.UsaLayout);

            // Extrato em CSV/TXT/PDF: carrega primeiro o arquivo, o layout é sugerido pelo documento e pode ser salvo
            var txt = Path.Combine(Path.GetTempPath(), $"extrato_{Guid.NewGuid():N}.txt");
            File.WriteAllText(txt, "Data        Histórico            Valor        Saldo\n21/03/2026  PIX FORNECEDOR XYZ   -150,00      250,00\n" +
                                   "22/03/2026  SALDO DO DIA                      250,00\n");
            ext.Arquivo = txt;
            Assert.True(ext.UsaLayout);
            Assert.Equal(3, ext.LinhasDocumento.Count);
            var mov = Assert.Single(ext.Transacoes);
            Assert.Equal(("150,00", "PIX FORNECEDOR XYZ"), (mov.Saida.Replace("R$", "").Trim(), mov.Descricao));
            ext.ColunaValor = 4;                                    // aponta o saldo: o movimento muda na hora
            Assert.Equal("250,00", Assert.Single(ext.Transacoes).Entrada.Replace("R$", "").Trim());
            ext.ColunaValor = 0;
            ext.NomeLayout = $"Banco TXT {Guid.NewGuid():N}"[..18];
            await ext.SalvarLayoutCommand.ExecuteAsync(null);
            Assert.Empty(ext.Erros);
            Assert.Equal(ext.NomeLayout, ext.LayoutSelecionado?.Nome);
            Foto(janela, "06e_importar_extrato_pdf");

            // Estorno na própria tela de lançamento: o lançamento estornado continua na lista, marcado
            vm.AbrirCommand.Execute(Telas.Lancamento);
            var lanc = vm.Lancamento;
            await lanc.CarregarAsync();
            Assert.Equal(3, lanc.Efetuados.Count);
            lanc.EfetuadoSelecionado = lanc.Efetuados.Single(e => e.Numero == 3);
            Assert.Equal(new DateTime(2026, 3, 20), lanc.DataEstorno);
            await lanc.EstornarCommand.ExecuteAsync(null);
            Assert.Contains("motivo", lanc.Erros[0]);
            lanc.MotivoEstorno = "Contrapartida errada";
            var estornando = lanc.EstornarCommand.ExecuteAsync(null);
            Assert.Contains("lançamento nº 3", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await estornando;
            Assert.Equal("Lançamento nº 3 estornado pelo lançamento nº 4.", lanc.Mensagem);
            Assert.Equal(4, lanc.Efetuados.Count);
            Assert.Equal("Estornado", lanc.Efetuados.Single(e => e.Numero == 3).Situacao);
            Assert.False(lanc.EstornarCommand.CanExecute(null));
            Foto(janela, "06f_lancamentos_efetuados");

            // Lançamentos › Conciliações
            vm.AbrirCommand.Execute(Telas.Conciliacao);
            var conc = vm.Conciliacao;
            await conc.CarregarContasAsync();
            conc.Ate = new DateTime(2026, 3, 31);
            conc.FiltroConta = "bancos conta";
            conc.ContaSelecionada = Assert.Single(conc.Contas);
            await conc.ConsultarCommand.ExecuteAsync(null);
            Assert.Equal("NF 123", Assert.Single(conc.Linhas).Documento);
            await conc.ConciliarAutomaticoCommand.ExecuteAsync(null);
            Assert.StartsWith("Nenhum documento fechou", conc.Mensagem);
            Foto(janela, "06g_conciliacao");

            // Lançamentos contábeis: planilha modelo do layout padrão
            vm.AbrirCommand.Execute(Telas.ImportarLancamentos);
            var padrao = vm.ImportacaoLancamentos;
            padrao.GerarModeloCommand.Execute(null);
            Assert.True(File.Exists(padrao.ModeloGerado));
            padrao.Arquivo = padrao.ModeloGerado!;
            await padrao.PreVisualizarCommand.ExecuteAsync(null);
            Assert.Empty(padrao.Erros);
            Assert.Equal(("1000,00".Replace(",00", ""), "OK"), (Assert.Single(padrao.Previa).Item.Lancamento.Total.ToString("0"), padrao.Previa[0].Situacao));
            Foto(janela, "06e_importar_lancamentos");

            // Centros de custo
            vm.AbrirCommand.Execute(Telas.CentrosCusto);
            vm.CentrosCusto.IncluirCommand.Execute(null);
            (vm.CentrosCusto.Codigo, vm.CentrosCusto.Descricao) = ("ADM", "Administrativo");
            await vm.CentrosCusto.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(vm.CentrosCusto.Erros);
            Assert.Single(vm.CentrosCusto.Lista);
            vm.CentrosCusto.IncluirCommand.Execute(null);           // Excluir (com confirmação)
            (vm.CentrosCusto.Codigo, vm.CentrosCusto.Descricao, vm.CentrosCusto.CodigoPai) = ("TEMP", "Temporário", "");
            await vm.CentrosCusto.SalvarCommand.ExecuteAsync(null);
            Assert.Equal(2, vm.CentrosCusto.Lista.Count);
            Assert.Equal("TEMP", vm.CentrosCusto.Selecionado?.Codigo);
            var exclusaoCc = vm.CentrosCusto.ExcluirCommand.ExecuteAsync(null);
            Assert.Contains("TEMP – Temporário", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await exclusaoCc;
            Assert.Single(vm.CentrosCusto.Lista);

            // Saldos iniciais
            vm.AbrirCommand.Execute(Telas.SaldosIniciais);
            var saldos = vm.SaldosIniciais;
            saldos.Ano = 2026;
            await saldos.CarregarAsync();
            saldos.Linhas.Single(l => l.Codigo == "1.1.01.001").Valor = "5.000,00";
            saldos.Linhas.Single(l => l.Codigo == "1.2.03.003").Valor = "40.000,00";
            saldos.Linhas.Single(l => l.Codigo == "2.3.01.001").Valor = "45.000,00";
            Assert.True(saldos.Balanceado);
            saldos.SomentePreenchidas = true;
            Foto(janela, "07_saldos_iniciais");
            await saldos.LancarCommand.ExecuteAsync(null);
            Assert.Empty(saldos.Erros);
            Assert.Contains("lançamento nº 5", saldos.Mensagem);       // após o nº 1 manual, os nº 2 e 3 importados e o estorno nº 4

            // Relatórios › Balanço Patrimonial e DRE (antes do encerramento: resultado destacado no PL)
            vm.AbrirCommand.Execute(Telas.Balanco);
            var bal = vm.Balanco;
            bal.Data31 = new DateTime(2026, 12, 31);
            await bal.GerarCommand.ExecuteAsync(null);
            Assert.Empty(bal.Erros);
            Assert.StartsWith("✓", bal.Resumo);
            Assert.NotEmpty(bal.Ativo);
            vm.AbrirCommand.Execute(Telas.Dre);
            var dem = vm.Dre;
            (dem.Inicio, dem.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            await dem.GerarCommand.ExecuteAsync(null);
            Assert.Empty(dem.Erros);
            Assert.Contains(dem.Dre, l => l.Descricao == "LUCRO LÍQUIDO DO PERÍODO" && l.Valor == "1.500,00");
            Foto(janela, "08a_demonstracoes");

            // Lançamento: Enter no último campo salva; duplo clique num efetuado abre para alteração (estorno + corrigido)
            vm.AbrirCommand.Execute(Telas.Lancamento);
            var la = vm.Lancamento;
            await la.CarregarAsync();
            (la.Linhas[0].Data, la.Linhas[0].Conta, la.Linhas[0].ContaCredito, la.Linhas[0].Valor, la.Linhas[0].Historico) =
                ("25/03/2026", "1.1.01.004", "1.1.02.001", "50,00", "Recebimento de cliente");
            Assert.True(la.SalvarPeloEnter());
            await la.SalvarCommand.ExecutionTask!;
            Assert.Empty(la.Erros);
            Assert.Equal("Lançamento nº 6 salvo.", la.Mensagem);
            Assert.False(la.SalvarPeloEnter());                                // linha vazia: Enter segue para a próxima linha
            await la.EditarAsync(la.Efetuados.Single(e => e.Numero == 6));
            Assert.True(la.EmEdicao);
            Assert.True(la.DigitacaoAberta);                               // a digitação abre no pop-up
            var editada = Assert.Single(la.Linhas);
            Assert.Equal(("1.1.01.004", "1.1.02.001", "50,00", "25/03/2026"), (editada.Conta, editada.ContaCredito, editada.Valor, editada.Data));
            Assert.Equal("Clientes", editada.NomeContaCredito);
            editada.Valor = "60,00";
            Foto(janela, "03c_lancamento_alteracao");
            await la.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(la.Erros);
            Assert.Equal("Lançamento nº 6 alterado: estornado pelo nº 7 e regravado como nº 8.", la.Mensagem);
            Assert.False(la.EmEdicao);
            Assert.False(la.DigitacaoAberta);                              // salvou: o pop-up fecha e a grade se atualiza
            Assert.Equal("Estornado", la.Efetuados.Single(e => e.Numero == 6).Situacao);
            await la.EditarAsync(la.Efetuados.Single(e => e.Numero == 6));    // estornado: não abre
            Assert.Contains("estornado", Assert.Single(la.Erros));
            await la.EditarAsync(la.Efetuados.Single(e => e.Numero == 2));    // importado: corrige-se por estorno
            Assert.Contains("não foi digitado manualmente", Assert.Single(la.Erros));
            Assert.False(la.EmEdicao);
            Assert.False(la.DigitacaoAberta);
            la.NovoLancamentoCommand.Execute(null);                        // "Novo lançamento" abre o pop-up em branco
            Assert.True(la.DigitacaoAberta);
            Dispatcher.UIThread.RunJobs();
            var pop = Assert.IsAssignableFrom<Window>(janela.ModalAberta);   // o pop-up existe de verdade e não está vazio
            pop.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var dig = Assert.IsType<LancamentoDigitacaoView>(pop.Content);
            Foto(pop, "03d_popup_novo_lancamento");
            Control? Campo(string tag) => dig.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Tag as string == tag);
            // Visíveis de verdade: com tamanho e dentro da área do pop-up (a grade antiga empurrava tudo para fora da janela)
            foreach (var tag in new[] { "data", "conta", "contacredito", "valor", "documento", "historico" })
            {
                var campo = Campo(tag);
                Assert.True(campo is { IsVisible: true, Bounds.Width: > 0, Bounds.Height: > 0 }, $"campo '{tag}' não aparece no pop-up");
                var topo = campo!.TranslatePoint(new Point(0, 0), pop)!.Value;
                Assert.True(topo.X >= 0 && topo.Y >= 0 && topo.Y + campo.Bounds.Height <= pop.ClientSize.Height && topo.X + campo.Bounds.Width <= pop.ClientSize.Width,
                    $"campo '{tag}' fora da área visível do pop-up ({topo})");
            }
            foreach (var nome in new[] { "BotaoSalvar", "BotaoCancelar", "BotaoLimpar", "Detalhes" })
                Assert.True(dig.FindControl<Control>(nome) is { IsVisible: true, Bounds.Height: > 0 }, nome);
            // Enter avança para o próximo campo; Shift+Enter volta
            Campo("data")!.Focus(); Dispatcher.UIThread.RunJobs();
            pop.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null); Dispatcher.UIThread.RunJobs();
            Assert.Equal("reduzido", (pop.FocusManager!.GetFocusedElement() as Control)?.Tag);
            pop.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.Enter, null); Dispatcher.UIThread.RunJobs();
            Assert.Same(Campo("data"), pop.FocusManager!.GetFocusedElement());
            Assert.NotNull(dig.FindControl<ItemsControl>("LinhasLancamento"));
            // preenchimento na vertical: cada campo abaixo do anterior
            var alturas = new[] { "data", "reduzido", "reduzidocredito", "valor", "documento", "codhistorico", "historico" }
                .Select(t => Campo(t)!.TranslatePoint(new Point(0, 0), pop)!.Value.Y).ToList();
            Assert.Equal(alturas.Order(), alturas);
            Assert.Equal(alturas.Count, alturas.Distinct().Count());
            // seta para baixo na última partida cria outra, e o formulário passa a mostrá-la
            pop.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, la.Linhas.Count);
            Assert.Same(la.Linhas[1], la.LinhaSelecionada);
            Assert.Same(la.Linhas[1], Campo("data")!.DataContext);
            la.CancelarDigitacao();
            Assert.False(la.DigitacaoAberta);
            // Botões da grade: Excluir (com confirmação), Consultar (somente leitura + Estornar) e Editar (pop-up para alteração)
            la.EfetuadoSelecionado = la.Efetuados.Single(e => e.Numero == 8);
            var excluindoLanc = la.ExcluirSelecionadoCommand.ExecuteAsync(null);     // sempre pede confirmação; cancelar não exclui
            var confirmaExclusao = (await AguardarConfirmacao(vm)).Mensagem;
            Assert.Contains("Deseja realmente excluir o lançamento nº 8 de ", confirmaExclusao);
            Assert.Contains(", valor R$ ", confirmaExclusao);
            vm.ResponderConfirmacaoCommand.Execute(false);
            await excluindoLanc;
            Assert.Contains(la.Efetuados, e => e.Numero == 8);
            await la.ConsultarSelecionadoCommand.ExecuteAsync(null);
            Assert.True(la.DigitacaoAberta && la.SomenteLeitura);
            Dispatcher.UIThread.RunJobs();
            var popLeitura = Assert.IsAssignableFrom<Window>(janela.ModalAberta);
            var digLeitura = Assert.IsType<LancamentoDigitacaoView>(popLeitura.Content);
            Assert.False(digLeitura.FindControl<Control>("BotaoSalvar")!.IsVisible);
            Assert.True(digLeitura.FindControl<Control>("BotaoEstornar")!.IsVisible);
            Assert.False(la.SalvarCommand.CanExecute(null));
            la.CancelarDigitacao();
            await la.EditarSelecionadoCommand.ExecuteAsync(null);
            Assert.True(la.DigitacaoAberta && la.EmEdicao && !la.SomenteLeitura);
            Dispatcher.UIThread.RunJobs();
            Assert.True(la.SalvarCommand.CanExecute(null));
            la.CancelarDigitacao();
            Assert.False(la.DigitacaoAberta);
            Assert.Null(janela.ModalAberta);
            vm.FecharFuncaoCommand.Execute(vm.JanelasAbertas.Single(j => j.Tela == Telas.Lancamento));

            // Plano de contas com participantes: opção na empresa, cliente alocado na conta Clientes, participante no lançamento
            vm.AbrirCommand.Execute(Telas.Empresas);
            var empPart = vm.Empresas;
            await empPart.CarregarSelecionadaAsync();
            Assert.False(empPart.UsaParticipantes);
            empPart.AlterarCommand.Execute(null);
            empPart.UsaParticipantes = true;
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(empPart.Erros);
            vm.AbrirCommand.Execute(Telas.Clientes);
            var cliPart = vm.Clientes;
            await cliPart.CarregarAsync();
            Assert.True(cliPart.UsaParticipantes);
            cliPart.Selecionado = cliPart.Lista.Single(c => c.Codigo == "1");
            cliPart.AlterarCommand.Execute(null);
            (cliPart.Conta, cliPart.ControleConta) = ("1.1.02.001", true);
            await cliPart.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(cliPart.Erros);
            vm.AbrirCommand.Execute(Telas.Lancamento);
            la = vm.Lancamento;
            await la.CarregarAsync();
            Dispatcher.UIThread.RunJobs();                                 // tela montada antes de digitar
            Assert.True(la.UsaParticipantes);
            (la.Linhas[0].Data, la.Linhas[0].Conta, la.Linhas[0].ContaCredito, la.Linhas[0].Valor, la.Linhas[0].Historico) =
                ("26/03/2026", "1.1.01.004", "1.1.02.001", "40,00", "Recebimento");
            await la.SalvarCommand.ExecuteAsync(null);
            Assert.Contains(la.Erros, e => e.Contains("exige participante"));
            la.Linhas[0].Participante = "1";
            Assert.Contains("EXEMPLO COMERCIO", la.Linhas[0].ResumoContas);
            await la.SalvarCommand.ExecuteAsync(null);
            Assert.True(la.Erros.Count == 0, string.Join("; ", la.Erros));
            Foto(janela, "03d_lancamento_participante");
            vm.AbrirCommand.Execute(Telas.Empresas);                       // desliga de novo (o restante do roteiro lança sem participante)
            await empPart.CarregarSelecionadaAsync();
            Assert.True(empPart.UsaParticipantes);
            empPart.AlterarCommand.Execute(null);
            empPart.UsaParticipantes = false;
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(empPart.Erros);

            // Cadastros › Empresas: NIRE (aba Dados cadastrais) e aba Contabilidade (modelo do plano, porte, contas de encerramento)
            await empPart.CarregarSelecionadaAsync();
            Assert.True(empPart.PodeAbaContabilidade);
            Assert.Equal("basico", empPart.ModeloSelecionado?.Id);
            Assert.Equal(("5.1.01.001", "2.3.03.001", "2.3.03.002"), (empPart.ContaApuracao, empPart.ContaLucros, empPart.ContaPrejuizos));
            empPart.AlterarCommand.Execute(null);
            empPart.Nire = "123";
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.Contains(empPart.Erros, e => e.Contains("NIRE"));
            empPart.Nire = "35.2.0123456-7";
            (empPart.GrandePorte, empPart.UsaCentroCusto, empPart.ContaLucros) = (true, true, "2.3.03.001");
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.True(empPart.Erros.Count == 0, string.Join("; ", empPart.Erros));
            Assert.Equal("35201234567", await db.EscalarAsync<string>("SELECT nire FROM core.empresa WHERE id = @id", ("id", empresa.Id)));
            Assert.True(await db.EscalarAsync<bool>("SELECT grande_porte AND usa_centro_custo FROM core.empresa WHERE id = @id", ("id", empresa.Id)));

            // Aba Contabilidade: máscara dos centros de custo (aqui, não na tela de centros), documento obrigatório e bloqueio de datas
            await empPart.CarregarSelecionadaAsync();
            Assert.True(empPart.UsaCentroCusto);
            empPart.AlterarCommand.Execute(null);
            (empPart.MascaraCentroCusto, empPart.ExigeDocumento, empPart.DataBloqueio) = ("9.99.999", true, new DateTime(2025, 12, 31));
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.Contains(empPart.Erros, e => e.Contains("não cabe na máscara"));    // os centros já cadastrados precisam caber na máscara
            Assert.True(empPart.EmEdicao);
            empPart.MascaraCentroCusto = "";
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.True(empPart.Erros.Count == 0, string.Join("; ", empPart.Erros));
            Assert.True(await db.EscalarAsync<bool>("SELECT exige_documento AND data_bloqueio = DATE '2025-12-31' FROM core.empresa WHERE id = @id", ("id", empresa.Id)));
            await empPart.CarregarSelecionadaAsync();
            Assert.Equal(("", true, new DateTime(2025, 12, 31)), (empPart.MascaraCentroCusto, empPart.ExigeDocumento, empPart.DataBloqueio));
            // sem regras que atrapalhem o restante do fluxo (os lançamentos seguintes não informam documento)
            empPart.AlterarCommand.Execute(null);
            (empPart.ExigeDocumento, empPart.DataBloqueio) = (false, null);
            await empPart.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(empPart.Erros);

            vm.AbrirCommand.Execute(Telas.Balancete);
            (vm.Balancete.Inicio, vm.Balancete.Fim, vm.Balancete.DetalharParticipantes) = (new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), true);
            await vm.Balancete.GerarCommand.ExecuteAsync(null);
            Assert.Contains(vm.Balancete.Linhas, l => l.Descricao.Contains("↳ 1 – EXEMPLO COMERCIO"));   // o detalhamento vale para os lançamentos já feitos
            Foto(janela, "05e_balancete_participantes");
                        foreach (var j in vm.JanelasAbertas.Where(j => j.Tela is Telas.Lancamento or Telas.Clientes).ToList())
                vm.FecharFuncaoCommand.Execute(j);

            // Lançamentos › Encerramento do exercício
            vm.AbrirCommand.Execute(Telas.Encerramento);
            var enc = vm.Encerramento;
            enc.Ano = 2026;
            await enc.PreverCommand.ExecuteAsync(null);
            Assert.Contains("LUCRO DO EXERCÍCIO: 1.500,00", enc.Previa);
            await enc.EncerrarCommand.ExecuteAsync(null);
            Assert.Empty(enc.Erros);
            Assert.True(enc.Encerrado);
            Foto(janela, "08b_encerramento");

            // Auditoria › Gerar ECD (SPED Contábil): abre o assistente e chega à validação prévia (pendências de cadastro bloqueiam)
            Assert.True(vm.AbrirCommand.CanExecute(Telas.EcdAssistente));
            vm.AbrirCommand.Execute(Telas.EcdAssistente);
            var ecd = vm.EcdAssistente;
            await ecd.CarregarAsync();
            Assert.Equal(1, ecd.Passo);
            Assert.Equal("EXEMPLO COMERCIO DE INFORMATICA LTDA", ecd.EmpresaNome);
            ecd.Ano = 2026;
            Foto(janela, "08c_ecd_passo1");
            await ecd.AvancarCommand.ExecuteAsync(null);
            Assert.Empty(ecd.Erros);
            Assert.Equal(2, ecd.Passo);
            Assert.NotEmpty(ecd.PreRequisitos);
            Foto(janela, "08c_ecd_passo2");
            await ecd.AvancarCommand.ExecuteAsync(null);
            Assert.Equal(3, ecd.Passo);
            Assert.NotEmpty(ecd.Pendencias);
            Foto(janela, "08c_ecd_passo3_validacao");
            ecd.Aba = EcdAssistenteViewModel.AbaGeradas;
            Foto(janela, "08c_ecd_geradas");
            ecd.Aba = EcdAssistenteViewModel.AbaAssistente;
            foreach (var j in vm.JanelasAbertas.Where(j => j.Tela == Telas.EcdAssistente).ToList()) vm.FecharFuncaoCommand.Execute(j);

            // Relatórios › Contábeis › DMPL e DLPA (telas separadas): a implantação de saldos é a abertura; o encerramento não é mutação
            vm.AbrirCommand.Execute(Telas.Dmpl);
            var mut = vm.Dmpl;
            (mut.Inicio, mut.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            await mut.GerarCommand.ExecuteAsync(null);
            Assert.Empty(mut.Erros);
            Assert.Equal(["Capital social", "Reservas de capital", "Reservas de lucros", "Ajustes de avaliação patrimonial",
                          "Lucros ou prejuízos acumulados", "Total"], mut.ColunasDmpl);
            Assert.Equal(("Saldo inicial em 31/12/2025", "45.000,00"), (mut.Dmpl[0].Descricao, mut.Dmpl[0].Valores[^1]));
            Assert.Contains(mut.Dmpl, l => l.Descricao.StartsWith("Resultado do exercício") && l.Valores[4] == "1.500,00");
            Assert.Equal("46.500,00", mut.Dmpl[^1].Valores[^1]);                  // = PL do Balanço depois do encerramento
            Foto(janela, "08b2_dmpl");
            mut.ExportarPdfCommand.Execute(null);
            Assert.Empty(mut.Erros);
            vm.AbrirCommand.Execute(Telas.Dlpa);
            var dlpa = vm.Dlpa;
            (dlpa.Inicio, dlpa.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            await dlpa.GerarCommand.ExecuteAsync(null);
            Assert.Empty(dlpa.Erros);
            Assert.Equal("1.500,00", dlpa.Dlpa[^1].Valor);
            Foto(janela, "08b3_dlpa");
            dlpa.ExportarPdfCommand.Execute(null);
            Assert.Empty(dlpa.Erros);

            // Cadastros › Empresas, aba Sócios: os do QSA vieram no cadastro da empresa; completa CPF e indica o representante legal
            vm.AbrirCommand.Execute(Telas.Empresas);
            Assert.True(vm.Empresas.TemEmpresaSelecionada);
            vm.Empresas.AbaSelecionada = CadastroEmpresaViewModel.AbaSocios;
            var socios = vm.Socios;
            await socios.CarregarAsync();
            Assert.Equal(["BELTRANA DA SILVA", "FULANO DE TAL"], socios.Lista.Select(s => s.Nome).Order());
            socios.Selecionado = socios.Lista.Single(s => s.Nome == "FULANO DE TAL");
            Assert.Equal("***123456**", socios.DocumentoMascarado);
            socios.AlterarCommand.Execute(null);
            (socios.Documento, socios.Participacao, socios.Representante) = ("390.533.447-05", "70", true);
            await socios.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(socios.Erros);
            Foto(janela, "08g_socios");

            // Cadastros › Empresas, aba Contadores (o mesmo cadastro de Cadastros › Contadores)
            vm.Empresas.AbaSelecionada = CadastroEmpresaViewModel.AbaContadores;
            var cont = vm.Contabilidade;
            await cont.CarregarAsync();
            cont.IncluirCommand.Execute(null);
            cont.IncluirContador = true;
            (cont.ContadorNome, cont.ContadorCpf, cont.ContadorCrc, cont.ContadorUf) = ("Maria Contadora", "529.982.247-25", "1SP123456/O-7", "SP");
            await cont.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(cont.Erros);
            cont.IncluirCommand.Execute(null);
            cont.IncluirContador = false;
            (cont.EscritorioCnpj, cont.EscritorioRazao, cont.EscritorioCrc) = ("45.723.174/0001-10", "Contábil Exemplo Ltda", "2SP012345/O-1");
            await cont.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(cont.Erros);
            Assert.Equal(2, cont.Vinculos.Count);
            Assert.Equal("✓ assina ECD/ECF", cont.Vinculos.Single(v => v.Nome == "Maria Contadora").AssinaEcdEcf);   // marcado por padrão na inclusão
            Foto(janela, "08h_contabilidade");
            vm.AbrirCommand.Execute(Telas.Contabilidade);
            Assert.Same(cont, vm.Conteudo);
            Assert.Equal("Cadastros › Contadores", vm.TituloTela);
            Assert.Equal(2, cont.Vinculos.Count);

            // Configurações › Certificados digitais: importa um A1 (.pfx), confere titular/CNPJ e avisa o vencimento próximo
            vm.AbrirCommand.Execute(Telas.Certificados);
            var cert = vm.Certificados;
            await cert.CarregarAsync();
            Assert.True(cert.PodeAlterar);
            var pfx = Path.Combine(Path.GetTempPath(), "vorticis_cert_" + Guid.NewGuid().ToString("N") + ".pfx");
            await File.WriteAllBytesAsync(pfx, Infra.Tests.CertificadosTests.Pfx("EXEMPLO COMERCIO DE INFORMATICA LTDA:11222333000181", "1234",
                DateTimeOffset.UtcNow.AddDays(15)));
            (cert.Arquivo, cert.Senha) = (pfx, "errada");
            cert.ConferirCommand.Execute(null);
            Assert.Contains("senha incorreta", Assert.Single(cert.Erros));
            cert.Senha = "1234";
            cert.ConferirCommand.Execute(null);
            Assert.StartsWith("EXEMPLO COMERCIO DE INFORMATICA LTDA — 11.222.333/0001-81", cert.Conferencia);
            await cert.ImportarCommand.ExecuteAsync(null);
            Assert.Empty(cert.Erros);
            var certLinha = Assert.Single(cert.Lista);
            Assert.Equal(("A1", "11.222.333/0001-81"), (certLinha.Tipo, certLinha.Documento));
            Assert.Contains("vence em", cert.AvisoVencimento);
            Foto(janela, "09b_certificados");
            File.Delete(pfx);

            // Relatórios › Contábeis › Livro Contábil Completo e Termos
            var pastaPdf = Path.Combine(Path.GetTempPath(), "vorticis_pdf_" + Guid.NewGuid().ToString("N"));
            (ArquivosGerados.AbrirAposGerar, ArquivosGerados.Pasta) = (false, pastaPdf);
            vm.AbrirCommand.Execute(Telas.LivrosPdf);
            var livros = vm.LivrosPdf;
            await livros.PreencherResponsaveisAsync();
            // termos preenchidos a partir dos cadastros de sócios e contadores
            Assert.Equal(("FULANO DE TAL", "390.533.447-05", "Maria Contadora", "1SP123456/O-7"),
                         (livros.Representante, livros.RepresentanteCpf, livros.Contador, livros.Crc));
            (livros.Inicio, livros.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            Assert.Equal(livros.SemNire, livros.AvisoNire.Contains("NIRE"));    // NIRE vem do cadastro; sem ele, aparece o aviso
            await livros.GerarLivroCommand.ExecuteAsync(null);
            Assert.Empty(livros.Erros);
            Assert.True(File.Exists(livros.UltimoArquivo));
            livros.IncluirRazao = livros.IncluirBalancete = false;              // livro parcial, só o que foi marcado
            await livros.GerarLivroCommand.ExecuteAsync(null);
            Assert.Empty(livros.Erros);
            vm.AbrirCommand.Execute(Telas.Termos);
            var termos = vm.Termos;
            await termos.PreencherResponsaveisAsync();
            (termos.Inicio, termos.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            await termos.GerarAberturaCommand.ExecuteAsync(null);
            Assert.Empty(termos.Erros);
            Assert.True(File.Exists(termos.UltimoArquivo));
            await termos.GerarEncerramentoCommand.ExecuteAsync(null);
            Assert.Empty(termos.Erros);
            Foto(janela, "08c_livros_pdf");
            if (Pasta is not null) foreach (var pdf in Directory.GetFiles(pastaPdf)) File.Copy(pdf, Path.Combine(Pasta, Path.GetFileName(pdf)), true);
            Directory.Delete(pastaPdf, true);

            // Configurações › Plano referencial da RFB (tabela FICTÍCIA de teste) e Cadastros › Vínculo ao plano referencial
            var arquivoRef = Path.Combine(Path.GetTempPath(), "referencial_ficticio_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(arquivoRef, PlanoReferencialTests.TabelaFicticia, System.Text.Encoding.Latin1);
            vm.AbrirCommand.Execute(Telas.ImportacaoReferencial);
            var imp = vm.ImportacaoReferencial;
            (imp.Ano, imp.Arquivo) = (2026, arquivoRef);
            await imp.ImportarCommand.ExecuteAsync(null);
            Assert.Empty(imp.Erros);
            Assert.Single(imp.Tabelas);
            Foto(janela, "08d_importacao_referencial");
            File.Delete(arquivoRef);

            vm.AbrirCommand.Execute(Telas.MapeamentoReferencial);
            var map = vm.MapeamentoReferencial;
            map.Ano = 2026;
            await map.CarregarAsync();
            await map.UsarPlanoCommand.ExecuteAsync(null);
            Assert.Empty(map.Erros);
            Assert.Contains("9 conta(s) com movimento sem vínculo", map.Resumo);   // inclui as 2 contas dos lançamentos importados
            map.SomentePendentes = true;
            await map.SugerirCommand.ExecuteAsync(null);
            Assert.Contains(map.Linhas, l => l.Codigo == "1.1.01.001" && l.SugestaoTexto.StartsWith("1.01.01"));
            Foto(janela, "08e_vinculo_sugestoes");
            await map.AceitarSugestoesCommand.ExecuteAsync(null);
            Assert.Empty(map.Erros);
            map.SomentePendentes = false;
            map.Selecionada = map.Linhas.First(l => l.Pendente);
            map.FiltroReferencial = "";
            map.ReferencialEscolhida = map.Opcoes.First();
            await map.VincularCommand.ExecuteAsync(null);
            Assert.Empty(map.Erros);
            Foto(janela, "08f_vinculo");

            // 6. Configurações › Usuários, aba Perfis de acesso: perfil personalizado só com Razão e Diário
            vm.AbrirCommand.Execute(Telas.Perfis);
            Assert.Same(vm.CentralUsuarios, vm.Conteudo);
            var perfis = vm.Perfis;
            await perfis.CarregarAsync();
            Assert.Equal(["Administrador", "Assistente", "Consulta", "Contador"], perfis.Lista.Select(p => p.Nome).Order());
            perfis.Selecionado = perfis.Lista.Single(p => p.Nome == "Administrador");
            Assert.True(perfis.EditandoAdministrador);
            Assert.All(perfis.Grupos.SelectMany(g => g.Funcoes), f => Assert.True(f.Marcada));   // todas as funções liberadas
            Assert.False(perfis.PodeMarcar);                        // Administrador: sempre todas
            perfis.IncluirCommand.Execute(null);
            (perfis.Nome, perfis.Descricao) = ("Auditor externo", "Consulta Razão e Diário");
            foreach (var f in perfis.Grupos.SelectMany(g => g.Funcoes))
                f.Marcada = f.Definicao.Permissao is Permissao.RelatorioRazao or Permissao.RelatorioDiario;
            await perfis.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(perfis.Erros);
            Foto(janela, "08_perfis");

            // Aba Usuários: cria João informando o perfil "Auditor externo" (vale para todas as empresas)
            vm.AbrirCommand.Execute(Telas.Usuarios);
            Assert.Equal(CentralUsuariosViewModel.AbaUsuarios, vm.CentralUsuarios.AbaSelecionada);
            var u = vm.Usuarios;
            await u.CarregarAsync();
            u.IncluirCommand.Execute(null);
            (u.Login, u.NomeEdicao, u.NovaSenha) = ("joao", "João Auditor", "Inicial123");
            await u.SalvarCommand.ExecuteAsync(null);
            Assert.Contains("Informe o perfil", u.Erros.Single());       // perfil obrigatório
            Assert.True(u.EmInclusao);
            u.PerfilEdicao = u.PerfisDisponiveis.Single(o => o.Nome == "Auditor externo");
            await u.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(u.Erros);
            Assert.Equal("Auditor externo", u.Selecionado?.Perfil);
            Assert.Equal("Auditor externo", u.PerfilEdicao?.Nome);
            for (int i = 0; i < 50 && u.Acessos.Count == 0; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            var acesso = Assert.Single(u.Acessos);
            Assert.Equal("Auditor externo", acesso.Perfil.Nome);          // perfil já concedido na empresa
            Assert.True(acesso.Marcada);                                  // lista de empresas: a do usuário vem marcada
            Assert.Equal("1 de 1 empresa(s) liberada(s)", u.ResumoEmpresas);
            u.AlterarCommand.Execute(null);
            u.DesmarcarTodasCommand.Execute(null);
            u.FiltroEmpresas = "zzz inexistente";
            Assert.Empty(u.AcessosFiltrados);
            u.FiltroEmpresas = acesso.Empresa[..5].ToLowerInvariant();
            Assert.Single(u.AcessosFiltrados);
            u.FiltroEmpresas = "";
            Assert.False(acesso.Marcada);
            await u.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(u.Erros);
            Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM core.usuario_empresa ue JOIN core.usuario u ON u.id = ue.usuario_id WHERE u.login = 'joao'"));
            Assert.False(await db.EscalarAsync<bool>("SELECT todas_empresas FROM core.usuario WHERE login = 'joao'"));
            for (int i = 0; i < 50 && u.Acessos.Count == 0; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            u.AlterarCommand.Execute(null);
            u.MarcarTodasCommand.Execute(null);                           // volta a liberar (o restante do fluxo usa o João na empresa)
            Assert.Equal("1 de 1 empresa(s) liberada(s)", u.ResumoEmpresas);
            await u.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(u.Erros);
            Assert.True(await db.EscalarAsync<bool>("SELECT todas_empresas FROM core.usuario WHERE login = 'joao'"));
            for (int i = 0; i < 50 && u.Acessos.Count == 0; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            acesso = Assert.Single(u.Acessos);
            Assert.True(acesso.Marcada);
            u.AlterarCommand.Execute(null);
            await u.SalvarCommand.ExecuteAsync(null);
            Assert.Equal("Alterações salvas.", u.Mensagem);
            Foto(janela, "09_usuarios");

            // Aba Registro de acessos
            vm.CentralUsuarios.AbaSelecionada = CentralUsuariosViewModel.AbaRegistro;
            await vm.CentralUsuarios.CarregarRegistrosAsync();
            Assert.Contains(vm.CentralUsuarios.Registros, r => r.Login == "maria" && r.Resultado == "Entrou");
            Foto(janela, "09b_registro_acessos");

            // Backup
            vm.AbrirCommand.Execute(Telas.Backup);
            await vm.Backup.CarregarAsync();
            Assert.Equal(Application.Sistema.ServicoBackup.PastaPadrao, vm.Backup.Pasta);     // padrão: diretório de instalação
            Assert.StartsWith(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), vm.Backup.Pasta);
            var pastaBackup = Path.Combine(Path.GetTempPath(), "vorticis_ui_" + Guid.NewGuid().ToString("N"));
            vm.Backup.Pasta = pastaBackup;
            await vm.Backup.SalvarPastaCommand.ExecuteAsync(null);                         // pasta escolhida fica gravada
            Assert.Empty(vm.Backup.Erros);
            Assert.Equal(pastaBackup, await vm.App!.ConfiguracoesGerais.PastaBackupAsync());
            await vm.Backup.GerarCommand.ExecuteAsync(null);
            Assert.Empty(vm.Backup.Erros);
            vm.Backup.Selecionado = Assert.Single(vm.Backup.Arquivos);
            Foto(janela, "10_backup");
            var restaurando = vm.Backup.RestaurarCommand.ExecuteAsync(null);              // restaurar pede a senha de administrador
            var alerta = await AguardarConfirmacao(vm);
            Assert.True(alerta.PedeSenha);
            alerta.Senha = "errada";
            vm.ResponderConfirmacaoCommand.Execute(true);
            await restaurando;
            Assert.Equal(["Senha de administrador incorreta. Nada foi restaurado."], vm.Backup.Erros);
            var excluindo = vm.Backup.ExcluirCommand.ExecuteAsync(null);                  // excluir o backup gerado
            Assert.Contains("Excluir definitivamente", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            await excluindo;
            Assert.Empty(vm.Backup.Erros);
            Assert.Empty(vm.Backup.Arquivos);
            await vm.Backup.UsarPastaPadraoCommand.ExecuteAsync(null);
            Assert.Equal(Application.Sistema.ServicoBackup.PastaPadrao, await vm.App!.ConfiguracoesGerais.PastaBackupAsync());
            Directory.Delete(pastaBackup, true);

            // Janelas flutuantes: cada função aberta tem a sua; a barra lista as abertas e o ✕ fecha
            Assert.True(vm.JanelasAbertas.Count >= 5);
            Assert.Same(vm.Backup, vm.FuncaoAtiva?.ViewModel);
            Assert.NotNull(janela.JanelaDa(vm.Backup));
            Dispatcher.UIThread.RunJobs();
            var quadroPrincipal = janela.CaptureRenderedFrame();
            if (Pasta is not null) quadroPrincipal?.Save(Path.Combine(Pasta, "10b_janelas_abertas.png"));
            var janelaBackup = vm.FuncaoAtiva!;
            vm.FecharFuncaoCommand.Execute(janelaBackup);
            Assert.DoesNotContain(janelaBackup, vm.JanelasAbertas);
            Assert.Null(janela.JanelaDa(vm.Backup));
            Assert.NotSame(vm.Backup, vm.Conteudo);
            vm.AbrirCommand.Execute(Telas.Balancete);                         // reabrir traz a mesma janela para a frente
            var antes = vm.JanelasAbertas.Count;
            vm.AbrirCommand.Execute(Telas.Balancete);
            Assert.Equal(antes, vm.JanelasAbertas.Count);
            var balanceteAntigo = vm.Balancete;                               // fechar e abrir de novo: recomeça do zero
            Assert.NotEqual("", balanceteAntigo.Totais);
            vm.FecharFuncaoCommand.Execute(vm.JanelasAbertas.Single(j => j.Tela == Telas.Balancete));
            vm.AbrirCommand.Execute(Telas.Balancete);
            Assert.NotSame(balanceteAntigo, vm.Balancete);
            Assert.Same(vm.Balancete, vm.Conteudo);
            Assert.Equal("", vm.Balancete.Totais);
            Assert.Empty(vm.Balancete.Linhas);

            // Ajuda › Licença e registro
            vm.AbrirCommand.Execute(Telas.Licenca);
            await vm.Licenca.CarregarAsync();
            Assert.Contains("não registrada", vm.Licenca.Situacao);
            (vm.Licenca.Titular, vm.Licenca.Documento) = ("Escritório Exemplo Contabilidade", "45.723.174/0001-10");
            await vm.Licenca.RegistrarCommand.ExecuteAsync(null);
            Assert.Empty(vm.Licenca.Erros);
            Assert.Equal("Registrado para: Escritório Exemplo Contabilidade — 45.723.174/0001-10", vm.Licenca.Situacao);
            Foto(janela, "11b_licenca");

            vm.AbrirCommand.Execute(Telas.Sobre);
            Foto(janela, "11_sobre");

            // 7. Sai e entra como João: troca obrigatória de senha e perfil personalizado
            vm.SairCommand.Execute(null);
            Assert.False(vm.Logado);
            login = Assert.IsType<LoginViewModel>(vm.Conteudo);
            Assert.Equal("maria", login.Usuario);                            // login salvo já vem preenchido
            Assert.True(login.SalvarLogin);
            (login.Usuario, login.Senha) = ("joao", "errada123");
            await login.EntrarCommand.ExecuteAsync(null);
            Assert.Equal(["Usuário ou senha inválidos."], login.Erros);
            Foto(janela, "12_login_erro");

            login.Senha = "Inicial123";
            await login.EntrarCommand.ExecuteAsync(null);
            Assert.True(login.EmTrocaObrigatoria);
            (login.Senha, login.Confirmacao) = ("NovaSenha9", "NovaSenha9");
            await login.TrocarSenhaCommand.ExecuteAsync(null);
            Assert.True(vm.Logado, string.Join("; ", login.Erros));

            // Menu reflete exatamente o perfil
            Assert.True(vm.AbrirCommand.CanExecute(Telas.Razao));
            Assert.True(vm.AbrirCommand.CanExecute(Telas.Diario));
            Assert.True(vm.AbrirCommand.CanExecute(Telas.PlanoContas));     // consulta do plano
            foreach (var proibida in new[] { Telas.Balancete, Telas.Lancamento, Telas.Estorno, Telas.SaldosIniciais, Telas.Periodos,
                                             Telas.Empresas, Telas.Perfis, Telas.Backup, Telas.Conexao })
                Assert.False(vm.AbrirCommand.CanExecute(proibida), proibida);
            Assert.Same(vm.Inicio, vm.Conteudo);                             // entra pela tela de descanso
            Assert.Null(vm.Inicio.Dica);
            vm.AbrirCommand.Execute(Telas.Usuarios);                         // usuário comum: só a troca da própria senha
            Assert.Equal(CentralUsuariosViewModel.AbaSenha, vm.CentralUsuarios.AbaSelecionada);
            Assert.False(vm.CentralUsuarios.PodeAdministrar);
            Assert.False(vm.CentralUsuarios.PodePerfis);
            vm.AbrirCommand.Execute(Telas.Razao);
            Assert.Equal("", vm.Balancete.Totais);                           // nada da sessão anterior fica visível

            (vm.Razao.Inicio, vm.Razao.Fim) = (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
            await vm.Razao.CarregarContasAsync();                           // painel de contas analíticas
            vm.Razao.FiltroConta = "bancos conta";
            vm.Razao.ContaSelecionada = Assert.Single(vm.Razao.Contas);
            await vm.Razao.GerarCommand.ExecuteAsync(null);
            Assert.Equal("1.1.01.002", vm.Razao.Conta);
            Assert.Single(vm.Razao.Linhas);
            Foto(janela, "12b_razao_contas");

            vm.AbrirCommand.Execute(Telas.PlanoContas);
            await vm.PlanoContas.CarregarAsync();
            Assert.False(vm.PlanoContas.PodeEditar);
            Assert.False(vm.PlanoContas.IncluirCommand.CanExecute(null));    // botões de manutenção desabilitados
            vm.PlanoContas.Modo = ModoCadastro.Inclusao;                      // mesmo forçando, o serviço recusa
            vm.PlanoContas.Codigo = "1.1.01.005";
            vm.PlanoContas.Descricao = "Tentativa";
            await vm.PlanoContas.SalvarCommand.ExecuteAsync(null);
            Assert.Contains(vm.PlanoContas.Erros, e => e.Contains("não permite"));
            Foto(janela, "13_perfil_restrito");

            janela.Close();                                                 // fechar pede confirmação
            Assert.Contains("Deseja realmente fechar", (await AguardarConfirmacao(vm)).Mensagem);
            vm.ResponderConfirmacaoCommand.Execute(true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(janela.IsVisible);
            await vm.DisposeAsync();
            LoginSalvo.Esquecer();
            return true;
        }, TestContext.Current.CancellationToken));
        Assert.True(concluido);
    }
}
