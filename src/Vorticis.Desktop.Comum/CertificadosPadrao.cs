using System.Security.Cryptography.X509Certificates;
using Vorticis.Application.Relatorios;
using Vorticis.Application.Sistema;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Infra.Sped;

namespace Vorticis.Desktop.Comum;

/// <summary>Abre o certificado padrão (Configurações › Certificados digitais) para assinar sem perguntar de novo.</summary>
public static class CertificadosPadrao
{
    /// <summary>Certificado padrão do papel para a empresa ativa, já aberto. A1: usa a senha guardada neste computador (ou pede); A3: pelo repositório do Windows.</summary>
    public static async Task<Resultado<X509Certificate2>> AbrirAsync(IContexto contexto, PapelAssinatura papel)
    {
        if (contexto.App is not { } app || contexto.Sessao is not { } sessao) return Resultado<X509Certificate2>.Falha("Sessão encerrada. Entre novamente.");
        var r = await app.Certificados.ObterPadraoAsync(sessao, papel, contexto.EmpresaId);
        if (!r.Sucesso) return Resultado<X509Certificate2>.Falha(r.Erros);
        if (r.Valor is not { } c)
            return Resultado<X509Certificate2>.Falha($"Não há certificado padrão {(papel == PapelAssinatura.Contador ? "do contador" : "do responsável")} válido. Defina em Configurações › Certificados digitais.");
        return await AbrirAsync(contexto, c);
    }

    /// <summary>
    /// Certificado padrão do papel para assinar/transmitir o eSocial da empresa ativa (quem tem a função de eSocial na empresa pode usar,
    /// não só o administrador). Papel Responsável = certificado da própria empresa (e-CNPJ) ou do responsável.
    /// </summary>
    public static async Task<Resultado<X509Certificate2>> AbrirParaEsocialAsync(IContexto contexto, PapelAssinatura papel)
    {
        if (contexto.App is not { } app || contexto.Sessao is not { } sessao || contexto.EmpresaId is not { } empresa)
            return Resultado<X509Certificate2>.Falha("Sessão encerrada ou empresa não selecionada.");
        var r = await app.Certificados.ObterPadraoParaAssinaturaAsync(sessao, papel, empresa);
        if (!r.Sucesso) return Resultado<X509Certificate2>.Falha(r.Erros);
        if (r.Valor is not { } c)
            return Resultado<X509Certificate2>.Falha($"Não há certificado padrão ({papel switch { PapelAssinatura.Contador => "do contador", PapelAssinatura.Procurador => "do procurador", _ => "da empresa/responsável" }}) válido. Defina em Configurações › Certificados digitais.");
        if (c.Tipo == "A1")
        {
            var senha = SenhasCertificados.Obter(c.Id)
                        ?? await contexto.SolicitarSenhaAsync("Senha do certificado", $"Informe a senha do certificado \"{c.Descricao}\".");
            if (senha is null) return Resultado<X509Certificate2>.Falha("Operação cancelada: senha do certificado não informada.");
            return await app.Certificados.AbrirA1ParaAssinaturaAsync(sessao, empresa, c.Id, senha);
        }
        return await AbrirAsync(contexto, c);
    }

    public static async Task<Resultado<X509Certificate2>> AbrirAsync(IContexto contexto, CertificadoDigital c)
    {
        if (contexto.App is not { } app || contexto.Sessao is not { } sessao) return Resultado<X509Certificate2>.Falha("Sessão encerrada. Entre novamente.");
        if (c.Tipo == "A1")
        {
            var senha = SenhasCertificados.Obter(c.Id)
                        ?? await contexto.SolicitarSenhaAsync("Senha do certificado", $"Informe a senha do certificado \"{c.Descricao}\".");
            if (senha is null) return Resultado<X509Certificate2>.Falha("Assinatura cancelada: senha do certificado não informada.");
            return await app.Certificados.AbrirA1Async(sessao, c.Id, senha);
        }
        if (!OperatingSystem.IsWindows()) return Resultado<X509Certificate2>.Falha(ServicoCertificados.AvisoSomenteWindows);
        var cert = CertificadosSped.DoRepositorioWindows(c.Impressao);
        return cert is null
            ? Resultado<X509Certificate2>.Falha($"O certificado A3 \"{c.Descricao}\" não foi encontrado neste computador. Conecte o token/cartão.")
            : Resultado<X509Certificate2>.Ok(cert);
    }

    /// <summary>Assinatura digital do PDF: certificado padrão do contador, ou o do responsável se não houver.</summary>
    public static async Task<Resultado<AssinaturaDigitalPdf>> ParaPdfAsync(IContexto contexto, string motivo)
    {
        if (contexto.App is not { } app || contexto.Sessao is not { } sessao) return Resultado<AssinaturaDigitalPdf>.Falha("Sessão encerrada. Entre novamente.");
        foreach (var papel in new[] { PapelAssinatura.Contador, PapelAssinatura.Responsavel })
        {
            var p = await app.Certificados.ObterPadraoAsync(sessao, papel, contexto.EmpresaId);
            if (!p.Sucesso) return Resultado<AssinaturaDigitalPdf>.Falha(p.Erros);
            if (p.Valor is null) continue;
            var cert = await AbrirAsync(contexto, p.Valor);
            return cert.Sucesso ? Resultado<AssinaturaDigitalPdf>.Ok(new AssinaturaDigitalPdf(cert.Valor, motivo)) : Resultado<AssinaturaDigitalPdf>.Falha(cert.Erros);
        }
        return Resultado<AssinaturaDigitalPdf>.Falha("Não há certificado padrão para assinar o PDF. Defina o do contador ou o do responsável em Configurações › Certificados digitais.");
    }
}
