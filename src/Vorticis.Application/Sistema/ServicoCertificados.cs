using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Sistema;

/// <summary>Certificado guardado no repositório (sem o conteúdo do arquivo).</summary>
public sealed record CertificadoDigital(long Id, string Tipo, string Descricao, string Titular, string? Documento, string Emissor,
                                        DateTimeOffset ValidoDe, DateTimeOffset ValidoAte, string Impressao, long? EmpresaId,
                                        bool PadraoContador = false, bool PadraoResponsavel = false, bool PadraoProcurador = false)
{
    public int DiasParaVencer(DateTimeOffset agora) => (int)Math.Floor((ValidoAte - agora).TotalDays);

    public string Situacao(DateTimeOffset agora) => DiasParaVencer(agora) switch
    {
        < 0 => "Vencido",
        <= 30 => $"Vence em {DiasParaVencer(agora)} dia(s)",
        _ => "Válido",
    };
}

/// <summary>Dados lidos de um certificado (arquivo .pfx ou repositório do Windows), antes de guardar.</summary>
public sealed record DadosCertificado(string Titular, string? Documento, string Emissor, DateTimeOffset ValidoDe, DateTimeOffset ValidoAte,
                                      string Impressao, bool TemChavePrivada, string? Tipo = null);

/// <summary>Papel de quem assina com o certificado padrão.</summary>
public enum PapelAssinatura { Contador, Responsavel, Procurador }

public interface IRepositorioCertificados
{
    Task<IReadOnlyList<CertificadoDigital>> ListarAsync(CancellationToken ct);
    Task<long> IncluirAsync(CertificadoDigital c, byte[]? arquivo, CancellationToken ct);
    Task<byte[]?> ObterArquivoAsync(long id, CancellationToken ct);
    Task ExcluirAsync(long id, CancellationToken ct);
    /// <summary>Marca (ou desmarca) o certificado como padrão do papel, desmarcando o anterior do mesmo escopo (empresa ou geral).</summary>
    Task DefinirPadraoAsync(long id, PapelAssinatura papel, bool padrao, CancellationToken ct);
}

/// <summary>
/// Configurações › Certificados digitais: repositório dos certificados usados na comunicação com órgãos públicos
/// (Receita Federal, SEFAZ, Junta Comercial). A1 (.pfx) fica guardado no banco como foi importado — protegido pela
/// própria senha, que não é gravada; A3 (token/cartão) é só registrado (o certificado fica no dispositivo). Só o
/// administrador mantém o repositório.
/// </summary>
public sealed class ServicoCertificados(IRepositorioCertificados repositorio, TimeProvider? relogio = null, Autorizador? autorizador = null)
{
    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;
    public DateTimeOffset Agora => _relogio.GetUtcNow();

    public async Task<Resultado<IReadOnlyList<CertificadoDigital>>> ListarAsync(SessaoUsuario usuario, CancellationToken ct = default)
        => usuario.Administrador
            ? Resultado<IReadOnlyList<CertificadoDigital>>.Ok((await repositorio.ListarAsync(ct)).OrderBy(c => c.ValidoAte).ToList())
            : Resultado<IReadOnlyList<CertificadoDigital>>.Falha("Somente o administrador acessa os certificados digitais.");

