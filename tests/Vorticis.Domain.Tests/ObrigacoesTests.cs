using Vorticis.Application.Obrigacoes;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Obrigacoes;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Domain.Tests;

public class ObrigacoesTests
{
    private static DateOnly D(int a, int m, int d) => new(a, m, d);
    private static DefinicaoObrigacao Def(string cod) => CatalogoObrigacoes.Federais.Single(x => x.Codigo == cod);

    [Fact]
    public void Feriados_moveis_2026_e_2027()
    {
        Assert.Equal(D(2026, 4, 5), CalendarioDiasUteis.Pascoa(2026));
        Assert.Equal(D(2027, 3, 28), CalendarioDiasUteis.Pascoa(2027));
        var f26 = CalendarioDiasUteis.Feriados(2026).ToDictionary(f => f.Nome, f => f.Data);
        Assert.Equal(D(2026, 2, 16), f26["Carnaval (segunda-feira)"]);
        Assert.Equal(D(2026, 2, 17), f26["Carnaval (terça-feira)"]);
        Assert.Equal(D(2026, 4, 3), f26["Sexta-feira Santa"]);
        Assert.Equal(D(2026, 6, 4), f26["Corpus Christi"]);
        var f27 = CalendarioDiasUteis.Feriados(2027).ToDictionary(f => f.Nome, f => f.Data);
        Assert.Equal(D(2027, 2, 8), f27["Carnaval (segunda-feira)"]);
        Assert.Equal(D(2027, 2, 9), f27["Carnaval (terça-feira)"]);
        Assert.Equal(D(2027, 3, 26), f27["Sexta-feira Santa"]);
        Assert.Equal(D(2027, 5, 27), f27["Corpus Christi"]);
        Assert.Contains(CalendarioDiasUteis.Feriados(2026), f => f.Data == D(2026, 11, 20)); // Consciência Negra (nacional desde 2024)
        Assert.DoesNotContain(CalendarioDiasUteis.Feriados(2023), f => f.Data == D(2023, 11, 20));
    }

    [Fact]
    public void Dias_uteis_ignoram_fim_de_semana_feriado_e_carnaval_mas_nao_a_quarta_de_cinzas()
    {
        Assert.False(CalendarioDiasUteis.EhDiaUtil(D(2026, 2, 17)));
        Assert.True(CalendarioDiasUteis.EhDiaUtil(D(2026, 2, 18)));
        Assert.False(CalendarioDiasUteis.EhDiaUtil(D(2026, 6, 20))); // sábado
        Assert.Equal(D(2026, 6, 22), CalendarioDiasUteis.ProximoDiaUtil(D(2026, 6, 20)));
        Assert.Equal(D(2026, 6, 19), CalendarioDiasUteis.DiaUtilAnterior(D(2026, 6, 20)));
        Assert.Equal(D(2026, 12, 31), CalendarioDiasUteis.UltimoDiaUtil(2026, 12));
        Assert.Equal(D(2026, 5, 29), CalendarioDiasUteis.UltimoDiaUtil(2026, 5));
    }

    [Fact]
    public void Das_competencia_03_2026_vence_em_20_04_e_posterga_quando_sabado()
    {
        Assert.Equal(D(2026, 4, 20), Def("DAS").Vencimento(new(2026, 3)));
        Assert.Equal(D(2026, 6, 22), Def("DAS").Vencimento(new(2026, 5))); // 20/06/2026 é sábado
    }

    [Fact]
    public void Dctfweb_e_darfs_antecipam()
    {
        Assert.Equal(D(2026, 6, 19), Def("DCTFWEB").Vencimento(new(2026, 5)));
        Assert.Equal(D(2026, 6, 19), Def("IRRF").Vencimento(new(2026, 5)));
        Assert.Equal(D(2026, 7, 20), Def("DCTFWEB").Vencimento(new(2026, 6)));
    }

