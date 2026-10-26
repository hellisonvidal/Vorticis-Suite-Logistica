using System.Security.Cryptography.X509Certificates;
using Vorticis.Application.Seguranca;
using Vorticis.Application.Sped;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;

namespace Vorticis.Fiscal.Sped;

/// <summary>Linha do resumo mostrado na tela e devolvido com o arquivo (ex.: "ICMS a recolher").</summary>
public sealed record ResumoItemEfd(string Rotulo, decimal Valor);

/// <summary>Apuração mensal para os livros de apuração do ICMS e do IPI (o IPI é nulo se não há itens com IPI no mês).</summary>
public sealed record LivroApuracao(DateOnly Inicio, DateOnly Fim, ApuracaoEfdIcms Icms, ApuracaoIpiEfd? Ipi);

/// <summary>Resultado da conferência prévia (passo "pendências" do assistente).</summary>
public sealed record PreviaEfd(TipoEfd Tipo, DateOnly Inicio, DateOnly Fim, IReadOnlyList<PendenciaEcd> Pendencias, int Documentos, IReadOnlyList<ResumoItemEfd> Resumo)
{
    public bool PodeGerar => !Pendencias.Any(p => p.Bloqueante);
    public IEnumerable<PendenciaEcd> Bloqueantes => Pendencias.Where(p => p.Bloqueante);
    public IEnumerable<PendenciaEcd> Avisos => Pendencias.Where(p => !p.Bloqueante);
}

/// <summary>Arquivo da EFD pronto para importar no PVA (assinado ou não).</summary>
public sealed record ArquivoEfd(TipoEfd Tipo, string NomeArquivo, byte[] Conteudo, string HashSha1, int Linhas, IReadOnlyDictionary<string, int> Registros,
                                IReadOnlyList<AssinaturaSped> Assinaturas, IReadOnlyList<PendenciaEcd> Avisos, IReadOnlyList<ResumoItemEfd> Resumo);

