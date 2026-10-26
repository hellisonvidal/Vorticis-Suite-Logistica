using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Cnpj;

namespace Vorticis.Infra.Tests;

public class ResponsaveisTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static Contador NovoContador(string nome, string cpf, string crc = "123456/O-7") => new(0, nome, cpf, crc, "SP", "Contato@Escritorio.com", null, null);

    [Fact]
    public async Task Contadores_e_escritorio_com_vigencia_e_um_responsavel_tecnico()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var svc = db.App.Responsaveis;
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        var outra = await db.CriarEmpresaAsync("33000167000101");
        var adm = await db.AdminAsync();

        Assert.Contains((await svc.VincularContadorAsync(empresa, NovoContador("X", "111.111.111-11"), new(2026, 1, 1), true, adm, ct)).Erros, e => e.Contains("CPF"));
        Assert.Contains((await svc.VincularContadorAsync(empresa, NovoContador("X", "529.982.247-25") with { UfCrc = "XX" }, new(2026, 1, 1), true, adm, ct)).Erros,
                        e => e.Contains("UF"));

        var maria = await svc.VincularContadorAsync(empresa, NovoContador("Maria Contadora", "529.982.247-25"), new(2026, 1, 1), true, adm, ct);
        Assert.True(maria.Sucesso, string.Join("; ", maria.Erros));
        Assert.Contains("já é o responsável técnico", (await svc.VincularContadorAsync(empresa, NovoContador("João Contador", "390.533.447-05"),
            new(2026, 2, 1), true, adm, ct)).Erros[0]);
        Assert.True((await svc.VincularContadorAsync(empresa, NovoContador("João Contador", "390.533.447-05"), new(2026, 2, 1), false, adm, ct)).Sucesso);
        Assert.Contains("já está vinculado", (await svc.VincularContadorAsync(empresa, NovoContador("Maria C. Atualizada", "52998224725"),
            new(2026, 3, 1), false, adm, ct)).Erros[0]);
        Assert.Equal("Maria C. Atualizada", (await svc.ListarContadoresAsync(ct)).Single(c => c.Cpf == "52998224725").Nome);   // dados atualizados

        // O mesmo contador atende outra empresa (cadastro global, vínculo por empresa)
        Assert.True((await svc.VincularContadorAsync(outra, NovoContador("Maria C. Atualizada", "52998224725"), new(2026, 1, 1), true, adm, ct)).Sucesso);

        var esc = await svc.VincularEscritorioAsync(empresa, new EscritorioContabil(0, "45.723.174/0001-10", "Escritório Exemplo Ltda", "2SP012345/O-1", "SP", null, null),
            new(2026, 1, 1), adm, ct);
        Assert.True(esc.Sucesso, string.Join("; ", esc.Erros));

        var vinculos = (await svc.ListarVinculosAsync(empresa, adm, ct)).Valor;
        Assert.Equal(3, vinculos.Count);
        var resp = await svc.ResponsavelTecnicoAsync(empresa, ct);
        Assert.Equal(("Maria C. Atualizada", "SP-123456/O-7"), (resp!.Nome, resp.CrcCompleto));
        Assert.Equal("1SP123456/O-7", (resp with { Crc = "1SP123456/O-7" }).CrcCompleto);   // UF já no número: não repete

        // Contador que assina a ECD/ECF: sem marcação, o responsável técnico; marcado, ele
        var joao = vinculos.Single(v => v.Nome == "João Contador");
        Assert.Equal("Maria C. Atualizada", (await svc.SignatarioEcdEcfAsync(empresa, ct))!.Nome);
        Assert.True((await svc.DefinirAssinaEcdEcfAsync(empresa, joao.Id, true, adm, ct)).Sucesso);
        Assert.True((await svc.ListarVinculosAsync(empresa, adm, ct)).Valor.Single(v => v.Id == joao.Id).AssinaEcdEcf);
        Assert.Equal("João Contador", (await svc.SignatarioEcdEcfAsync(empresa, ct))!.Nome);
        Assert.Contains("Só um contador", (await svc.DefinirAssinaEcdEcfAsync(empresa, vinculos.Single(v => v.EscritorioId is not null).Id, true, adm, ct)).Erros[0]);

        // Troca de responsável técnico e encerramento de vínculo
        Assert.True((await svc.DefinirResponsavelAsync(empresa, joao.Id, adm, ct)).Sucesso);
        Assert.Equal("João Contador", (await svc.ResponsavelTecnicoAsync(empresa, ct))!.Nome);
        Assert.Contains("deve ser um contador", (await svc.DefinirResponsavelAsync(empresa, vinculos.Single(v => v.EscritorioId is not null).Id, adm, ct)).Erros[0]);
        Assert.Contains("anterior ao início", (await svc.EncerrarVinculoAsync(empresa, joao.Id, new(2025, 12, 31), adm, ct)).Erros[0]);
        Assert.True((await svc.EncerrarVinculoAsync(empresa, joao.Id, new(2026, 6, 30), adm, ct)).Sucesso);
        Assert.Null(await svc.ResponsavelTecnicoAsync(empresa, ct));
        Assert.True(await db.EscalarAsync<bool>("SELECT COUNT(*) >= 4 FROM core.auditoria WHERE tabela = 'core.empresa_contabilidade'"));
    }

    [Fact]
    public async Task Socios_validacoes_representante_e_importacao_do_qsa()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("60701190000104");
        var adm = await db.AdminAsync();
        // Serviço com a consulta de CNPJ simulada (QSA de exemplo)
        var http = new HttpSimulado(_ => HttpSimulado.Json(ConsultaCnpjTests.RespostaExemplo));
        var repo = new Postgres.RepositorioResponsaveis(Npgsql.NpgsqlDataSource.Create(db.ConnectionString));
        var svc = new ServicoResponsaveis(repo, db.App.RepositorioEmpresas, db.App.Autorizador, new ConsultaCnpjPublica(new HttpClient(http)));

        Socio S(string nome, decimal? p, bool rep = false, string? cpf = null) => new(0, nome, 'F', cpf, null, "Sócio", null, p, new(2020, 1, 1), null, rep, null, null);
        Assert.Contains((await svc.SalvarSocioAsync(empresa, S("Sem CPF", 10m, rep: true), adm, ct)).Erros, e => e.Contains("CPF do representante"));
        Assert.Contains((await svc.SalvarSocioAsync(empresa, S("CPF ruim", 10m, cpf: "123"), adm, ct)).Erros, e => e.Contains("CPF do sócio inválido"));
        var ana = await svc.SalvarSocioAsync(empresa, S("Ana Sócia", 60m, rep: true, cpf: "529.982.247-25"), adm, ct);
        Assert.True(ana.Sucesso, string.Join("; ", ana.Erros));
        Assert.Contains("soma das participações", (await svc.SalvarSocioAsync(empresa, S("Bruno", 50m), adm, ct)).Erros[0]);
        Assert.True((await svc.SalvarSocioAsync(empresa, S("Bruno", 40m), adm, ct)).Sucesso);

        var rep = await svc.RepresentanteLegalAsync(empresa, ct);
        Assert.Equal(("Ana Sócia", "52998224725"), (rep!.Nome, rep.CpfCnpj));

        // Saída do sócio libera participação
        var bruno = (await svc.ListarSociosAsync(empresa, adm, ct)).Valor.Single(s => s.Nome == "Bruno");
        Assert.True((await svc.SalvarSocioAsync(empresa, bruno with { DataSaida = new(2026, 5, 1) }, adm, ct)).Sucesso);
        Assert.True((await svc.SalvarSocioAsync(empresa, S("Carla", 40m), adm, ct)).Sucesso);

        // Importação do QSA: inclui os que faltam, sem duplicar
        var imp = await svc.ImportarDaReceitaAsync(empresa, adm, ct);
        Assert.True(imp.Sucesso, string.Join("; ", imp.Erros));
        Assert.Equal(2, imp.Valor);
        Assert.Equal(0, (await svc.ImportarDaReceitaAsync(empresa, adm, ct)).Valor);
        var fulano = (await svc.ListarSociosAsync(empresa, adm, ct)).Valor.Single(s => s.Nome == "FULANO DE TAL");
        Assert.Equal(("***123456**", "Sócio-Administrador", (int?)49, (string?)null), (fulano.DocumentoMascarado, fulano.Qualificacao, fulano.QualificacaoCodigo, fulano.CpfCnpj));
        Assert.StartsWith("BrasilAPI", fulano.Origem);

        // Perfil sem a permissão não altera
        var consulta = (await db.App.Perfis.ListarAsync(new SessaoUsuario(adm, "a", "A", true, false), ct)).Single(p => p.Nome == "Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(new SessaoUsuario(adm, "a", "A", true, false), "leitor2", "Leitor", "Inicial123", consulta.Id, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(new SessaoUsuario(adm, "a", "A", true, false), uid, empresa, consulta.Id, ct);
        Assert.True((await svc.ListarSociosAsync(empresa, uid, ct)).Sucesso);
        Assert.Contains("não permite", (await svc.SalvarSocioAsync(empresa, S("Hack", 1m), uid, ct)).Erros[0]);
    }
}
