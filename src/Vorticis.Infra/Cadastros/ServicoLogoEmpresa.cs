using Npgsql;
using Vorticis.Application.Relatorios;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Postgres;

namespace Vorticis.Infra.Cadastros;

/// <summary>Logo da empresa (impresso nos relatórios em PDF): ver exige acesso à empresa; trocar ou remover, ser administrador.</summary>
public sealed class ServicoLogoEmpresa(NpgsqlDataSource ds, Autorizador autorizador)
{
    private readonly BancoModulo _db = new(ds);

    public async Task<byte[]?> ObterAsync(long empresa, long usuario, CancellationToken ct = default)
    {
        if (!(await autorizador.ObterAsync(usuario, empresa, ct)).TemAcesso) return null;
        var l = await _db.ListarAsync("SELECT logo FROM core.empresa WHERE id = @e AND logo IS NOT NULL", r => (byte[])r.GetValue(0), ct, ("e", empresa));
        return l.FirstOrDefault();
    }

    /// <summary>Grava o logo (PNG ou JPEG, até 1 MB) ou o remove, com nulo.</summary>
    public async Task<Resultado> GravarAsync(long empresa, byte[]? logo, long usuario, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuario, empresa, Permissao.AdministrarSistema, ct) is { } erro) return Resultado.Falha(erro);
        if (logo is not null && LogoRelatorios.Validar(logo) is { } invalido) return Resultado.Falha(invalido);
        await _db.ExecutarAsync("UPDATE core.empresa SET logo = @l WHERE id = @e", ct, ("l", logo), ("e", empresa));
        return Resultado.Ok();
    }
}
