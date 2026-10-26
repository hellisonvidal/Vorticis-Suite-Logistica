using System.Xml.Linq;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Esocial;
using Vorticis.Folha.Servicos;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Tests;

/// <summary>Integração com PostgreSQL: cadastros, folha mensal, fechamento, relatórios, contabilização, férias, 13º, rescisão, provisões e eSocial.</summary>
public class FolhaIntegracaoTests(BancoFolha db) : IClassFixture<BancoFolha>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static Funcionario Novo(string matricula, string nome, string cpf, decimal salario, string admissao = "2020-01-01", long? centro = null,
                                     long? cargo = null, long? depto = null, IReadOnlyList<DependenteFolha>? deps = null, string? pis = null)
        => new(0, matricula, nome, cpf, pis, new DateOnly(1990, 5, 20), "F", 6, 1, "09", "func@exemplo.com.br", "11999998888", "01310100", "Av. Paulista", "1000", null, "Bela Vista",
               "3550308", "SP", DateOnly.Parse(admissao), "101", "INDETERMINADO", null, cargo, depto, "252105", "MENSAL", salario, 44, 220, centro, 0, false,
               "341", "1234", "56789-0", "CORRENTE", null, "ATIVO", null, null, deps ?? [], TipoLogradouro: "AV", CnpjSindicato: "11222333000181");

    private static void Ok(Vorticis.Domain.Comum.Resultado r) => Assert.True(r.Sucesso, string.Join("; ", r.Erros));

    private async Task<(long Empresa, long Admin)> PrepararAsync(string cnpj, RegimeTributario regime = RegimeTributario.LucroPresumido)
    {
        BancoFolha.ExigirBanco();
        return (await db.CriarEmpresaAsync(cnpj, regime), await db.AdminAsync());
    }

    [Fact]
    public async Task Cadastros_validam_dados_e_respeitam_as_permissoes_do_perfil()
    {
        var (empresa, adm) = await PrepararAsync("11222333000181");
        var cad = db.Folha.Cadastros;

        var cargo = await cad.GravarCargoAsync(empresa, new Cargo(0, "ANL", "Analista contábil", "252105"), adm, Ct);
        Ok(cargo);
        Assert.Contains("6 dígitos", (await cad.GravarCargoAsync(empresa, new Cargo(0, "X", "Cargo X", "123"), adm, Ct)).Erros[0]);
        var depto = await cad.GravarDepartamentoAsync(empresa, new Departamento(0, "ADM", "Administrativo"), adm, Ct);
        Ok(depto);
        Assert.Contains("Já existe", (await cad.GravarDepartamentoAsync(empresa, new Departamento(0, "ADM", "Outro"), adm, Ct)).Erros[0]);

        var invalido = Novo("", "Fulana", "11111111111", 3000m) with { PisNis = "12345678901" };
        var r = await cad.GravarFuncionarioAsync(empresa, invalido, adm, ct: Ct);
        Assert.Contains("CPF inválido.", r.Erros);
        Assert.Contains("PIS/NIS inválido.", r.Erros);

        var filho = new DependenteFolha(0, "Filho", null, new DateOnly(2020, 3, 1), "03", false, true, true);
        var id = await cad.GravarFuncionarioAsync(empresa, Novo("", "Maria da Silva", "529.982.247-25", 3000m, cargo: cargo.Valor, depto: depto.Valor, deps: [filho], pis: "12345678919"), adm, ct: Ct);
        Ok(id);
        var f = await cad.ObterFuncionarioAsync(empresa, id.Valor, adm, Ct);
        Assert.Equal(("000001", "52998224725", "Analista contábil", 1), (f!.Matricula, f.Cpf, f.CargoNome, f.Dependentes.Count));
        Assert.Contains("Já existe funcionário com este CPF", (await cad.GravarFuncionarioAsync(empresa, Novo("", "Outra", "52998224725", 2000m), adm, ct: Ct)).Erros[0]);

        // Alteração de salário gera alteração contratual (S-2206)
        Ok(await cad.GravarFuncionarioAsync(empresa, f with { Salario = 3300m }, adm, new DateOnly(2026, 3, 1), Ct));
        Assert.Equal(1, await db.Escalar<long>("SELECT COUNT(*) FROM folha.alteracao_contratual WHERE funcionario_id = @f AND salario_anterior = 3000 AND salario_novo = 3300", ("f", id.Valor)));

        // Rubricas padrão criadas sob demanda; padrão não é excluída
        var rubricas = await cad.ListarRubricasAsync(empresa, adm, Ct);
        Assert.Contains(rubricas, x => x.Codigo == "1001" && x.Natureza == "1003" && x.Variavel);
        Assert.Contains("padrão do sistema", (await cad.ExcluirRubricaAsync(empresa, rubricas.First(x => x.Codigo == "1000").Id, adm, Ct)).Erros[0]);
        var propria = await cad.GravarRubricaAsync(empresa, new Rubrica(0, "3000", "Prêmio produtividade", 'P', "1207", "11", "01", "11", true, false), adm, Ct);
        Ok(propria);
        Ok(await cad.ExcluirRubricaAsync(empresa, propria.Valor, adm, Ct));

        // Parâmetros de encargos
        Assert.Contains("RAT", (await cad.GravarParametrosAsync(empresa, ParametroEmpresa.Padrao with { RatAliquota = 4 }, adm, Ct)).Erros[0]);
        Ok(await cad.GravarParametrosAsync(empresa, ParametroEmpresa.Padrao with { Fap = 1.2m }, adm, Ct));
        Assert.Equal(1.2m, (await cad.ObterParametrosAsync(empresa, adm, Ct)).Fap);

        // Tabelas legais no banco = tabelas embutidas
        var tabelas = await cad.ListarTabelasLegaisAsync(empresa, adm, Ct);
        Assert.Equal(TabelasLegais.Embutidas().Count, tabelas.Count);
        Assert.Equal(Resumir(TabelasLegais.Em(new DateOnly(2026, 6, 1), TabelasLegais.Embutidas())), Resumir(TabelasLegais.Em(new DateOnly(2026, 6, 1), tabelas)));
        Assert.Equal(Resumir(TabelasLegais.Em(new DateOnly(2025, 8, 1), TabelasLegais.Embutidas())), Resumir(TabelasLegais.Em(new DateOnly(2025, 8, 1), tabelas)));

        // Perfis: "Consulta" não vê a folha; um perfil com FOLHA_CONSULTAR vê mas não altera
        var consulta = (await db.App.Usuarios.CriarUsuarioAsync(BancoFolha.SessaoAdmin, "consulta_folha", "Consulta", "Inicial123", await PerfilAsync("Consulta"), Ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(BancoFolha.SessaoAdmin, consulta, empresa, await PerfilAsync("Consulta"), Ct);
        Assert.Empty(await cad.ListarFuncionariosAsync(empresa, consulta, Ct));
        var leitor = await db.App.Perfis.SalvarAsync(BancoFolha.SessaoAdmin, null, "Leitor da folha", null, [Permissao.FolhaConsultar], Ct);
        Ok(leitor);
        var usuario = (await db.App.Usuarios.CriarUsuarioAsync(BancoFolha.SessaoAdmin, "leitor_folha", "Leitor", "Inicial123", leitor.Valor, Ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(BancoFolha.SessaoAdmin, usuario, empresa, leitor.Valor, Ct);
        Assert.Single(await cad.ListarFuncionariosAsync(empresa, usuario, Ct));
        Assert.Contains("não permite: Manter funcionários", (await cad.GravarFuncionarioAsync(empresa, f, usuario, ct: Ct)).Erros[0]);
        Assert.Contains("não permite", (await db.Folha.Folha.CalcularMensalAsync(empresa, new DateOnly(2026, 6, 1), usuario, ct: Ct)).Erros[0]);
        Assert.Contains("não permite", (await db.Folha.Folha.FecharAsync(empresa, new DateOnly(2026, 6, 1), usuario, Ct)).Erros[0]);
        Assert.Contains("não permite", (await db.Folha.Contabilizacao.ContabilizarAsync(empresa, new DateOnly(2026, 6, 1), "FOLHA", usuario, Ct)).Erros[0]);
        Assert.Contains("não permite", (await db.Folha.ESocial.GerarAsync(empresa, new DateOnly(2026, 6, 1), ["S-1000"], usuario, Ct)).Erros[0]);

        // Contador e Administrador recebem as permissões da folha; o catálogo tem as 10 novas
        Assert.Equal(10, Permissoes.Catalogo.Count(p => p.Grupo == "Folha de pagamento"));
        var contador = (await db.App.Perfis.ListarAsync(BancoFolha.SessaoAdmin, Ct)).Single(p => p.Nome == "Contador");
        Assert.Equal(Permissoes.Catalogo.Count, contador.Permissoes.Count);

        // Funcionário sem movimento pode ser excluído; empresa excluída leva a folha junto
        var sem = await cad.GravarFuncionarioAsync(empresa, Novo("", "Sem Movimento", "11144477735", 2000m), adm, ct: Ct);
        Ok(await cad.ExcluirFuncionarioAsync(empresa, sem.Valor, adm, Ct));
        Assert.True((await db.App.Empresas.ExcluirAsync(empresa, Ct)).Sucesso);
        Assert.Equal(0, await db.Escalar<long>("SELECT COUNT(*) FROM folha.funcionario WHERE empresa_id = @e", ("e", empresa)));
    }

    private static string Resumir(TabelasLegais t) => string.Join("|",
        t.Inss.Faixas.Select(f => $"{f.Ate:0.##}:{f.Aliquota:0.##}"), t.Irrf.Faixas.Select(f => $"{f.Ate:0.##}:{f.Aliquota:0.##}:{f.Deduzir:0.##}"),
        $"{t.Irrf.DeducaoDependente:0.##}", $"{t.Irrf.DescontoSimplificado:0.##}", $"{t.Irrf.Reducao?.Constante:0.######}:{t.Irrf.Reducao?.Coeficiente:0.######}",
        $"{t.SalarioFamilia.Cota:0.##}:{t.SalarioFamilia.LimiteRemuneracao:0.##}", $"{t.SalarioMinimo:0.##}", $"{t.Fgts.Aliquota:0.##}:{t.Fgts.MultaSemJustaCausa:0.##}");

    private static string Valor(XDocument d, string nome) => d.Descendants().First(x => x.Name.LocalName == nome).Value;

    private Task<long> PerfilAsync(string nome) => db.Escalar<long>("SELECT id FROM core.perfil WHERE nome = @n", ("n", nome));

    [Fact]
    public async Task Folha_mensal_com_eventos_fechamento_relatorios_e_contabilizacao()
    {
        var (empresa, adm) = await PrepararAsync("06990590000123");
        var folha = db.Folha.Folha;
        var cad = db.Folha.Cadastros;
        var competencia = new DateOnly(2026, 6, 1);

        var centro = await db.App.PlanoContas.IncluirCentroAsync(empresa, "CC1", "Administrativo", "", adm, sintetico: false, ct: Ct);
        Ok(centro);
        var filho = new DependenteFolha(0, "Filho", null, new DateOnly(2020, 3, 1), "03", false, true, true);
        var maria = (await cad.GravarFuncionarioAsync(empresa, Novo("1", "Maria da Silva", "52998224725", 3000m, centro: centro.Valor, deps: [filho]), adm, ct: Ct)).Valor;
        var joao = (await cad.GravarFuncionarioAsync(empresa, Novo("2", "João Souza", "11144477735", 6000m, centro: centro.Valor), adm, ct: Ct)).Valor;

        // Eventos variáveis: validações
        Assert.Contains("calculada automaticamente", (await folha.LancarEventoAsync(empresa, maria, competencia, "1000", 0, 100m, null, adm, Ct)).Erros[0]);
        Assert.Contains("Informe a quantidade", (await folha.LancarEventoAsync(empresa, maria, competencia, "1001", 0, null, null, adm, Ct)).Erros[0]);
        Assert.Contains("Informe o valor", (await folha.LancarEventoAsync(empresa, maria, competencia, "1007", 0, null, null, adm, Ct)).Erros[0]);
        Ok(await folha.LancarEventoAsync(empresa, maria, competencia, "1001", 10, null, "Fechamento", adm, Ct));
        Ok(await folha.LancarEventoAsync(empresa, maria, competencia, "1007", 0, 500m, null, adm, Ct));
        Ok(await folha.LancarEventoAsync(empresa, joao, competencia, "2003", 1, null, null, adm, Ct));
        Assert.Equal(3, (await folha.ListarEventosAsync(empresa, competencia, adm, Ct)).Count);

        var calc = await folha.CalcularMensalAsync(empresa, competencia, adm, ct: Ct);
        Ok(calc);
        Assert.Equal(2, calc.Valor.Calculados);
        var calculos = await folha.ListarCalculosAsync(empresa, competencia, TipoFolha.Mensal, adm, comItens: true, ct: Ct);
        var m = calculos.Single(c => c.FuncionarioId == maria);
        decimal V(CalculoGravado c, string cod) => c.Itens.Single(i => i.Codigo == cod).Valor;
        // Maria: salário 3.000; 10h x (3.000/220) x 1,5 = 204,55; comissões 500; DSR = 704,55 / 26 x 4 = 108,39
        Assert.Equal((3000m, 204.55m, 500m, 108.39m), (V(m, "1000"), V(m, "1001"), V(m, "1007"), V(m, "1003")));
        Assert.Equal((3812.94m, 346.15m, 0m, 305.04m), (m.TotalProventos, m.Inss, m.Irrf, m.Fgts));
        Assert.Equal(1060.00m, m.EncargosPrevidenciarios);   // 762,59 + 76,26 + 221,15 = (20% + RAT 2% + terceiros 5,8%) sobre 3.812,94
        Assert.Contains("1.621,00 x 7,5%", m.Itens.Single(i => i.Codigo == "2001").Memoria);
        // João: 1 falta (200) e perda de 1 DSR (200) -> base 5.600; INSS 585,51; IRRF 231,28 (simplificado: 4.992,80 x 27,5% - 908,73 = 464,29 - redução 233,01)
        var j = calculos.Single(c => c.FuncionarioId == joao);
        Assert.Equal((585.51m, 231.28m, 4783.21m), (j.Inss, j.Irrf, j.Liquido));

        // Fechamento: bloqueia alterações; reabertura libera
        Ok(await folha.FecharAsync(empresa, competencia, adm, Ct));
        Assert.Contains("fechada", (await folha.CalcularMensalAsync(empresa, competencia, adm, ct: Ct)).Erros[0]);
        Assert.Contains("fechada", (await folha.LancarEventoAsync(empresa, maria, competencia, "1007", 0, 10m, null, adm, Ct)).Erros[0]);
        Assert.Contains("já está fechada", (await folha.FecharAsync(empresa, competencia, adm, Ct)).Erros[0]);

        // Relatórios
        var rel = db.Folha.Relatorios;
        var holerites = await rel.HoleritesAsync(empresa, competencia, TipoFolha.Mensal, adm, Ct);
        Ok(holerites);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(holerites.Valor.Conteudo, 0, 4));
        Assert.True(holerites.Valor.Paginas >= 2);
        var soMaria = await rel.HoleritesAsync(empresa, competencia, TipoFolha.Mensal, adm, Ct, funcionarioId: maria);   // holerite de um funcionário
        Ok(soMaria);
        Assert.True(soMaria.Valor.Paginas < holerites.Valor.Paginas);
        Assert.Contains("funcionário escolhido", (await rel.HoleritesAsync(empresa, competencia, TipoFolha.Mensal, adm, Ct, funcionarioId: -1)).Erros[0]);
        var recibo = await rel.ReciboAsync(empresa, m.Id, adm, Ct);
        Ok(recibo);
        Assert.Equal(1, recibo.Valor.Paginas);
        var resumo = await rel.ResumoFolhaAsync(empresa, competencia, adm, Ct);
        Ok(resumo);
        Assert.Equal(m.Liquido + j.Liquido, (decimal)resumo.Valor[0].Linhas[^1].Valores[4]!);
        var encargos = await rel.EncargosAsync(empresa, competencia, adm, Ct);
        Ok(encargos);
        Assert.Equal(m.Fgts + j.Fgts, (decimal)encargos.Valor.Linhas[^1].Valores[6]!);

        // Contabilização: precisa de regras, depois lança, não duplica e desfaz por estorno
        var contab = db.Folha.Contabilizacao;
        var semRegra = await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct);
        Assert.Contains("Sem regra de contabilização", semRegra.Erros[0]);
        var sugeridas = await contab.GerarRegrasSugeridasAsync(empresa, comProvisoes: true, adm, Ct);
        Ok(sugeridas);
        Assert.True(sugeridas.Valor > 20);
        var previa = await contab.PreviaAsync(empresa, competencia, "FOLHA", adm, Ct);
        Ok(previa);
        Assert.Contains(previa.Valor.Proposta.Partidas, p => p.CodigoCentroCusto == "CC1" && p.DC == IndicadorDC.D);
        var lanc = await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct);
        Ok(lanc);
        Assert.Equal("FOLHA", await db.Escalar<string>("SELECT origem::text FROM contabil.lancamento WHERE id = @l", ("l", lanc.Valor.Id)));
        Assert.Equal(new DateOnly(2026, 6, 30), await db.Escalar<DateOnly>("SELECT data_lancamento FROM contabil.lancamento WHERE id = @l", ("l", lanc.Valor.Id)));
        Assert.Contains("Já contabilizado", (await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct)).Erros[0]);
        Assert.Contains("contabilizada", (await folha.ReabrirAsync(empresa, competencia, adm, Ct)).Erros[0]);
        Assert.Single(await contab.ListarAsync(empresa, competencia, adm, Ct));
        // Despesa de salários = proventos de salário da competência; salários a pagar = líquido total
        var totalDeb = await db.Escalar<decimal>("SELECT COALESCE(SUM(valor), 0) FROM contabil.partida WHERE lancamento_id = @l AND ind_dc = 'D'", ("l", lanc.Valor.Id));
        Assert.Equal(previa.Valor.Total, totalDeb);

        Ok(await contab.DesfazerAsync(empresa, competencia, "FOLHA", adm, Ct));
        Assert.Equal("ESTORNADO", await db.Escalar<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", lanc.Valor.Id)));
        Assert.DoesNotContain(await contab.ListarAsync(empresa, competencia, adm, Ct), c => c.DesfeitaEm is null);
        Ok(await contab.ContabilizarAsync(empresa, competencia, "FOLHA", adm, Ct));   // pode contabilizar de novo
        Ok(await contab.DesfazerAsync(empresa, competencia, "FOLHA", adm, Ct));
        Ok(await folha.ReabrirAsync(empresa, competencia, adm, Ct));
        Ok(await folha.CalcularMensalAsync(empresa, competencia, adm, ct: Ct));
    }

    [Fact]
    public async Task Ferias_decimo_terceiro_rescisao_e_provisoes()
    {
        var (empresa, adm) = await PrepararAsync("61695227000193");
        var folha = db.Folha.Folha;
        var cad = db.Folha.Cadastros;
        var ana = (await cad.GravarFuncionarioAsync(empresa, Novo("1", "Ana Lima", "52998224725", 3000m, admissao: "2024-03-10"), adm, ct: Ct)).Valor;
        var base2 = (await cad.GravarFuncionarioAsync(empresa, Novo("2", "Bruno Dias", "11144477735", 3000m, admissao: "2020-01-01"), adm, ct: Ct)).Valor;

        // Férias do 1º período aquisitivo (10/03/2024 a 09/03/2025), pagas em 06/2025 com as tabelas de 2025
        var saldos = await folha.ListarSaldosFeriasAsync(empresa, ana, new DateOnly(2025, 6, 1), adm, Ct);
        Assert.Equal(2, saldos.Count);   // o 2º período (a partir de 10/03/2025) ainda está em curso
        Assert.True(saldos[0].Completo);
        Assert.False(saldos[1].Completo);
        Assert.Contains("só se completa", (await folha.CalcularFeriasAsync(empresa, ana, new DateOnly(2025, 3, 10), new DateOnly(2025, 7, 1), 30, 0, 0, new DateOnly(2025, 6, 27), adm, Ct)).Erros[0]);
        var ferias = await folha.CalcularFeriasAsync(empresa, ana, new DateOnly(2024, 3, 10), new DateOnly(2025, 7, 1), 20, 10, 0, new DateOnly(2025, 6, 27), adm, Ct);
        Ok(ferias);
        var cf = (await folha.ListarCalculosAsync(empresa, new DateOnly(2025, 6, 1), TipoFolha.Ferias, adm, true, Ct)).Single();
        Assert.Equal((4000m, 217.23m), (cf.TotalProventos, cf.Inss));
        // 2025: tabela anterior (sem redução): INSS de 2.666,67 com teto 8.157,41 e faixas de 2025
        Assert.Contains("1.518,00 x 7,5%", cf.Itens.Single(i => i.Codigo == "2001").Memoria);
        Assert.Contains("passam", (await folha.CalcularFeriasAsync(empresa, ana, new DateOnly(2024, 3, 10), new DateOnly(2025, 8, 1), 20, 0, 0, new DateOnly(2025, 7, 20), adm, Ct)).Erros[0]);
        Assert.Contains("início do gozo", (await folha.CalcularFeriasAsync(empresa, ana, new DateOnly(2024, 3, 10), new DateOnly(2025, 8, 1), 5, 0, 0, new DateOnly(2025, 8, 5), adm, Ct)).Erros[0]);

        // 13º salário de 2026: Bruno (admitido em 2020) 12 avos; Ana também
        var d1 = await folha.CalcularDecimoAsync(empresa, 2026, 1, adm, Ct);
        Ok(d1);
        Assert.Equal(2, d1.Valor.Calculados);
        Assert.Equal(3000m, d1.Valor.TotalProventos);   // 1.500 x 2
        var d2 = await folha.CalcularDecimoAsync(empresa, 2026, 2, adm, Ct);
        Ok(d2);
        var c2 = (await folha.ListarCalculosAsync(empresa, new DateOnly(2026, 12, 1), TipoFolha.Decimo2, adm, true, Ct)).Single(c => c.FuncionarioId == base2);
        Assert.Equal((3000m, 1500m, 248.60m, 1251.40m), (c2.Itens.Single(i => i.Codigo == "1021").Valor, c2.Itens.Single(i => i.Codigo == "2012").Valor, c2.Inss, c2.Liquido));

        // Rescisão de Ana: sem justa causa em 15/06/2026 com aviso indenizado (2 anos completos = 36 dias); a 1ª férias foi gozada, a 2ª (a partir de 10/03/2025) está vencida
        var projetoFgts = await folha.SaldoFgtsEstimadoAsync(empresa, ana, new DateOnly(2026, 5, 1), adm, Ct);
        Assert.True(projetoFgts >= 0);
        var resc = await folha.CalcularRescisaoAsync(empresa, ana, MotivoRescisao.SemJustaCausa, new DateOnly(2026, 6, 15), TipoAviso.Indenizado, 10000m, new DateOnly(2026, 6, 25), adm, Ct);
        Ok(resc);
        var cr = (await folha.ListarCalculosAsync(empresa, new DateOnly(2026, 6, 1), TipoFolha.Rescisao, adm, true, Ct)).Single();
        decimal V(string cod) => cr.Itens.Single(i => i.Codigo == cod).Valor;
        Assert.Equal((1500m, 3600m, 3000m, 1000m), (V("1030"), V("1031"), V("1032"), V("1033")));
        Assert.Equal((1000m, 333.33m, 1750m), (V("1034"), V("1035"), V("1036")));        // 4/12 avos de férias e 7/12 avos de 13º
        Assert.Equal(4219.20m, cr.Itens.Single(i => i.Codigo == "9005").Valor);            // 40% de (10.000 + FGTS da rescisão 548)
        Assert.Equal("DESLIGADO", (await cad.ObterFuncionarioAsync(empresa, ana, adm, Ct))!.Situacao);
        Assert.Contains("já está desligado", (await folha.CalcularRescisaoAsync(empresa, ana, MotivoRescisao.PedidoDemissao, new DateOnly(2026, 6, 20), TipoAviso.Trabalhado, 0, new DateOnly(2026, 6, 30), adm, Ct)).Erros[0]);
        var trct = await db.Folha.Relatorios.ReciboAsync(empresa, cr.Id, adm, Ct);
        Ok(trct);
        Assert.Equal("RESCISAO", await db.Escalar<string>("SELECT tipo FROM folha.calculo WHERE id = @c", ("c", cr.Id)));
        // S-2299 do desligamento (motivo 02 = sem justa causa; aviso indenizado projetado)
        Ok(await db.Folha.ESocial.GerarAsync(empresa, new DateOnly(2026, 6, 1), ["S-2299"], adm, Ct));
        var s2299 = XDocument.Parse((await db.Folha.ESocial.ListarAsync(empresa, new DateOnly(2026, 6, 1), adm, Ct)).Single(e => e.Tipo == "S-2299").Xml);
        Assert.Equal(("02", "2026-06-15", "S", "2026-07-21"), (Valor(s2299, "mtvDeslig"), Valor(s2299, "dtDeslig"), Valor(s2299, "indPagtoAPI"), Valor(s2299, "dtProjFimAPI")));
        // Folha mensal ignora o desligado
        var mensal = await folha.CalcularMensalAsync(empresa, new DateOnly(2026, 6, 1), adm, ct: Ct);
        Ok(mensal);
        Assert.Equal(1, mensal.Valor.Calculados);
        Ok(await folha.CancelarRescisaoAsync(empresa, resc.Valor, adm, Ct));
        Assert.Equal("ATIVO", (await cad.ObterFuncionarioAsync(empresa, ana, adm, Ct))!.Situacao);
        Assert.Empty(await folha.ListarCalculosAsync(empresa, new DateOnly(2026, 6, 1), TipoFolha.Rescisao, adm, false, Ct));

        // Provisões mensais e contabilização
        var prov = await folha.CalcularProvisoesAsync(empresa, new DateOnly(2026, 6, 1), adm, Ct);
        Ok(prov);
        Assert.Equal(2, prov.Valor.Calculados);
        var provisoes = await folha.ListarProvisoesAsync(empresa, new DateOnly(2026, 6, 1), adm, Ct);
        Assert.All(provisoes, p => Assert.Equal((333.33m, 250m), (p.Ferias, p.Decimo)));
        // Encargos: (20% + RAT 2% x FAP 1 + terceiros 5,8%) = 27,8% e FGTS 8% sobre cada provisão
        Assert.Equal((92.67m, 26.67m), (provisoes[0].EncFeriasInss, provisoes[0].EncFeriasFgts));
        var tabela = await db.Folha.Relatorios.ProvisoesAsync(empresa, new DateOnly(2026, 6, 1), adm, Ct);
        Ok(tabela);
        var contab = db.Folha.Contabilizacao;
        Assert.Contains("Sem regra", (await contab.ContabilizarAsync(empresa, new DateOnly(2026, 6, 1), "PROVISAO", adm, Ct)).Erros[0]);
        Ok(await contab.GerarRegrasSugeridasAsync(empresa, true, adm, Ct));
        var lanc = await contab.ContabilizarAsync(empresa, new DateOnly(2026, 6, 1), "PROVISAO", adm, Ct);
        Ok(lanc);
        Assert.Contains("contabilizadas", (await folha.CalcularProvisoesAsync(empresa, new DateOnly(2026, 6, 1), adm, Ct)).Erros[0]);
        Ok(await contab.DesfazerAsync(empresa, new DateOnly(2026, 6, 1), "PROVISAO", adm, Ct));
        Ok(await folha.CalcularProvisoesAsync(empresa, new DateOnly(2026, 6, 1), adm, Ct));

        // Cancelar férias libera os dias do período
        var cancel = await folha.CancelarFeriasAsync(empresa, ferias.Valor, adm, Ct);
        Ok(cancel);
        Assert.Empty(await folha.ListarCalculosAsync(empresa, new DateOnly(2025, 6, 1), TipoFolha.Ferias, adm, false, Ct));
    }

    [Fact]
    public async Task Assistente_do_eSocial_aponta_as_pendencias_por_etapa_e_libera_depois_de_corrigidas()
    {
        var (empresa, adm) = await PrepararAsync("63810427000175", RegimeTributario.SimplesNacional);
        var cad = db.Folha.Cadastros;
        var assistente = db.Folha.AssistenteEsocial;
        var competencia = new DateOnly(2026, 6, 1);

        // empresa recém-cadastrada: falta o responsável, não há funcionário nem folha
        var etapas = await assistente.DiagnosticarAsync(empresa, competencia, adm, Ct);
        Assert.Equal(8, etapas.Count);
        Assert.StartsWith("1. Dados do empregador", etapas[0].Titulo);
        Assert.Equal(SituacaoEtapaEsocial.Pendente, etapas[1].Situacao);                       // responsável
        Assert.Contains(etapas[1].Pendencias, p => p.Contains("nome do responsável"));
        Assert.Equal(SituacaoEtapaEsocial.Pronta, etapas[2].Situacao);                         // produção restrita (padrão)
        Assert.Equal(SituacaoEtapaEsocial.Atencao, etapas[5].Situacao);                        // nenhum funcionário
        Assert.Equal(DestinoEtapaEsocial.Funcionarios, etapas[5].Destino);
        Assert.Equal(SituacaoEtapaEsocial.Atencao, etapas[^1].Situacao);                       // nada gerado ainda

        // funcionário com cadastro incompleto (sem CBO/sindicato) aparece na etapa 6, com o nome
        Ok(await cad.GravarParametrosAsync(empresa, ParametroEmpresa.Padrao with { SimplesAnexo = "III", ResponsavelNome = "Contador Responsável",
            ResponsavelCpf = "52998224725", ResponsavelEmail = "resp@exemplo.com.br" }, adm, Ct));
        var incompleto = Novo("7", "Pedro Incompleto", "11144477735", 2500m) with { CnpjSindicato = null };
        Ok(await cad.GravarFuncionarioAsync(empresa, incompleto, adm, ct: Ct));
        etapas = await assistente.DiagnosticarAsync(empresa, competencia, adm, Ct);
        Assert.Equal(SituacaoEtapaEsocial.Pronta, etapas[1].Situacao);
        Assert.Equal(SituacaoEtapaEsocial.Pendente, etapas[5].Situacao);
        Assert.Contains(etapas[5].Pendencias, p => p.Contains("Pedro Incompleto") && p.Contains("sindicato"));
        Assert.Equal(SituacaoEtapaEsocial.Pendente, etapas[6].Situacao);                       // folha não calculada
        Assert.Contains("não foi calculada", etapas[6].Pendencias[0]);

        // cadastro corrigido e folha calculada: trabalhadores prontos; competência ainda aberta (atenção)
        var f = (await cad.ListarFuncionariosAsync(empresa, adm, Ct)).Single();
        Ok(await cad.GravarFuncionarioAsync(empresa, (await cad.ObterFuncionarioAsync(empresa, f.Id, adm, Ct))! with { CnpjSindicato = "11222333000181" }, adm, ct: Ct));
        Ok(await db.Folha.Folha.CalcularMensalAsync(empresa, competencia, adm, ct: Ct));
        etapas = await assistente.DiagnosticarAsync(empresa, competencia, adm, Ct);
        Assert.True(etapas.Take(6).All(e => e.Situacao == SituacaoEtapaEsocial.Pronta), string.Join(" | ", etapas.Take(6).SelectMany(e => e.Pendencias)));
        Assert.Equal(SituacaoEtapaEsocial.Atencao, etapas[6].Situacao);
        Assert.Contains("está aberta", etapas[6].Pendencias[0]);
    }

    [Fact]
    public async Task ESocial_gera_os_xmls_com_id_no_padrao_e_exporta_por_competencia()
    {
        var (empresa, adm) = await PrepararAsync("00000000000191", RegimeTributario.SimplesNacional);
        var folha = db.Folha.Folha;
        var cad = db.Folha.Cadastros;
        var competencia = new DateOnly(2026, 6, 1);
        Ok(await cad.GravarParametrosAsync(empresa, ParametroEmpresa.Padrao with { SimplesAnexo = "III", ResponsavelNome = "Contador Responsável",
            ResponsavelCpf = "52998224725", ResponsavelEmail = "resp@exemplo.com.br" }, adm, Ct));
        var depto = (await cad.GravarDepartamentoAsync(empresa, new Departamento(0, "ADM", "Administrativo"), adm, Ct)).Valor;
        var cargo = (await cad.GravarCargoAsync(empresa, new Cargo(0, "ANL", "Analista", "252105"), adm, Ct)).Valor;
        var filho = new DependenteFolha(0, "Filho", "11144477735", new DateOnly(2020, 3, 1), "03", false, true, true);
        var f = (await cad.GravarFuncionarioAsync(empresa, Novo("1", "Maria da Silva", "52998224725", 3000m, cargo: cargo, depto: depto, deps: [filho]), adm, ct: Ct)).Valor;
        Ok(await cad.GravarFuncionarioAsync(empresa, (await cad.ObterFuncionarioAsync(empresa, f, adm, Ct))! with { Salario = 3200m }, adm, new DateOnly(2026, 6, 10), Ct));
        Ok(await cad.GravarAfastamentoAsync(empresa, new Afastamento(0, f, "", new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 20), "03", "Atestado"), adm, Ct));
        Ok(await folha.CalcularMensalAsync(empresa, competencia, adm, ct: Ct));

        // Encargos do Simples Nacional (anexo III): sem CPP; só FGTS
        var c = (await folha.ListarCalculosAsync(empresa, competencia, TipoFolha.Mensal, adm, ct: Ct)).Single();
        Assert.Equal((0m, 0m, 0m), (c.EncInssPatronal, c.EncRat, c.EncTerceiros));

        var es = db.Folha.ESocial;
        var todos = EventosEsocial.Todos.Select(t => t.Tipo).ToArray();
        var gerar = await es.GerarAsync(empresa, competencia, todos, adm, Ct);
        Ok(gerar);
        Assert.Empty(gerar.Valor.Avisos);
        var eventos = await es.ListarAsync(empresa, competencia, adm, Ct);
        Assert.All(todos.Where(t => t != "S-2299"), tipo => Assert.Contains(eventos, e => e.Tipo == tipo));   // sem rescisão neste cenário
        Assert.All(eventos, e => Assert.Equal(("GERADO", true), (e.Status, GeradoresEsocial.IdValido(e.IdEvento))));
        Assert.Equal(eventos.Count, eventos.Select(e => e.IdEvento).Distinct().Count());
        foreach (var e in eventos)
        {
            var xml = XDocument.Parse(e.Xml);   // XML bem formado
            Assert.StartsWith("http://www.esocial.gov.br/schema/evt/", xml.Root!.Name.NamespaceName);
            Assert.EndsWith("v_S_01_03_00", xml.Root.Name.NamespaceName);
            var evt = xml.Root.Elements().Single();
            Assert.Equal(e.IdEvento, evt.Attribute("Id")!.Value);
            Assert.Equal(36, evt.Attribute("Id")!.Value.Length);
        }
        // Validação contra os XSDs oficiais S-1.3 embutidos
        var errosXsd = eventos.SelectMany(e => ValidadorXsdEsocial.ValidarEvento(e.Xml, exigirAssinatura: false).Select(m => $"{e.Tipo}: {m}")).Distinct().ToList();
        Assert.True(errosXsd.Count == 0, string.Join("\n", errosXsd));
        var s2200 = XDocument.Parse(eventos.Single(e => e.Tipo == "S-2200").Xml);
        string Txt(XDocument d, string nome) => d.Descendants().First(x => x.Name.LocalName == nome).Value;
        Assert.Equal(("52998224725", "000000", "2020-01-01"), (Txt(s2200, "cpfTrab"), "000000", Txt(s2200, "dtAdm")));
        Assert.Equal("Analista", Txt(s2200, "nmCargo"));
        Assert.Equal("S", Txt(s2200, "depSF"));
        Assert.Equal("00000000", Txt(XDocument.Parse(eventos.Single(e => e.Tipo == "S-1000").Xml), "nrInsc"));
        var s1200 = XDocument.Parse(eventos.Single(e => e.Tipo == "S-1200").Xml);
        Assert.Equal("2026-06", Txt(s1200, "perApur"));
        Assert.Contains(s1200.Descendants().Where(x => x.Name.LocalName == "codRubr"), x => x.Value == "1000");
        Assert.Equal(c.Liquido.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Txt(XDocument.Parse(eventos.Single(e => e.Tipo == "S-1210").Xml), "vrLiq"));
        Assert.Equal("S", Txt(XDocument.Parse(eventos.Single(e => e.Tipo == "S-1299").Xml), "evtRemun"));
        Assert.Contains(eventos, e => e.Tipo == "S-2206" && e.Xml.Contains("<vrSalFx>3200.00</vrSalFx>"));
        Assert.Equal(2, eventos.Count(e => e.Tipo == "S-2230"));   // início e término

        // Exportar: grava os arquivos e marca como exportado; regerar preserva os já exportados
        var pasta = Path.Combine(Path.GetTempPath(), "vorticis_esocial_" + Guid.NewGuid().ToString("N"));
        try
        {
            var exp = await es.ExportarAsync(empresa, competencia, eventos.Where(e => e.Tipo is "S-1000" or "S-1200").Select(e => e.Id).ToArray(), pasta, adm, Ct);
            Ok(exp);
            Assert.Equal(2, Directory.GetFiles(pasta, "*.xml").Length);
            Assert.Equal(2, (await es.ListarAsync(empresa, competencia, adm, Ct)).Count(e => e.Status == "EXPORTADO"));
            var de_novo = await es.GerarAsync(empresa, competencia, ["S-1000", "S-1200", "S-1005"], adm, Ct);
            Ok(de_novo);
            Assert.Equal((1, 2), (de_novo.Valor.Gerados, de_novo.Valor.Ignorados));   // S-1005 regerado; S-1000 e S-1200 já exportados
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        }
    }
}
