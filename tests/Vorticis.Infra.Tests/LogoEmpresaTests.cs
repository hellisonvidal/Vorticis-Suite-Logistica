using Vorticis.Application.Relatorios;
using Vorticis.Infra.Cadastros;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Infra.Tests;

/// <summary>Logo da empresa: gravado no cadastro (só o administrador troca) e impresso no cabeçalho dos relatórios em PDF.</summary>
[Collection("LogoRelatorios")]
public class LogoEmpresaTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>PNG de 1×1 pixel.</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void Aceita_so_png_ou_jpg_de_ate_1_mb()
    {
        Assert.Null(LogoRelatorios.Validar(Png));
        Assert.Null(LogoRelatorios.Validar([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]));
        Assert.Contains("PNG ou JPG", LogoRelatorios.Validar("GIF89a..."u8.ToArray()));
        Assert.Contains("1 MB", LogoRelatorios.Validar([.. Png, .. new byte[LogoRelatorios.TamanhoMaximo]]));
        Assert.NotNull(LogoRelatorios.Validar(null));
    }

    [Fact]
    public void Pdf_sai_com_o_logo_no_cabecalho()
    {
        static bool TemImagem(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf).Contains("/Subtype/Image");
        var doc = new DocumentoRelatorio("RELATÓRIO", ["EMPRESA EXEMPLO LTDA"], [new Bloco(Texto: "Corpo")]);
        var anterior = LogoRelatorios.Atual;
        try
        {
            LogoRelatorios.Atual = null;
            Assert.False(TemImagem(new GeradorPdf().Gerar(doc).Conteudo));
            Assert.True(TemImagem(new GeradorPdf().Gerar(doc with { Logo = Png }).Conteudo));
            LogoRelatorios.Atual = Png;   // o da empresa ativa vale para qualquer relatório
            Assert.True(TemImagem(new GeradorPdf().Gerar(doc).Conteudo));
            LogoRelatorios.Atual = "nao e imagem"u8.ToArray();   // arquivo inválido: sai sem logo, sem erro
            Assert.False(TemImagem(new GeradorPdf().Gerar(doc).Conteudo));
        }
        finally { LogoRelatorios.Atual = anterior; }
    }

    [Fact]
    public async Task Administrador_grava_e_remove_o_logo_e_os_demais_so_veem()
    {
        BancoDeTeste.ExigirBanco();
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        var svc = new ServicoLogoEmpresa(db.App.DataSource, db.App.Autorizador);
        Assert.Null(await svc.ObterAsync(empresa, adm, Ct));
        Assert.Contains("PNG ou JPG", (await svc.GravarAsync(empresa, "texto"u8.ToArray(), adm, Ct)).Erros[0]);
        Assert.True((await svc.GravarAsync(empresa, Png, adm, Ct)).Sucesso);
        Assert.Equal(Png, await svc.ObterAsync(empresa, adm, Ct));

        var contador = await db.EscalarAsync<long>("INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES ('logo_contador', 'x', 'x', (SELECT id FROM core.perfil WHERE nome = 'Contador')) RETURNING id");
        await db.EscalarAsync<int>("INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id) VALUES (@u, @e, (SELECT id FROM core.perfil WHERE nome = 'Contador')) RETURNING 1",
                                   ("u", contador), ("e", empresa));
        Assert.Equal(Png, await svc.ObterAsync(empresa, contador, Ct));
        Assert.False((await svc.GravarAsync(empresa, null, contador, Ct)).Sucesso);

        Assert.True((await svc.GravarAsync(empresa, null, adm, Ct)).Sucesso);
        Assert.Null(await svc.ObterAsync(empresa, adm, Ct));
    }
}
