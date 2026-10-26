using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using Vorticis.Application.Sistema;
using Vorticis.Application.Sped;
using Vorticis.Domain.Sped;
using Vorticis.Infra.Sped;

namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Passo "Assinaturas" dos assistentes da ECD e da ECF: lista os certificados (cadastrados, A3 do Windows), aplica o certificado padrão a
/// cada signatário e abre os certificados escolhidos (A1 com senha, A3 pelo repositório do Windows) conferindo o titular com o signatário.
/// </summary>
public sealed class AssinantesSped(IContexto contexto, ObservableCollection<string> erros)
{
    public ObservableCollection<SignatarioAssinatura> Signatarios { get; } = [];
    public ObservableCollection<DadosCertificado> Instalados { get; } = [];
    public ObservableCollection<CertificadoDigital> Cadastrados { get; } = [];
    public List<CertificadoAssinante> Assinantes { get; } = [];

    public static bool RepositorioWindowsDisponivel => OperatingSystem.IsWindows();
    public static string AvisoRepositorio => RepositorioWindowsDisponivel ? ""
        : "Certificados A3 / repositório do Windows só estão disponíveis no Windows. Use um arquivo A1 (.pfx), um certificado cadastrado ou assine no PVA.";

    /// <summary>Lê os certificados do usuário no Windows e os cadastrados em Configurações › Certificados digitais; já escolhe os padrão.</summary>
    public async Task CarregarCertificadosAsync()
    {
        Instalados.Clear();
        Cadastrados.Clear();
        try { foreach (var d in ServicoCertificados.ListarInstaladosNoWindows(ServicoCertificados.TipoA3)) Instalados.Add(d); }   // A3: nunca os A1
        catch (Exception ex) { erros.Add($"Não foi possível ler o repositório do Windows: {ex.Message}"); }
        if (contexto.App is { } app && contexto.Sessao is { } sessao && contexto.EmpresaId is { } empresa)
        {
            // Quem gera a ECD/ECF (perfil Contador, por exemplo) usa os certificados cadastrados da empresa, mesmo sem ser administrador.
            var r = await app.Certificados.ListarParaAssinaturaAsync(sessao, empresa);
            if (r.Sucesso) foreach (var c in r.Valor) Cadastrados.Add(c);
            else foreach (var e in r.Erros) erros.Add(e);
            // Certificados padrão (Configurações › Certificados digitais): já vêm escolhidos, com a senha guardada neste computador.
            foreach (var s in Signatarios.Where(x => x.Modo == ModoAssinatura.Nenhum))
            {
                var papel = s.Signatario.Contador ? PapelAssinatura.Contador
                    : s.Signatario.CodigoQualificacao == QualificacaoAssinante.Procurador ? PapelAssinatura.Procurador : PapelAssinatura.Responsavel;
                var padrao = await app.Certificados.ObterPadraoParaAssinaturaAsync(sessao, papel, empresa);
                if (!padrao.Sucesso || padrao.Valor is not { } p) continue;
                s.Cadastrado = Cadastrados.FirstOrDefault(c => c.Id == p.Id);
                s.Senha = p.Tipo == "A1" ? SenhasCertificados.Obter(p.Id) ?? "" : "";
                s.Modo = ModoAssinatura.Cadastrado;
            }
        }
    }

    /// <summary>Confere cada escolha de certificado e abre os certificados (A1 com senha, A3 pelo repositório do Windows).</summary>
    public async Task<bool> PrepararAsync()
    {
        erros.Clear();
        foreach (var c in Assinantes) c.Certificado.Dispose();
        Assinantes.Clear();
        if (contexto.App is not { } app || contexto.Sessao is not { } sessao || contexto.EmpresaId is not { } empresa) return false;
        var ok = true;
        foreach (var s in Signatarios)
        {
            if (!s.DocumentoValido)
            {
                erros.Add($"{s.Nome}: o CPF/CNPJ {s.Documento} do cadastro é inválido. Corrija no cadastro (Sócios ou Contabilidade) antes de assinar.");
                ok = false;
                continue;
            }
            if (s.Modo == ModoAssinatura.Nenhum) continue;
            try
            {
                X509Certificate2? cert = null;
                switch (s.Modo)
                {
                    case ModoAssinatura.ArquivoA1:
                        if (string.IsNullOrWhiteSpace(s.ArquivoPfx) || !File.Exists(s.ArquivoPfx)) { erros.Add($"{s.Nome}: escolha o arquivo .pfx do certificado A1."); ok = false; continue; }
                        cert = CertificadosSped.AbrirPfx(await File.ReadAllBytesAsync(s.ArquivoPfx), s.Senha);
                        break;
                    case ModoAssinatura.RepositorioWindows:
                        if (!RepositorioWindowsDisponivel) { erros.Add($"{s.Nome}: {AvisoRepositorio}"); ok = false; continue; }
                        if (s.Instalado is null) { erros.Add($"{s.Nome}: escolha o certificado do repositório do Windows."); ok = false; continue; }
                        cert = CertificadosSped.DoRepositorioWindows(s.Instalado.Impressao);
                        if (cert is null) { erros.Add($"{s.Nome}: certificado não encontrado no Windows (token conectado?)."); ok = false; continue; }
                        break;
                    case ModoAssinatura.Cadastrado:
                        if (s.Cadastrado is not { } cad) { erros.Add($"{s.Nome}: escolha o certificado cadastrado."); ok = false; continue; }
                        if (cad.Tipo == "A1")
                        {
                            var a1 = await app.Certificados.AbrirA1ParaAssinaturaAsync(sessao, empresa, cad.Id, s.Senha);
                            if (!a1.Sucesso) { foreach (var e in a1.Erros) erros.Add($"{s.Nome}: {e}"); ok = false; continue; }
                            cert = a1.Valor;
                        }
                        else
                        {
                            cert = OperatingSystem.IsWindows() ? CertificadosSped.DoRepositorioWindows(cad.Impressao) : null;
                            if (cert is null) { erros.Add($"{s.Nome}: o certificado A3 \"{cad.Descricao}\" não está disponível neste computador."); ok = false; continue; }
                        }
                        break;
                }
                if (cert is null) continue;
                var doc = ServicoEcd.DocumentoIcpBrasil(cert);
                if (doc is not null && doc != s.Signatario.CpfCnpj)
                {
                    erros.Add($"{s.Nome}: o certificado escolhido é de outro titular ({doc}); o signatário pede {s.Documento}.");
                    cert.Dispose();
                    ok = false;
                    continue;
                }
                Assinantes.Add(new CertificadoAssinante(cert, s.Signatario.CodigoQualificacao));
            }
            catch (Exception ex)
            {
                erros.Add($"{s.Nome}: {ex.Message}");
                ok = false;
            }
        }
        return ok;
    }
}
