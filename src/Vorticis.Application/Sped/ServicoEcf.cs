using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;

namespace Vorticis.Application.Sped;

/// <summary>Escolhas do usuário para a ECF (tela/assistente). Os nulos são deduzidos do cadastro.</summary>
/// <param name="InformarEcd">Informa o hash da ECD gerada pelo Vorticis no ano (C040). Padrão: bloco C sem dados (o PVA recupera a ECD).</param>
/// <param name="AssinarComoProcurador">O procurador da empresa (procuração vigente, qualificação 309) assina no lugar do responsável; a transmissão exige a procuração eletrônica no e-CAC.</param>
/// <param name="IndicadorReceita">0010.IND_REC_RECEITA (Lucro Presumido): 1 caixa, 2 competência; nulo = pelo regime de caixa do cadastro (1) ou competência (2).</param>
public sealed record OpcoesEcf(
    bool Retificadora = false,
    string? NumeroRecibo = null,
    bool InformarEcd = false,
    char? IndicadorSituacaoInicio = null,
    char? IndicadorReceita = null,
    bool ResponsavelPessoaJuridica = false,
    string? CodigoQualificacaoSocio = null,
    bool AssinarComoProcurador = false);

/// <summary>Resultado da conferência prévia (passo "pendências" do assistente).</summary>
public sealed record PreviaEcf(int Ano, DateOnly Inicio, DateOnly Fim, FormaTributacaoEcf Forma, IReadOnlyList<SignatarioEcd> Signatarios,
                               IReadOnlyList<PendenciaEcd> Pendencias, int Lancamentos, int Periodos)
{
    public bool PodeGerar => !Pendencias.Any(p => p.Bloqueante);
    public IEnumerable<PendenciaEcd> Bloqueantes => Pendencias.Where(p => p.Bloqueante);
    public IEnumerable<PendenciaEcd> Avisos => Pendencias.Where(p => !p.Bloqueante);
    public string NomeForma => Forma == FormaTributacaoEcf.Real ? "Lucro Real" : "Lucro Presumido";
}

/// <summary>Arquivo da ECF pronto para importar no PVA (assinado ou não).</summary>
public sealed record ArquivoEcf(string NomeArquivo, byte[] Conteudo, string HashSha1, int Linhas, IReadOnlyDictionary<string, int> Registros,
                                IReadOnlyList<AssinaturaSped> Assinaturas, IReadOnlyList<PendenciaEcd> Avisos, FormaTributacaoEcf Forma);

/// <summary>
/// Gerador da ECF (Escrituração Contábil Fiscal), leiaute 12, Lucro Real e Lucro Presumido trimestrais. Mesmo fluxo da ECD:
/// <see cref="ValidarAsync"/> (pendências), <see cref="GerarAsync"/> (arquivo ISO-8859-1 já assinado quando recebe certificados) e
/// <see cref="Assinar"/> (assinaturas posteriores). Empresas do Simples Nacional não entregam ECF.
/// </summary>
public sealed class ServicoEcf(IConsultasEcf consultas, ServicoEcd ecd, Autorizador autorizador, IAssinadorSped? assinador = null)
{
    private sealed record Preparo(DadosEcf Dados, CalculoEcf Calculo, List<PendenciaEcd> Pendencias);