    [Fact]
    public void Pis_cofins_antecipa_em_mes_com_feriado_ou_fim_de_semana()
    {
        Assert.Equal(D(2026, 12, 24), Def("PIS_COFINS").Vencimento(new(2026, 11))); // 25/12 é Natal
        Assert.Equal(D(2026, 4, 24), Def("PIS_COFINS").Vencimento(new(2026, 3)));   // 25/04 é sábado
        Assert.Equal(D(2026, 6, 25), Def("PIS_COFINS").Vencimento(new(2026, 5)));
    }

    [Fact]
    public void Ecd_ecf_defis_em_2026()
    {
        Assert.Equal(D(2026, 6, 30), Def("ECD").Vencimento(new(2025, 0)));
        Assert.Equal(D(2026, 7, 31), Def("ECF").Vencimento(new(2025, 0)));
        Assert.Equal(D(2026, 3, 31), Def("DEFIS").Vencimento(new(2025, 0)));
        Assert.Equal(D(2026, 11, 30), Def("DECIMO_1").Vencimento(new(2026, 0)));
        Assert.Equal(D(2026, 12, 18), Def("DECIMO_2").Vencimento(new(2026, 0))); // 20/12/2026 é domingo
    }

    [Fact]
    public void Dctf_e_efd_contribuicoes_contam_dias_uteis_do_segundo_mes()
    {
        Assert.Equal(D(2026, 5, 22), Def("DCTF").Vencimento(new(2026, 3)));          // maio/2026 tem 1º de maio
        Assert.Equal(D(2026, 5, 15), Def("EFD_CONTRIB").Vencimento(new(2026, 3)));
        Assert.Equal(D(2026, 2, 24), Def("DCTF").Vencimento(new(2025, 12)));        // Carnaval em 16 e 17/02/2026
    }

    [Fact]
    public void Irpj_csll_trimestral_em_cotas()
    {
        var c = new Competencia(2026, 3);
        Assert.Equal(D(2026, 4, 30), Def("IRPJ_CSLL_C1").Vencimento(c));
        Assert.Equal(D(2026, 5, 29), Def("IRPJ_CSLL_C2").Vencimento(c));
        Assert.Equal(D(2026, 6, 30), Def("IRPJ_CSLL_C3").Vencimento(c));
        Assert.Equal(D(2027, 1, 29), Def("IRPJ_CSLL_C1").Vencimento(new(2026, 12)));
    }

    [Fact]
    public void Ocorrencias_do_periodo_respeitam_periodicidade_e_vigencia()
    {
        var abril = CalendarioObrigacoes.Ocorrencias(CatalogoObrigacoes.Federais, D(2026, 4, 1), D(2026, 4, 30));
        Assert.Contains(abril, o => o.Definicao.Codigo == "DAS" && o.Competencia == new Competencia(2026, 3) && o.Vencimento == D(2026, 4, 20));
        Assert.Contains(abril, o => o.Definicao.Codigo == "IRPJ_CSLL_C1" && o.Competencia == new Competencia(2026, 3));
        Assert.DoesNotContain(abril, o => o.Definicao.Codigo == "IRPJ_CSLL_C2" && o.Competencia.Mes != 3);
        Assert.Equal(abril.OrderBy(o => o.Vencimento).Select(o => o.Vencimento), abril.Select(o => o.Vencimento));
        Assert.DoesNotContain(CalendarioObrigacoes.Ocorrencias([Def("DIRF")], D(2026, 1, 1), D(2026, 12, 31)), _ => true);
        Assert.Single(CalendarioObrigacoes.Ocorrencias([Def("DIRF")], D(2025, 1, 1), D(2025, 12, 31)));
    }

    [Fact]
    public void Filtros_por_regime_e_empregados()
    {
        Assert.True(Def("DAS").AplicaA(RegimesObrigacao.Simples, false));
        Assert.False(Def("DAS").AplicaA(RegimesObrigacao.Presumido, true));
        Assert.False(Def("ECD").AplicaA(RegimesObrigacao.Simples, true));
        Assert.False(Def("FGTS").AplicaA(RegimesObrigacao.Simples, false));
        Assert.True(Def("FGTS").AplicaA(RegimesObrigacao.Simples, true));
        Assert.True(Def("IRPJ_CSLL_EST").AplicaA(RegimesObrigacao.Real, false));
        Assert.False(Def("IRPJ_CSLL_EST").AplicaA(RegimesObrigacao.Presumido, false));
    }

