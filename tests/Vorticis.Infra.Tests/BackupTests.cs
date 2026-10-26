using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Sistema;

namespace Vorticis.Infra.Tests;

public class BackupTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Restaura_o_estado_do_backup_e_guarda_copia_de_seguranca()
    {
        BancoDeTeste.ExigirBanco();
        string? ferramenta = null;
        try { ferramenta = BackupPostgres.LocalizarFerramenta("pg_dump"); } catch (FileNotFoundException) { }
        Assert.SkipWhen(ferramenta is null, "pg_dump não disponível nesta máquina.");

        var ct = TestContext.Current.CancellationToken;
        var admin = new SessaoUsuario(await db.AdminAsync(), "admin_teste", "Admin", true, false);
        var empresa = await db.CriarEmpresaAsync("11222333000181");
        async Task Lancar(int dia) => Assert.True((await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 6, dia),
            OrigemLancamento.Manual, [new("1.1.01.001", IndicadorDC.D, 10m, "x"), new("3.1.01.001", IndicadorDC.C, 10m, "x")]), admin.Id, ct)).Sucesso);
        Task<long> Contar() => db.EscalarAsync<long>("SELECT COUNT(*) FROM contabil.lancamento");

        await Lancar(1);
        var pasta = Path.Combine(Path.GetTempPath(), "vorticis_backup_" + Guid.NewGuid().ToString("N"));
        try
        {
            var gerado = await db.App.Backup.GerarAsync(admin, pasta, ct);
            Assert.True(gerado.Sucesso, string.Join("; ", gerado.Erros));
            Assert.True(new FileInfo(gerado.Valor).Length > 1000);

            await Lancar(2);
            Assert.Equal(2, await Contar());

            var comum = admin with { Administrador = false };
            Assert.Contains("Somente administradores", (await db.App.Backup.RestaurarAsync(comum, gerado.Valor, pasta, ct)).Erros[0]);

            var restaurado = await db.App.Backup.RestaurarAsync(admin, gerado.Valor, pasta, ct);
            Assert.True(restaurado.Sucesso, string.Join("; ", restaurado.Erros));
            Assert.Equal(1, await Contar());
            Assert.Contains("antes_da_restauracao", restaurado.Valor);
            Assert.Equal(2, Application.Sistema.ServicoBackup.Listar(pasta).Count);
            Assert.True(await db.EscalarAsync<bool>("SELECT core.fn_verificar_auditoria() IS NULL"));   // cadeia íntegra após restaurar
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }
}