/// <summary>
/// Geração da EFD ICMS/IPI (SPED Fiscal) e da EFD-Contribuições a partir dos documentos fiscais e da apuração do módulo Fiscal.
/// Mesmo fluxo da ECD/ECF: <c>Validar</c> (pendências), <c>Gerar</c> (arquivo ISO-8859-1, já assinado se receber certificados) e <see cref="Assinar"/>.
/// </summary>
public sealed class ServicoEfd(IRepositorioEfd repositorio, Autorizador autorizador, IAssinadorSped? assinador = null)
{
    private static (DateOnly Ini, DateOnly Fim) Mes(DateOnly d) => (new DateOnly(d.Year, d.Month, 1), new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month)));

    // ------------------------------------------------------------------------------------------------ configuração

    public async Task<Resultado<ConfiguracaoEfd>> ConfiguracaoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ConfiguracaoEfd>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        return Resultado<ConfiguracaoEfd>.Ok(await repositorio.ConfiguracaoAsync(empresaId, ct));
    }

    public async Task<Resultado<bool>> SalvarConfiguracaoAsync(long empresaId, ConfiguracaoEfd c, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<bool>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        if (c.IcmsPerfil is not ('A' or 'B' or 'C')) return Resultado<bool>.Falha("Perfil inválido: use A, B ou C.");
        if (c.IcmsIndicadorAtividade is not ('0' or '1')) return Resultado<bool>.Falha("Indicador de atividade inválido.");
        if (c.IcmsDiaVencimento is < 1 or > 28) return Resultado<bool>.Falha("O dia de vencimento do ICMS deve estar entre 1 e 28.");
        if (c.ContribNaturezaPj is not ("00" or "01" or "02")) return Resultado<bool>.Falha("Natureza da pessoa jurídica inválida (00, 01 ou 02).");
        if (c.ContribIndicadorAtividade is not ('0' or '1' or '2' or '3' or '4' or '9')) return Resultado<bool>.Falha("Indicador de atividade da EFD-Contribuições inválido.");
        await repositorio.SalvarConfiguracaoAsync(empresaId, c, ct);
        return Resultado<bool>.Ok(true);
    }

    // ------------------------------------------------------------------------------------------------ livros de apuração

    /// <summary>
    /// Livros de Registro de Apuração do ICMS (modelo 9) e do IPI (modelo 8) de um mês, com os mesmos números do E110 e do E520 da EFD ICMS/IPI
    /// (o cálculo é o do gerador da EFD). Exige a permissão de relatórios fiscais.
    /// </summary>
    public async Task<Resultado<LivroApuracao>> LivroApuracaoAsync(long empresaId, DateOnly competencia, decimal saldoCredorAnterior, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<LivroApuracao>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) is { } negado) return negado;
        if (saldoCredorAnterior < 0) return Resultado<LivroApuracao>.Falha("O saldo credor do período anterior não pode ser negativo.");
        var p = await PrepararIcmsAsync(empresaId, competencia, new OpcoesEfdIcms(SaldoCredorAnterior: saldoCredorAnterior), ct);
        if (!p.Sucesso) return Resultado<LivroApuracao>.Falha(p.Erros);
        var (d, ap, _) = p.Valor;
        return Resultado<LivroApuracao>.Ok(new LivroApuracao(d.Inicio, d.Fim, ap, CalculoEfdIcms.CalcularIpi(d.Documentos)));
    }

    // ------------------------------------------------------------------------------------------------ EFD ICMS/IPI

    private async Task<Resultado<(DadosEfdIcms Dados, ApuracaoEfdIcms Apuracao, List<PendenciaEcd> Pendencias)>> PrepararIcmsAsync(long empresaId, DateOnly competencia,
                                                                                                                                      OpcoesEfdIcms opcoes, CancellationToken ct)
    {
        var (ini, fim) = Mes(competencia);
        var empresa = await repositorio.EmpresaAsync(empresaId, ini, fim, ct);
        if (empresa is null) return Resultado<(DadosEfdIcms, ApuracaoEfdIcms, List<PendenciaEcd>)>.Falha("Empresa não encontrada.");
        var dados = new DadosEfdIcms(empresa, await repositorio.ContadorAsync(empresaId, ini, fim, ct), ini, fim, await repositorio.DocumentosAsync(empresaId, ini, fim, ct), opcoes,
                                     await repositorio.AjustesIcmsAsync(empresaId, ini, ct), await repositorio.ContasConsumoAsync(empresaId, ini, fim, ct),
                                     await repositorio.InventarioAsync(empresaId, ini, ct));
        var ap = CalculoEfdIcms.Apurar(dados);
        return Resultado<(DadosEfdIcms, ApuracaoEfdIcms, List<PendenciaEcd>)>.Ok((dados, ap, ValidadorEfd.ValidarIcms(dados, ap).ToList()));
    }

    private static List<ResumoItemEfd> ResumoIcms(ApuracaoEfdIcms ap)
    {
        var r = new List<ResumoItemEfd>
        {
            new("Débitos do ICMS (saídas)", ap.Debitos), new("Créditos do ICMS (entradas)", ap.Creditos), new("Saldo credor do período anterior", ap.SaldoCredorAnterior),
            new("ICMS a recolher", ap.Recolher), new("Saldo credor a transportar", ap.SaldoCredorTransportar),
        };
        if (ap.AjustesDebitos + ap.EstornosCreditos > 0) r.Add(new("Ajustes a débito e estornos de créditos (E111)", ap.AjustesDebitos + ap.EstornosCreditos));
        if (ap.AjustesCreditos + ap.EstornosDebitos > 0) r.Add(new("Ajustes a crédito e estornos de débitos (E111)", ap.AjustesCreditos + ap.EstornosDebitos));
        if (ap.Deducoes > 0) r.Add(new("Deduções (E111)", ap.Deducoes));
        if (ap.DebitosEspeciais > 0) r.Add(new("Débitos especiais (E111)", ap.DebitosEspeciais));
        if (ap.St.Count > 0) r.Add(new("ICMS-ST a recolher", ap.St.Sum(s => s.Recolher)));
        if (ap.Ipi is { } ipi) { r.Add(new("IPI — débitos", ipi.Debitos)); r.Add(new("IPI — créditos", ipi.Creditos)); }
        return r;
    }

    public async Task<Resultado<PreviaEfd>> ValidarIcmsAsync(long empresaId, DateOnly competencia, OpcoesEfdIcms opcoes, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<PreviaEfd>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        var p = await PrepararIcmsAsync(empresaId, competencia, opcoes, ct);
        if (!p.Sucesso) return Resultado<PreviaEfd>.Falha(p.Erros);
        var (d, ap, pend) = p.Valor;
        return Resultado<PreviaEfd>.Ok(new PreviaEfd(TipoEfd.IcmsIpi, d.Inicio, d.Fim, pend, CalculoEfdIcms.Escrituraveis(d.Documentos).Count(), ResumoIcms(ap)));
    }

    public async Task<Resultado<ArquivoEfd>> GerarIcmsAsync(long empresaId, DateOnly competencia, OpcoesEfdIcms opcoes, long usuarioId,
                                                           IReadOnlyList<CertificadoAssinante>? assinantes = null, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ArquivoEfd>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        var p = await PrepararIcmsAsync(empresaId, competencia, opcoes, ct);
        if (!p.Sucesso) return Resultado<ArquivoEfd>.Falha(p.Erros);
        var (d, ap, pend) = p.Valor;
        if (pend.Any(x => x.Bloqueante)) return Resultado<ArquivoEfd>.Falha(pend.Where(x => x.Bloqueante).Select(x => x.Mensagem));
        var arq = GeradorEfdIcms.Gerar(d, ap);
        return await FinalizarAsync(empresaId, TipoEfd.IcmsIpi, d.Inicio, d.Fim, opcoes.Perfil.ToString(), opcoes.Retificadora, arq, assinantes, pend, ResumoIcms(ap),
            $"EFD_ICMS_IPI_{d.Empresa.Cnpj}_{d.Inicio:yyyyMM}.txt", d.Empresa, usuarioId, ct);
    }

    // ------------------------------------------------------------------------------------------------ EFD-Contribuições

    private async Task<Resultado<(DadosEfdContribuicoes Dados, List<PendenciaEcd> Pendencias)>> PrepararContribuicoesAsync(long empresaId, DateOnly competencia,
                                                                                                                           OpcoesEfdContribuicoes opcoes, CancellationToken ct)
    {
        var (ini, fim) = Mes(competencia);
        var empresa = await repositorio.EmpresaAsync(empresaId, ini, fim, ct);
        if (empresa is null) return Resultado<(DadosEfdContribuicoes, List<PendenciaEcd>)>.Falha("Empresa não encontrada.");
        var dados = new DadosEfdContribuicoes(empresa, await repositorio.ContadorAsync(empresaId, ini, fim, ct), ini, fim, await repositorio.DocumentosAsync(empresaId, ini, fim, ct),
            await repositorio.ApuracaoPisCofinsAsync(empresaId, ini, ct), opcoes);
        return Resultado<(DadosEfdContribuicoes, List<PendenciaEcd>)>.Ok((dados, ValidadorEfd.ValidarContribuicoes(dados).ToList()));
    }

    private static List<ResumoItemEfd> ResumoContribuicoes(DadosEfdContribuicoes d)
    {
        if (d.Apuracao is not { } ap) return [];
        var pis = GeradorEfdContribuicoes.CalcularM(ap, true);
        var cof = GeradorEfdContribuicoes.CalcularM(ap, false);
        return
        [
            new("Receita base da apuração", ap.ReceitaBase), new("PIS — contribuição do período", pis.Debito), new("PIS — crédito descontado", pis.CreditoDescontado),
            new("PIS — retido na fonte", pis.Retido), new("PIS a recolher (M200)", pis.TotalARecolher),
            new("COFINS — contribuição do período", cof.Debito), new("COFINS — crédito descontado", cof.CreditoDescontado), new("COFINS — retida na fonte", cof.Retido),
            new("COFINS a recolher (M600)", cof.TotalARecolher),
        ];
    }

    public async Task<Resultado<PreviaEfd>> ValidarContribuicoesAsync(long empresaId, DateOnly competencia, OpcoesEfdContribuicoes opcoes, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<PreviaEfd>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        var p = await PrepararContribuicoesAsync(empresaId, competencia, opcoes, ct);
        if (!p.Sucesso) return Resultado<PreviaEfd>.Falha(p.Erros);
        var (d, pend) = p.Valor;
        return Resultado<PreviaEfd>.Ok(new PreviaEfd(TipoEfd.Contribuicoes, d.Inicio, d.Fim, pend, d.Documentos.Count, ResumoContribuicoes(d)));
    }

    public async Task<Resultado<ArquivoEfd>> GerarContribuicoesAsync(long empresaId, DateOnly competencia, OpcoesEfdContribuicoes opcoes, long usuarioId,
                                                                    IReadOnlyList<CertificadoAssinante>? assinantes = null, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ArquivoEfd>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        var p = await PrepararContribuicoesAsync(empresaId, competencia, opcoes, ct);
        if (!p.Sucesso) return Resultado<ArquivoEfd>.Falha(p.Erros);
        var (d, pend) = p.Valor;
        if (pend.Any(x => x.Bloqueante)) return Resultado<ArquivoEfd>.Falha(pend.Where(x => x.Bloqueante).Select(x => x.Mensagem));
        var arq = GeradorEfdContribuicoes.Gerar(d);
        return await FinalizarAsync(empresaId, TipoEfd.Contribuicoes, d.Inicio, d.Fim, null, opcoes.Retificadora, arq, assinantes, pend, ResumoContribuicoes(d),
            $"EFD_CONTRIBUICOES_{d.Empresa.Cnpj}_{d.Inicio:yyyyMM}.txt", d.Empresa, usuarioId, ct);
    }

    // ------------------------------------------------------------------------------------------------ assinatura e histórico

    private async Task<Resultado<ArquivoEfd>> FinalizarAsync(long empresaId, TipoEfd tipo, DateOnly ini, DateOnly fim, string? perfil, bool retificadora, ArquivoGeradoEfd arq,
                                                             IReadOnlyList<CertificadoAssinante>? assinantes, List<PendenciaEcd> pendencias, IReadOnlyList<ResumoItemEfd> resumo,
                                                             string nome, EmpresaEfd empresa, long usuarioId, CancellationToken ct)
    {
        var conteudo = arq.Conteudo;
        IReadOnlyList<AssinaturaSped> assinaturas = [];
        if (assinantes is { Count: > 0 })
        {
            var assinado = Assinar(conteudo, assinantes);
            if (!assinado.Sucesso) return Resultado<ArquivoEfd>.Falha(assinado.Erros);
            conteudo = assinado.Valor;
            assinaturas = assinador!.LerAssinaturas(conteudo);
        }
        await repositorio.RegistrarAsync(empresaId, tipo, ini, fim, perfil, retificadora ? "1" : "0", arq.HashSha1, arq.QuantidadeLinhas, assinaturas.Count, usuarioId, ct);
        _ = empresa;
        return Resultado<ArquivoEfd>.Ok(new ArquivoEfd(tipo, nome, conteudo, arq.HashSha1, arq.QuantidadeLinhas, arq.Registros, assinaturas,
            pendencias.Where(x => !x.Bloqueante).ToList(), resumo));
    }

    /// <summary>
    /// Acrescenta assinaturas CMS ao fim do arquivo (mesmo esquema SBRCAAEPDR do PVA da ECD/ECF; o PVA da EFD usa a mesma biblioteca de assinatura,
    /// mas a conferência no PVA da EFD não foi testada — a EFD oficial costuma ser assinada no próprio PVA, na transmissão).
    /// </summary>
    public Resultado<byte[]> Assinar(byte[] arquivo, IReadOnlyList<CertificadoAssinante> assinantes)
    {
        if (assinador is null) return Resultado<byte[]>.Falha("Assinatura digital não configurada.");
        if (assinantes.Count == 0) return Resultado<byte[]>.Falha("Escolha ao menos um certificado.");
        var erros = new List<string>();
        var agora = DateTime.Now;
        foreach (var a in assinantes)
        {
            var nome = a.Certificado.GetNameInfo(X509NameType.SimpleName, false);
            if (!a.Certificado.HasPrivateKey) erros.Add($"O certificado de {nome} não tem a chave privada.");
            else if (a.Certificado.NotAfter < agora || a.Certificado.NotBefore > agora) erros.Add($"Certificado de {nome} fora da validade ({a.Certificado.NotAfter:dd/MM/yyyy}).");
        }
        return erros.Count > 0 ? Resultado<byte[]>.Falha(erros) : Resultado<byte[]>.Ok(assinador.Assinar(arquivo, assinantes));
    }

    public IReadOnlyList<AssinaturaSped> LerAssinaturas(byte[] arquivo) => assinador?.LerAssinaturas(arquivo) ?? [];

    public async Task<Resultado<IReadOnlyList<EfdRegistrada>>> ListarGeradasAsync(long empresaId, TipoEfd? tipo, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<EfdRegistrada>>(usuarioId, empresaId, Permissao.FiscalSpedGerar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<EfdRegistrada>>.Ok(await repositorio.ListarAsync(empresaId, tipo, ct));
    }
}