    // ------------------------------------------------------------- serviço de status

    private sealed class Perm(PermissoesEfetivas p) : IRepositorioPermissoes
    {
        public Task<PermissoesEfetivas> ObterAsync(long usuarioId, long empresaId, CancellationToken ct) => Task.FromResult(p);
    }

    private sealed class RepoFalso(PerfilObrigacoesEmpresa perfil) : IRepositorioObrigacoes
    {
        public List<BaixaObrigacao> Baixas { get; } = [];
        public List<ObrigacaoPersonalizada> Pers { get; } = [];
        public Task<PerfilObrigacoesEmpresa> ObterPerfilAsync(long e, CancellationToken ct) => Task.FromResult(perfil);
        public Task<IReadOnlyList<ObrigacaoPersonalizada>> ListarPersonalizadasAsync(long e, CancellationToken ct) => Task.FromResult<IReadOnlyList<ObrigacaoPersonalizada>>(Pers.ToList());
        public Task<long> SalvarPersonalizadaAsync(long e, ObrigacaoPersonalizada o, CancellationToken ct) { Pers.Add(o with { Id = Pers.Count + 1 }); return Task.FromResult((long)Pers.Count); }
        public Task ExcluirPersonalizadaAsync(long e, long id, CancellationToken ct) { Pers.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
        public Task<IReadOnlyList<BaixaObrigacao>> ListarBaixasAsync(long e, CancellationToken ct) => Task.FromResult<IReadOnlyList<BaixaObrigacao>>(Baixas.ToList());
        public Task SalvarBaixaAsync(long e, BaixaObrigacao b, long u, CancellationToken ct) { Baixas.RemoveAll(x => x.Obrigacao == b.Obrigacao && x.Competencia == b.Competencia); Baixas.Add(b); return Task.CompletedTask; }
        public Task RemoverBaixaAsync(long e, string o, string c, CancellationToken ct) { Baixas.RemoveAll(x => x.Obrigacao == o && x.Competencia == c); return Task.CompletedTask; }
    }

    private static ServicoObrigacoes Servico(RepoFalso repo, bool podeManter, bool acesso = true) =>
        new(repo, new Autorizador(new Perm(acesso
            ? new PermissoesEfetivas(false, podeManter ? new HashSet<Permissao> { Permissao.ObrigacoesManter } : new HashSet<Permissao>(), "Perfil")
            : PermissoesEfetivas.Nenhuma)));

    [Fact]
    public void Status_pendente_cumprida_atrasada()
    {
        var hoje = D(2026, 4, 21);
        Assert.Equal(StatusObrigacao.Atrasada, ServicoObrigacoes.CalcularStatus(D(2026, 4, 20), null, hoje));
        Assert.Equal(StatusObrigacao.Pendente, ServicoObrigacoes.CalcularStatus(D(2026, 4, 21), null, hoje));
        Assert.Equal(StatusObrigacao.Cumprida, ServicoObrigacoes.CalcularStatus(D(2026, 4, 20), new("DAS", "2026-03", hoje, null), hoje));
    }