    public async Task<Resultado<PreviaEcf>> ValidarAsync(long empresaId, int ano, OpcoesEcf opcoes, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<PreviaEcf>(usuarioId, empresaId, Permissao.SpedEcfGerar, ct) is { } negado) return negado;
        var preparo = await PrepararAsync(empresaId, ano, opcoes, ct);
        if (!preparo.Sucesso) return Resultado<PreviaEcf>.Falha(preparo.Erros);
        var (d, calc, pendencias) = preparo.Valor;
        return Resultado<PreviaEcf>.Ok(new PreviaEcf(ano, d.Parametros.Inicio, d.Parametros.Fim, d.Parametros.Forma, d.Signatarios, pendencias,
            d.Lancamentos.Count, calc.Periodos.Count));
    }

    /// <summary>Gera a ECF. Com pendências bloqueantes, falha com a lista delas. Sem <paramref name="assinantes"/>, o arquivo sai sem assinatura.</summary>
    public async Task<Resultado<ArquivoEcf>> GerarAsync(long empresaId, int ano, OpcoesEcf opcoes, long usuarioId,
                                                       IReadOnlyList<CertificadoAssinante>? assinantes = null, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ArquivoEcf>(usuarioId, empresaId, Permissao.SpedEcfGerar, ct) is { } negado) return negado;
        var preparo = await PrepararAsync(empresaId, ano, opcoes, ct);
        if (!preparo.Sucesso) return Resultado<ArquivoEcf>.Falha(preparo.Erros);
        var (d, calc, pendencias) = preparo.Valor;
        if (pendencias.Any(p => p.Bloqueante))
            return Resultado<ArquivoEcf>.Falha(pendencias.Where(p => p.Bloqueante).Select(p => p.Mensagem));

        var escrituracao = GeradorEcf.Gerar(d, calc);
        var conteudo = escrituracao.Conteudo;
        IReadOnlyList<AssinaturaSped> assinaturas = [];
        if (assinantes is { Count: > 0 })
        {
            var assinado = ecd.AssinarConferindo(conteudo, assinantes, d.Signatarios);
            if (!assinado.Sucesso) return Resultado<ArquivoEcf>.Falha(assinado.Erros);
            conteudo = assinado.Valor;
            assinaturas = assinador!.LerAssinaturas(conteudo);
        }
        var forma = d.Parametros.Forma == FormaTributacaoEcf.Real ? "REAL" : "PRESUMIDO";
        await consultas.RegistrarAsync(empresaId, ano, d.Parametros.Inicio, d.Parametros.Fim, forma, escrituracao.HashSha1, escrituracao.QuantidadeLinhas,
                                       assinaturas.Count, usuarioId, ct);
        var cnpj = new string(d.Empresa.Cnpj.Where(char.IsAsciiLetterOrDigit).ToArray());
        return Resultado<ArquivoEcf>.Ok(new ArquivoEcf($"ECF_{cnpj}_{d.Parametros.Inicio:yyyyMMdd}_{d.Parametros.Fim:yyyyMMdd}_{(forma == "REAL" ? "LucroReal" : "LucroPresumido")}.txt",
            conteudo, escrituracao.HashSha1, escrituracao.QuantidadeLinhas, escrituracao.Registros, assinaturas,
            pendencias.Where(p => !p.Bloqueante).ToList(), d.Parametros.Forma));
    }

    /// <summary>Acrescenta assinaturas a um arquivo da ECF já gerado (assinado em parte ou não).</summary>
    public Resultado<byte[]> Assinar(byte[] arquivo, IReadOnlyList<CertificadoAssinante> assinantes)
    {
        if (assinador is null) return Resultado<byte[]>.Falha("Assinatura digital não configurada.");
        if (assinantes.Count == 0) return Resultado<byte[]>.Falha("Escolha ao menos um certificado.");
        return ecd.AssinarConferindo(arquivo, assinantes, LerSignatariosDoArquivo(arquivo, assinador.TamanhoSemAssinaturas(arquivo)));
    }

    public IReadOnlyList<AssinaturaSped> LerAssinaturas(byte[] arquivo) => assinador?.LerAssinaturas(arquivo) ?? [];

    public async Task<Resultado<IReadOnlyList<EcfRegistrada>>> ListarGeradasAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<EcfRegistrada>>(usuarioId, empresaId, Permissao.SpedEcfGerar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<EcfRegistrada>>.Ok(await consultas.ListarAsync(empresaId, ct));
    }

    // ------------------------------------------------------------------------------------------------ preparo

    private async Task<Resultado<Preparo>> PrepararAsync(long empresaId, int ano, OpcoesEcf opcoes, CancellationToken ct)
    {
        var c = await consultas.CarregarAsync(empresaId, ano, ct);
        if (c is null) return Resultado<Preparo>.Falha($"Exercício {ano} não cadastrado para a empresa.");

        FormaTributacaoEcf forma;
        switch (c.Regime)
        {
            case "SIMPLES_NACIONAL" or "MEI":
                return Resultado<Preparo>.Falha($"Empresas do {(c.Regime == "MEI" ? "MEI" : "Simples Nacional")} não entregam a ECF (a declaração é a DEFIS/PGDAS-D). Nada a gerar para {ano}.");
            case "LUCRO_ARBITRADO" or "IMUNE_ISENTA":
                return Resultado<Preparo>.Falha("Esta versão da ECF cobre apenas Lucro Real e Lucro Presumido trimestrais; Lucro Arbitrado e Imunes/Isentas não são suportados.");
            case "LUCRO_REAL": forma = FormaTributacaoEcf.Real; break;
            case "LUCRO_PRESUMIDO": forma = FormaTributacaoEcf.Presumido; break;
            default:   // sem regime cadastrado: deduz das apurações do ano
                forma = c.Apuracoes.Any(a => a.Regime == FormaTributacaoEcf.Real) ? FormaTributacaoEcf.Real : FormaTributacaoEcf.Presumido;
                break;
        }

        var indicadorInicio = opcoes.IndicadorSituacaoInicio
            ?? (c.Empresa.DataInicioAtividades > new DateOnly(ano, 1, 1) && c.Empresa.DataInicioAtividades.Year == ano ? '1' : '0');
        var parametros = new ParametrosEcf(ano, c.InicioExercicio, c.FimExercicio, forma, c.CodPlanRef, indicadorInicio, opcoes.Retificadora,
            opcoes.NumeroRecibo?.Trim(), opcoes.InformarEcd, opcoes.IndicadorReceita ?? (c.RegimeCaixa ? '1' : '2'));
        var signatarios = await ecd.SignatariosAsync(empresaId, c.Empresa.Nome, c.Empresa.Cnpj, opcoes.ResponsavelPessoaJuridica, opcoes.CodigoQualificacaoSocio,
            null, null, opcoes.AssinarComoProcurador, "ECF", ct);
        var dados = new DadosEcf(c.Empresa, parametros, c.Plano, c.SaldosAbertura, c.Lancamentos, c.Referencial, c.Apuracoes, c.Lalur, c.ParteB, c.Socios,
            signatarios, opcoes.InformarEcd ? c.Ecd : null);
        var calc = new CalculoEcf(dados);
        var pendencias = ValidadorEcf.Validar(dados, calc).ToList();
        if (opcoes.AssinarComoProcurador) await ecd.AjustarPendenciasProcuradorAsync(empresaId, signatarios, pendencias, "ECF", ct);
        if (opcoes.InformarEcd && c.Ecd is null)
            pendencias.Add(new(GravidadePendencia.Aviso, "SEM_ECD_GERADA", "Nenhuma ECD gerada pelo Vorticis para o ano: o bloco C saiu sem dados."));
        if (c.PeriodosAbertos.Count > 0)
            pendencias.Add(new(GravidadePendencia.Aviso, "PERIODOS_ABERTOS",
                $"Competências ainda abertas: {string.Join(", ", c.PeriodosAbertos.Select(p => p.ToString("MM/yyyy")))}. Feche os períodos antes de entregar a ECF."));
        return Resultado<Preparo>.Ok(new Preparo(dados, calc, pendencias));
    }

    /// <summary>Signatários (0930) lidos do próprio arquivo, para assinar um arquivo gerado antes.</summary>
    private static IReadOnlyList<SignatarioEcd> LerSignatariosDoArquivo(byte[] arquivo, int tamanho)
    {
        var texto = FormatoSped.Latin1.GetString(arquivo, 0, tamanho);
        var res = new List<SignatarioEcd>();
        foreach (var linha in texto.Split("\r\n"))
        {
            if (!linha.StartsWith("|0930|", StringComparison.Ordinal)) continue;
            var f = linha.Split('|');   // |0930|NOME|CPF_CNPJ|QUALIF|CRC|EMAIL|FONE|
            if (f.Length < 8) continue;
            res.Add(new SignatarioEcd(f[2], f[3], QualificacaoAssinante.Descricao(f[4]), f[4], f[4] != QualificacaoAssinante.Contador, f[5], null, f[6], f[7]));
        }
        return res;
    }
}
