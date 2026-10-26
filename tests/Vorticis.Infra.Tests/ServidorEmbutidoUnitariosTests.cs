using Vorticis.Infra.Sistema;

namespace Vorticis.Infra.Tests;

/// <summary>Partes do servidor embutido que não dependem de binários do PostgreSQL.</summary>
public class ServidorEmbutidoUnitariosTests
{
    private static string Arquivo(string conteudo)
    {
        var a = Path.Combine(Path.GetTempPath(), $"pid-{Guid.NewGuid():N}");
        File.WriteAllText(a, conteudo);
        return a;
    }

    [Theory]
    [InlineData("1234\n/dados\n", null, true)]          // processo inexistente
    [InlineData("1234\n/dados\n", "chrome", true)]      // número reaproveitado por outro programa
    [InlineData("1234\n/dados\n", "postgres", false)]
    [InlineData("1234\n/dados\n", "postgres.exe", false)]
    [InlineData("", "postgres", true)]                  // vazio
    [InlineData("lixo", "postgres", true)]              // corrompido
    public void Reconhece_postmaster_pid_orfao(string conteudo, string? processo, bool orfao)
    {
        var a = Arquivo(conteudo);
        try { Assert.Equal(orfao, ServidorPostgresEmbutido.PidOrfao(a, _ => processo)); }
        finally { File.Delete(a); }
    }

    [Fact]
    public void LimparPidOrfao_remove_o_arquivo_so_quando_orfao()
    {
        var dados = Path.Combine(Path.GetTempPath(), $"dados-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dados);
        try
        {
            var s = new ServidorPostgresEmbutido("/x", dados, Path.Combine(dados, "l.log"));
            var pid = Path.Combine(dados, "postmaster.pid");
            File.WriteAllText(pid, "42\n");
            Assert.False(s.LimparPidOrfao(_ => "postgres"));
            Assert.True(File.Exists(pid));
            Assert.True(s.LimparPidOrfao(_ => null));
            Assert.False(File.Exists(pid));
            Assert.False(s.LimparPidOrfao(_ => null));
        }
        finally { Directory.Delete(dados, recursive: true); }
    }

    [Fact]
    public void Verifica_dlls_do_Visual_Cpp()
    {
        var bin = Path.Combine(Path.GetTempPath(), $"bin-{Guid.NewGuid():N}");
        var sis = Path.Combine(Path.GetTempPath(), $"sis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(sis);
        try
        {
            Assert.Empty(RuntimeVisualCpp.Ausentes(bin, sis, windows: false));
            Assert.Equal(RuntimeVisualCpp.Dlls, RuntimeVisualCpp.Ausentes(bin, sis, windows: true));
            File.WriteAllText(Path.Combine(bin, "vcruntime140.dll"), "");
            File.WriteAllText(Path.Combine(sis, "msvcp140.dll"), "");
            Assert.Equal(["vcruntime140_1.dll"], RuntimeVisualCpp.Ausentes(bin, sis, windows: true));
        }
        finally { Directory.Delete(bin, true); Directory.Delete(sis, true); }
    }

    [Fact]
    public async Task Trava_entre_processos_e_exclusiva_e_expira()
    {
        var arq = Path.Combine(Path.GetTempPath(), $"trava-{Guid.NewGuid():N}.lock");
        var ct = TestContext.Current.CancellationToken;
        using (var a = await TravaEntreProcessos.AdquirirAsync(arq, TimeSpan.FromSeconds(2), ct: ct))
        {
            var avisou = false;
            await Assert.ThrowsAsync<TimeoutException>(() =>
                TravaEntreProcessos.AdquirirAsync(arq, TimeSpan.FromMilliseconds(700), () => avisou = true, ct));
            Assert.True(avisou);
        }
        using var b = await TravaEntreProcessos.AdquirirAsync(arq, TimeSpan.FromSeconds(2), ct: ct);   // liberada
        File.Delete(arq);
    }
}