    [Fact]
    public async Task Lista_filtra_por_regime_modulo_e_aplica_baixas()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new RepoFalso(new(RegimesObrigacao.Simples, false));
        var svc = Servico(repo, true);
        var hoje = D(2026, 4, 25);
        var r = await svc.ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), hoje, null, ct);
        Assert.True(r.Sucesso);
        var das = Assert.Single(r.Valor, i => i.Ocorrencia.Definicao.Codigo == "DAS");
        Assert.Equal(StatusObrigacao.Atrasada, das.Status);
        Assert.DoesNotContain(r.Valor, i => i.Ocorrencia.Definicao.Codigo is "PIS_COFINS" or "FGTS" or "ESOCIAL"); // regime e sem empregados

        var apenasFolha = await svc.ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), hoje, [ModuloObrigacao.Folha], ct);
        Assert.DoesNotContain(apenasFolha.Valor, i => i.Ocorrencia.Definicao.Codigo == "DAS");

        var m = await svc.MarcarCumpridaAsync(1, 1, "DAS", "2026-03", D(2026, 4, 17), "Pago", ct);
        Assert.True(m.Sucesso, string.Join(";", m.Erros));
        var depois = (await svc.ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), hoje, null, ct)).Valor.Single(i => i.Ocorrencia.Definicao.Codigo == "DAS");
        Assert.Equal(StatusObrigacao.Cumprida, depois.Status);
        Assert.Equal(D(2026, 4, 17), depois.CumpridaEm);
        Assert.Equal("Pago", depois.Observacao);

        Assert.True((await svc.DesfazerCumprimentoAsync(1, 1, "DAS", "2026-03", ct)).Sucesso);
        Assert.Equal(StatusObrigacao.Atrasada, (await svc.ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), hoje, null, ct)).Valor.Single(i => i.Ocorrencia.Definicao.Codigo == "DAS").Status);
    }

    [Fact]
    public async Task Folha_com_empregados_mostra_fgts_e_esocial_e_proximas_ignoram_cumpridas()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new RepoFalso(new(RegimesObrigacao.Presumido, true));
        var svc = Servico(repo, true);
        var hoje = D(2026, 6, 10);
        var r = await svc.ListarAsync(1, 1, D(2026, 6, 1), D(2026, 6, 30), hoje, null, ct);
        Assert.Contains(r.Valor, i => i.Ocorrencia.Definicao.Codigo == "FGTS" && i.Ocorrencia.Vencimento == D(2026, 6, 19));
        Assert.Contains(r.Valor, i => i.Ocorrencia.Definicao.Codigo == "ESOCIAL" && i.Ocorrencia.Vencimento == D(2026, 6, 15));
        await svc.MarcarCumpridaAsync(1, 1, "ESOCIAL", "2026-05", hoje, null, ct);
        var prox = await svc.ProximasAsync(1, 1, hoje, 7, 50, null, ct);
        Assert.DoesNotContain(prox.Valor, i => i.Ocorrencia.Definicao.Codigo == "ESOCIAL" && i.Ocorrencia.Competencia == new Competencia(2026, 5));
        Assert.All(prox.Valor, i => Assert.True(i.Ocorrencia.Vencimento <= hoje.AddDays(7)));
    }

    [Fact]
    public async Task Permissoes_leitura_com_acesso_e_manutencao_com_permissao()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new RepoFalso(new(RegimesObrigacao.Simples, false));
        var leitura = Servico(repo, false);
        Assert.True((await leitura.ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), D(2026, 4, 1), null, ct)).Sucesso);
        Assert.False((await leitura.MarcarCumpridaAsync(1, 1, "DAS", "2026-03", D(2026, 4, 1), null, ct)).Sucesso);
        var pers = new ObrigacaoPersonalizada(0, "ISS", "Prefeitura", "Municipal", Periodicidade.Mensal, TipoRegra.DiaFixo, 10, 1, 1, 0, AjusteNaoUtil.Posterga, null);
        Assert.False((await leitura.SalvarPersonalizadaAsync(1, 1, pers, ct)).Sucesso);
        Assert.False((await Servico(repo, true, acesso: false).ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), D(2026, 4, 1), null, ct)).Sucesso);

        var gestor = Servico(repo, true);
        Assert.False((await gestor.SalvarPersonalizadaAsync(1, 1, pers with { Nome = " " }, ct)).Sucesso);
        Assert.True((await gestor.SalvarPersonalizadaAsync(1, 1, pers, ct)).Sucesso);
        var lista = await gestor.ListarAsync(1, 1, D(2026, 4, 1), D(2026, 4, 30), D(2026, 4, 1), null, ct);
        var iss = Assert.Single(lista.Valor, i => i.Ocorrencia.Definicao.Personalizada);
        Assert.Equal(D(2026, 4, 10), iss.Ocorrencia.Vencimento); // competência 03/2026, dia 10 de abril (sexta)
    }
}
