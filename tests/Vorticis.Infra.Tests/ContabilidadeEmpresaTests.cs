using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;

namespace Vorticis.Infra.Tests;

/// <summary>Aba Contabilidade do cadastro da empresa: modelo de plano, NIRE, configurações e grupo dos participantes.</summary>
public class ContabilidadeEmpresaTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static NovaEmpresa Nova(string cnpj, string? nire = null, string? modelo = null, ConfiguracaoContabil? cfg = null, bool importar = true) =>
        new(cnpj, "Empresa Contábil LTDA", null, "2062", "4751201", "SP", "3550308", new DateOnly(2020, 1, 1), RegimeTributario.LucroPresumido, 2026,
            importar, Nire: nire, ModeloPlano: modelo, Contabil: cfg);

    [Fact]
    public async Task Cadastro_grava_nire_modelo_configuracao_e_usa_as_contas_do_modelo_no_encerramento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();

        Assert.Contains("NIRE", (await db.App.Empresas.CadastrarAsync(Nova("60606060000184", nire: "123"), ct)).Erros[0]);
        Assert.Contains("Modelo de plano", (await db.App.Empresas.CadastrarAsync(Nova("60606060000184", modelo: "nao_existe"), ct)).Erros[0]);
        Assert.Contains("Máscara", string.Join(" ", (await db.App.Empresas.CadastrarAsync(
            Nova("60606060000184", cfg: new ConfiguracaoContabil(MascaraPlano: "9.9.9.9.9.9.9.9.9.9.9")), ct)).Erros));

        var r = await db.App.Empresas.CadastrarAsync(Nova("60606060000184", nire: "35.2.0123456-7", modelo: "comercio_presumido",
            cfg: new ConfiguracaoContabil(MascaraPlano: "9.9.99.999", InicioEscrituracao: new DateOnly(2026, 1, 1), UsaCentroCusto: true, GrandePorte: true,
                                          ApuracaoIrpj: ConfiguracaoContabil.Trimestral)), ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));

        Assert.Equal("35201234567", (await db.App.Empresas.ObterAsync(r.Valor, ct))!.Nire);
        var modelo = db.App.Empresas.ListarModelosPlano().Single(m => m.Id == "comercio_presumido");
        var cfg = await db.App.PlanoContas.ObterConfiguracaoAsync(r.Valor, adm, ct);
        Assert.Equal(("comercio_presumido", "9.9.99.999", true, true), (cfg.ModeloPlano, cfg.MascaraPlano, cfg.UsaCentroCusto, cfg.GrandePorte));
        Assert.Equal(new DateOnly(2026, 1, 1), cfg.InicioEscrituracao);
        Assert.Equal(ConfiguracaoContabil.Trimestral, cfg.ApuracaoIrpj);
        // contas de encerramento: as do modelo, que existem no plano criado
        Assert.Equal((modelo.Contas.Apuracao, modelo.Contas.LucrosAcumulados, modelo.Contas.PrejuizosAcumulados), (cfg.ContaApuracao, cfg.ContaLucros, cfg.ContaPrejuizos));
        var plano = await db.App.PlanoContas.ListarAsync(r.Valor, adm, ct);
        Assert.Equal(modelo.QuantidadeContas, plano.Count);
        Assert.All(new[] { cfg.ContaApuracao, cfg.ContaLucros, cfg.ContaPrejuizos }, c => Assert.True(plano.Single(x => x.Codigo == c).Analitica));

        // alterar o cadastro mantém e atualiza o NIRE
        var det = (await db.App.Empresas.ObterAsync(r.Valor, ct))!;
        var alt = new AlteracaoEmpresa(det.Id, det.RazaoSocial, det.NomeFantasia, det.NaturezaJuridica, det.CnaePrincipal, det.Uf, det.CodMunicipioIbge,
                                       det.DataInicioAtividades!.Value, det.InscricaoEstadual, null, Nire: "");
        Assert.True((await db.App.Empresas.AlterarAsync(alt, ct)).Sucesso);
        Assert.Null((await db.App.Empresas.ObterAsync(r.Valor, ct))!.Nire);
    }

    [Fact]
    public async Task Salvar_configuracao_valida_as_contas_no_plano_e_o_modelo_so_troca_antes_do_primeiro_lancamento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("61616161000107");      // plano básico
        var svc = db.App.PlanoContas;

        var cfg = await svc.ObterConfiguracaoAsync(empresa, adm, ct);
        Assert.Equal("basico", cfg.ModeloPlano);
        Assert.Equal("5.1.01.001", cfg.ContaApuracao);

        var inexistente = await svc.SalvarConfiguracaoAsync(empresa, cfg with { ContaLucros = "9.9.9.999" }, new DateOnly(2020, 1, 1), adm, ct);
        Assert.Contains("não existe no plano", inexistente.Erros[0]);
        var sintetica = await svc.SalvarConfiguracaoAsync(empresa, cfg with { ContaLucros = "2.3.03" }, new DateOnly(2020, 1, 1), adm, ct);
        Assert.Contains("não é analítica", sintetica.Erros[0]);
        Assert.Contains("anterior ao início", (await svc.SalvarConfiguracaoAsync(empresa, cfg with { InicioEscrituracao = new DateOnly(2019, 1, 1) },
                                                                              new DateOnly(2020, 1, 1), adm, ct)).Erros[0]);
        Assert.True((await svc.SalvarConfiguracaoAsync(empresa, cfg with { GrandePorte = true, UsaCentroCusto = true, ContaPrejuizos = "2.3.03.001" },
                                                       new DateOnly(2020, 1, 1), adm, ct)).Sucesso);
        var salva = await svc.ObterConfiguracaoAsync(empresa, adm, ct);
        Assert.Equal((true, true, "2.3.03.001", "basico"), (salva.GrandePorte, salva.UsaCentroCusto, salva.ContaPrejuizos, salva.ModeloPlano));

        // sem lançamentos: o modelo pode ser trocado, e as contas de encerramento passam a ser as dele
        Assert.False((await svc.AplicarModeloAsync(empresa, "nao_existe", adm, ct)).Sucesso);
        var aplicado = await svc.AplicarModeloAsync(empresa, "servicos_simples", adm, ct);
        Assert.True(aplicado.Sucesso, string.Join("; ", aplicado.Erros));
        var modelo = svc.ListarModelos().Single(m => m.Id == "servicos_simples");
        Assert.Equal(modelo.QuantidadeContas, aplicado.Valor);
        cfg = await svc.ObterConfiguracaoAsync(empresa, adm, ct);
        Assert.Equal(("servicos_simples", modelo.Contas.LucrosAcumulados, modelo.Contas.PrejuizosAcumulados), (cfg.ModeloPlano, cfg.ContaLucros, cfg.ContaPrejuizos));
        Assert.True(cfg.GrandePorte);       // demais configurações preservadas

        // com lançamento, a troca é recusada
        var contas = (await svc.ListarAsync(empresa, adm, ct));
        var debito = contas.First(c => c.Analitica && c.CodNat == "01" && c.NaturezaSaldo == IndicadorDC.D).Codigo;
        var credito = contas.First(c => c.Analitica && c.CodNat == "04" && c.NaturezaSaldo == IndicadorDC.C).Codigo;
        var lanc = await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 3, 10), OrigemLancamento.Manual,
            [new(debito, IndicadorDC.D, 100m, "Teste"), new(credito, IndicadorDC.C, 100m, "Teste")]), adm, ct);
        Assert.True(lanc.Sucesso, string.Join("; ", lanc.Erros));
        Assert.Contains("lançamentos", (await svc.AplicarModeloAsync(empresa, "comercio_simples", adm, ct)).Erros[0]);
    }

    [Fact]
    public async Task Conta_que_exige_participante_indica_o_grupo_sintetico_e_o_cadastro_de_participantes_o_respeita()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("62626262000113");
        var svc = db.App.PlanoContas;
        var plano = await svc.ListarAsync(empresa, adm, ct);
        var clientes = plano.Single(c => c.Codigo == "1.1.02.001");
        var grupoCorreto = plano.Single(c => c.Codigo == "1.1.02");
        var grupoPassivo = plano.Single(c => c.Codigo == "2.1");

        Task<Resultado> Marcar(long? grupo) => svc.AlterarAsync(empresa, clientes.Id, clientes.Descricao, clientes.NaturezaSaldo, false, true, true, adm, ct, grupo);
        Assert.Contains("Informe o grupo", (await Marcar(null)).Erros[0]);
        Assert.Contains("sintética", (await Marcar(clientes.Id)).Erros[0]);
        Assert.Contains("outra natureza", (await Marcar(grupoPassivo.Id)).Erros[0]);
        Assert.True((await Marcar(grupoCorreto.Id)).Sucesso);
        Assert.Equal(grupoCorreto.Id, (await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == clientes.Id).GrupoParticipantesId);

        // desmarcar limpa o grupo
        Assert.True((await svc.AlterarAsync(empresa, clientes.Id, clientes.Descricao, clientes.NaturezaSaldo, false, false, true, adm, ct)).Sucesso);
        Assert.Null((await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == clientes.Id).GrupoParticipantesId);

        // conta nova que exige participante também precisa do grupo
        var nova = new NovaConta("1.1.02.090", "Clientes diversos", "01", IndicadorDC.D, false, true, new DateOnly(2026, 1, 1));
        Assert.Contains("Informe o grupo", (await svc.IncluirAsync(empresa, nova, adm, ct)).Erros[0]);
        Assert.True((await svc.IncluirAsync(empresa, nova with { GrupoParticipantesId = grupoCorreto.Id }, adm, ct)).Sucesso);

        // participante alocado numa conta sem grupo definido: o grupo passa a ser a conta superior
        var fornecedores = plano.Single(c => c.Codigo == "2.1.01.001");
        Assert.True((await db.App.Participantes.DefinirUsaParticipantesAsync(empresa, true, adm, ct)).Sucesso);
        var forn = new Participante(0, "", "Fornecedor Beta", null, "11.222.333/0001-81", null, "SP", null, null, null, null, false, true,
                                    "2.1.01.001", true, ControleConta: true);
        var g = await db.App.Participantes.GravarAsync(empresa, forn, adm, ct);
        Assert.True(g.Sucesso, string.Join("; ", g.Erros));
        var conta = (await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == fornecedores.Id);
        Assert.True(conta.ExigeParticipante);
        Assert.Equal(conta.ContaPaiId, conta.GrupoParticipantesId);
        // desligar o uso de participantes limpa o grupo
        Assert.True((await db.App.Participantes.DefinirUsaParticipantesAsync(empresa, false, adm, ct)).Sucesso);
        Assert.Null((await svc.ListarAsync(empresa, adm, ct)).Single(c => c.Id == fornecedores.Id).GrupoParticipantesId);
    }
}