    /// <summary>Lê o .pfx com a senha (sem guardar nada) para conferir titular e validade antes de importar.</summary>
    public static Resultado<DadosCertificado> LerPfx(byte[] pfx, string senha)
    {
        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet);
            return Resultado<DadosCertificado>.Ok(Dados(cert));
        }
        catch (CryptographicException)
        {
            return Resultado<DadosCertificado>.Falha("Não foi possível abrir o certificado: senha incorreta ou arquivo que não é .pfx/.p12.");
        }
    }

    /// <summary>Importa um certificado A1 (.pfx/.p12). A senha só é usada para conferir o arquivo.</summary>
    public async Task<Resultado<long>> ImportarA1Async(SessaoUsuario usuario, byte[] pfx, string senha, string? descricao, long? empresaId,
                                                     CancellationToken ct = default)
    {
        if (!usuario.Administrador) return Resultado<long>.Falha("Somente o administrador importa certificados digitais.");
        var lido = LerPfx(pfx, senha);
        if (!lido.Sucesso) return Resultado<long>.Falha(lido.Erros);
        if (!lido.Valor.TemChavePrivada) return Resultado<long>.Falha("O arquivo não contém a chave privada; exporte o certificado A1 com a chave (.pfx).");
        return await GravarAsync("A1", lido.Valor, pfx, descricao, empresaId, ct);
    }

    /// <summary>Registra um certificado A3 (token/cartão) visto no repositório do Windows.</summary>
    public async Task<Resultado<long>> RegistrarA3Async(SessaoUsuario usuario, DadosCertificado dados, string? descricao, long? empresaId,
                                                      CancellationToken ct = default)
    {
        if (!usuario.Administrador) return Resultado<long>.Falha("Somente o administrador registra certificados digitais.");
        return await GravarAsync("A3", dados, null, descricao, empresaId, ct);
    }

    private async Task<Resultado<long>> GravarAsync(string tipo, DadosCertificado d, byte[]? arquivo, string? descricao, long? empresaId,
                                                   CancellationToken ct)
    {
        if (d.ValidoAte < Agora) return Resultado<long>.Falha($"Certificado vencido em {d.ValidoAte.ToLocalTime():dd/MM/yyyy}.");
        if ((await repositorio.ListarAsync(ct)).FirstOrDefault(c => c.Impressao == d.Impressao) is { } existente)
            return Resultado<long>.Falha($"Este certificado já está no repositório ({existente.Descricao}).");
        var desc = string.IsNullOrWhiteSpace(descricao) ? d.Titular : descricao.Trim();
        try
        {
            return Resultado<long>.Ok(await repositorio.IncluirAsync(new CertificadoDigital(0, tipo, Truncar(desc, 150), Truncar(d.Titular, 250), d.Documento,
                Truncar(d.Emissor, 250), d.ValidoDe, d.ValidoAte, d.Impressao, empresaId), arquivo, ct));
        }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirAsync(SessaoUsuario usuario, long id, CancellationToken ct = default)
    {
        if (!usuario.Administrador) return Resultado.Falha("Somente o administrador exclui certificados digitais.");
        await repositorio.ExcluirAsync(id, ct);
        return Resultado.Ok();
    }

    /// <summary>Define (ou retira) o certificado padrão para assinar como contador ou como responsável. Vale para a empresa do certificado, ou para todas se ele não tiver empresa.</summary>
    public async Task<Resultado> DefinirPadraoAsync(SessaoUsuario usuario, long id, PapelAssinatura papel, bool padrao, CancellationToken ct = default)
    {
        if (!usuario.Administrador) return Resultado.Falha("Somente o administrador define o certificado padrão.");
        var c = (await repositorio.ListarAsync(ct)).FirstOrDefault(x => x.Id == id);
        if (c is null) return Resultado.Falha("Certificado não encontrado.");
        if (padrao && c.ValidoAte < Agora) return Resultado.Falha($"Certificado vencido em {c.ValidoAte.ToLocalTime():dd/MM/yyyy}: não pode ser o padrão.");
        await repositorio.DefinirPadraoAsync(id, papel, padrao, ct);
        return Resultado.Ok();
    }

    /// <summary>Certificado padrão do papel para a empresa: o da própria empresa, senão o geral (sem empresa). Nulo se não houver ou estiver vencido.</summary>
    public async Task<Resultado<CertificadoDigital?>> ObterPadraoAsync(SessaoUsuario usuario, PapelAssinatura papel, long? empresaId, CancellationToken ct = default)
    {
        if (!usuario.Administrador) return Resultado<CertificadoDigital?>.Falha("Somente o administrador usa os certificados digitais.");
        return Resultado<CertificadoDigital?>.Ok(await PadraoAsync(papel, empresaId, ct));
    }

    private async Task<CertificadoDigital?> PadraoAsync(PapelAssinatura papel, long? empresaId, CancellationToken ct)
    {
        var padroes = (await repositorio.ListarAsync(ct))
            .Where(c => papel switch { PapelAssinatura.Contador => c.PadraoContador, PapelAssinatura.Procurador => c.PadraoProcurador, _ => c.PadraoResponsavel } && c.ValidoAte >= Agora).ToList();
        return padroes.FirstOrDefault(c => empresaId is not null && c.EmpresaId == empresaId) ?? padroes.FirstOrDefault(c => c.EmpresaId is null);
    }

    // ------------------------------------------------------------------ uso na assinatura da ECD/ECF
    // Cadastrar, importar, excluir e definir o padrão continuam só do administrador. Para ASSINAR a ECD/ECF, quem tem a função de gerá-las na
    // empresa (perfil Contador, por exemplo) lista e usa os certificados cadastrados dessa empresa (ou gerais), inclusive o padrão.

    private async Task<string?> NegarAssinaturaAsync(SessaoUsuario usuario, long empresaId, CancellationToken ct)
    {
        if (usuario.Administrador) return null;
        const string recusa = "Seu perfil não permite usar os certificados digitais para assinar a ECD/ECF ou transmitir o eSocial.";
        if (autorizador is null) return recusa;
        if (await autorizador.VerificarAsync(usuario.Id, empresaId, Permissao.SpedEcdGerar, ct) is null) return null;
        if (await autorizador.VerificarAsync(usuario.Id, empresaId, Permissao.SpedEcfGerar, ct) is null) return null;
        // Quem gera a EFD (Fiscal › Obrigações › SPED) e quem transmite o eSocial (Folha › eSocial) também usa os certificados cadastrados da empresa.
        if (await autorizador.VerificarAsync(usuario.Id, empresaId, Permissao.FiscalSpedGerar, ct) is null) return null;
        return await autorizador.VerificarAsync(usuario.Id, empresaId, Permissao.FolhaESocial, ct) is null ? null : recusa;
    }

    /// <summary>Certificados da empresa e os gerais (sem empresa), para escolher o de cada signatário da ECD/ECF.</summary>
    public async Task<Resultado<IReadOnlyList<CertificadoDigital>>> ListarParaAssinaturaAsync(SessaoUsuario usuario, long empresaId, CancellationToken ct = default)
    {
        if (await NegarAssinaturaAsync(usuario, empresaId, ct) is { } negado) return Resultado<IReadOnlyList<CertificadoDigital>>.Falha(negado);
        return Resultado<IReadOnlyList<CertificadoDigital>>.Ok((await repositorio.ListarAsync(ct))
            .Where(c => c.EmpresaId is null || c.EmpresaId == empresaId).OrderBy(c => c.ValidoAte).ToList());
    }

    /// <summary>Certificado padrão do papel para assinar a ECD/ECF da empresa.</summary>
    public async Task<Resultado<CertificadoDigital?>> ObterPadraoParaAssinaturaAsync(SessaoUsuario usuario, PapelAssinatura papel, long empresaId, CancellationToken ct = default)
    {
        if (await NegarAssinaturaAsync(usuario, empresaId, ct) is { } negado) return Resultado<CertificadoDigital?>.Falha(negado);
        return Resultado<CertificadoDigital?>.Ok(await PadraoAsync(papel, empresaId, ct));
    }

    /// <summary>Abre o A1 guardado para assinar a ECD/ECF da empresa (só certificados da empresa ou gerais).</summary>
    public async Task<Resultado<X509Certificate2>> AbrirA1ParaAssinaturaAsync(SessaoUsuario usuario, long empresaId, long id, string senha, CancellationToken ct = default)
    {
        if (await NegarAssinaturaAsync(usuario, empresaId, ct) is { } negado) return Resultado<X509Certificate2>.Falha(negado);
        var c = (await repositorio.ListarAsync(ct)).FirstOrDefault(x => x.Id == id);
        if (c is null || (c.EmpresaId is not null && c.EmpresaId != empresaId)) return Resultado<X509Certificate2>.Falha("Certificado não disponível para esta empresa.");
        return await AbrirAsync(id, senha, ct);
    }

    /// <summary>Carrega o certificado A1 guardado, pronto para assinar ou autenticar (a senha é pedida no uso).</summary>
    public async Task<Resultado<X509Certificate2>> AbrirA1Async(SessaoUsuario usuario, long id, string senha, CancellationToken ct = default)
    {
        if (!usuario.Administrador) return Resultado<X509Certificate2>.Falha("Somente o administrador usa os certificados digitais.");
        return await AbrirAsync(id, senha, ct);
    }

    private async Task<Resultado<X509Certificate2>> AbrirAsync(long id, string senha, CancellationToken ct)
    {
        if (await repositorio.ObterArquivoAsync(id, ct) is not { } pfx) return Resultado<X509Certificate2>.Falha("Certificado A1 não encontrado.");
        try
        {
            return Resultado<X509Certificate2>.Ok(X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet));
        }
        catch (CryptographicException) { return Resultado<X509Certificate2>.Falha("Senha do certificado incorreta."); }
    }

    public const string TipoA1 = "A1", TipoA3 = "A3";

    /// <summary>
    /// A3 = chave em dispositivo (token/cartão: provedor de cartão inteligente ou chave não exportável); A1 = chave de software
    /// exportável. A regra pura, para ser testada em qualquer sistema.
    /// </summary>
    public static string ClassificarChave(string? provedor, bool exportavel, bool hardware)
        => hardware || !exportavel || (provedor?.Contains("Smart Card", StringComparison.OrdinalIgnoreCase) ?? false) ? TipoA3 : TipoA1;

    /// <summary>Tipo (A1 ou A3) do certificado com chave privada. Fora do Windows não há como ler o provedor: considera-se A1 se a chave for exportável.</summary>
    public static string TipoDoCertificado(X509Certificate2 c)
    {
        string? provedor = null;
        var exportavel = false;
        var hardware = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var rsa = c.GetRSAPrivateKey();
                if (rsa is RSACng cng)
                {
                    provedor = cng.Key.Provider?.Provider;
                    exportavel = (cng.Key.ExportPolicy & (CngExportPolicies.AllowExport | CngExportPolicies.AllowPlaintextExport)) != 0;
                }
                else if (rsa is RSACryptoServiceProvider csp)
                {
                    var info = csp.CspKeyContainerInfo;
                    provedor = info.ProviderName;
                    exportavel = info.Exportable;
                    hardware = info.HardwareDevice || info.Removable;
                }
                else exportavel = PodeExportar(c);
            }
            catch (CryptographicException) { exportavel = false; }
        }
        else exportavel = PodeExportar(c);
        return ClassificarChave(provedor, exportavel, hardware);
    }

    private static bool PodeExportar(X509Certificate2 c)
    {
        try { c.Export(X509ContentType.Pfx, "x"); return true; }
        catch (CryptographicException) { return false; }
    }

    /// <summary>Certificados pessoais com chave privada instalados no Windows, de todos os tipos.</summary>
    public static IReadOnlyList<DadosCertificado> ListarInstaladosNoWindows() => ListarInstaladosNoWindows(null);

    /// <summary>Certificados pessoais com chave privada instalados no Windows; com <paramref name="tipo"/> ("A1" ou "A3"), só desse tipo.
    /// O A3 nunca inclui certificados de software (A1).</summary>
    public static IReadOnlyList<DadosCertificado> ListarInstaladosNoWindows(string? tipo)
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates.Where(c => c.HasPrivateKey)
            .Select(c => Dados(c) with { Tipo = TipoDoCertificado(c) })
            .Where(d => tipo is null || d.Tipo == tipo).OrderBy(d => d.Titular).ToList();
    }

    /// <summary>Mensagem para quando o repositório do Windows não existe (Linux/macOS).</summary>
    public const string AvisoSomenteWindows = "O repositório de certificados do Windows (A3 em token/cartão e A1 instalados) só está disponível no Windows. Use um arquivo A1 (.pfx).";

    /// <summary>Exporta um certificado instalado (com chave exportável) para .pfx protegido pela senha dada, para importá-lo como A1.</summary>
    public static Resultado<byte[]> ExportarComoPfx(X509Certificate2 cert, string senha)
    {
        if (string.IsNullOrEmpty(senha)) return Resultado<byte[]>.Falha("Informe uma senha de proteção para o arquivo do certificado.");
        if (!cert.HasPrivateKey) return Resultado<byte[]>.Falha("O certificado não tem chave privada.");
        try { return Resultado<byte[]>.Ok(cert.Export(X509ContentType.Pfx, senha)); }
        catch (CryptographicException)
        {
            return Resultado<byte[]>.Falha("A chave privada deste certificado não é exportável (token/cartão A3 ou chave protegida): ele não pode ser importado como A1. Registre-o como A3.");
        }
    }

    /// <summary>Exporta (como .pfx) o certificado instalado no Windows com a impressão dada.</summary>
    public static Resultado<byte[]> ExportarInstaladoComoPfx(string impressao, string senha)
    {
        if (!OperatingSystem.IsWindows()) return Resultado<byte[]>.Falha(AvisoSomenteWindows);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        using var cert = store.Certificates.Find(X509FindType.FindByThumbprint, impressao.Replace(" ", ""), validOnly: false)
            .Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey);
        return cert is null ? Resultado<byte[]>.Falha("Certificado não encontrado no repositório do Windows.") : ExportarComoPfx(cert, senha);
    }

    public static DadosCertificado Dados(X509Certificate2 c)
    {
        var nome = c.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        // ICP-Brasil: "RAZAO SOCIAL:12345678000190" (e-CNPJ) ou "NOME:12345678901" (e-CPF).
        var m = Regex.Match(nome, @"^(?<titular>.*?):(?<doc>\d{11}|\d{14})$");
        var titular = m.Success ? m.Groups["titular"].Value.Trim() : nome;
        var doc = m.Success ? m.Groups["doc"].Value : null;
        return new DadosCertificado(titular, doc, c.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
            new DateTimeOffset(c.NotBefore.ToUniversalTime()), new DateTimeOffset(c.NotAfter.ToUniversalTime()), c.Thumbprint.ToUpperInvariant(),
            c.HasPrivateKey);
    }

    private static string Truncar(string s, int max) => s.Length <= max ? s : s[..max];
}
