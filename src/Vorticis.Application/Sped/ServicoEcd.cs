using System.Text.RegularExpressions;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Sped;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Sped;

/// <summary>Escolhas do usuário para a ECD (tela/assistente). Os nulos são deduzidos do cadastro.</summary>
/// <param name="NumeroOrdem">Número do livro (I030/J900.NUM_ORD); nulo = o da última ECD gerada no ano ou o seguinte ao do ano anterior (ou 1).</param>
/// <param name="NivelAglutinacao">Nível do plano cujas contas viram as linhas de detalhe do Balanço e da DRE (padrão 3).</param>
/// <param name="DataArquivamentoAtos">I030.DT_ARQ — data de arquivamento dos atos constitutivos na Junta (data do NIRE);
/// nulo = data de início das atividades (o manual orienta usar a data de abertura quando não há NIRE).</param>
/// <param name="IndicadorSituacaoInicio">0000.IND_SIT_INI_PER; nulo = 1 (abertura) se a empresa começou as atividades no meio
/// do exercício, senão 0 (normal).</param>
/// <param name="ResponsavelPessoaJuridica">O responsável pela assinatura é o e-CNPJ da empresa (código 001) — a situação
/// recomendada pela RFB; senão, o sócio representante legal (e-CPF).</param>
/// <param name="CodigoQualificacaoSocio">Código de qualificação do sócio responsável (tabela do J930); nulo = deduzido da
/// qualificação no QSA (sócio-administrador → 205 Administrador...).</param>
/// <param name="NumeroCertidaoCrc">Certidão de regularidade profissional do contador (J930.NUM_SEQ_CRC, formato UF/AAAA/NÚMERO).</param>
/// <param name="AssinarComoProcurador">O responsável pela assinatura é o procurador da empresa (qualificação 309), com procuração vigente
/// na data da geração, no lugar do sócio ou do e-CNPJ. A transmissão exige a procuração eletrônica cadastrada no e-CAC.</param>
public sealed record OpcoesEcd(
    int? NumeroOrdem = null,
    string NaturezaLivro = "DIÁRIO GERAL",
    short NivelAglutinacao = 3,
    DateOnly? DataArquivamentoAtos = null,
    DateOnly? DataArquivamentoConversao = null,
    char? IndicadorSituacaoInicio = null,
    char? IndicadorSituacaoEspecial = null,
    TipoMutacoesEcd Mutacoes = TipoMutacoesEcd.Dmpl,
    bool IncluirHistoricosPadronizados = true,
    bool ResponsavelPessoaJuridica = false,
    string? CodigoQualificacaoSocio = null,
    string? NumeroCertidaoCrc = null,
    DateOnly? ValidadeCertidaoCrc = null,
    bool MudancaPlanoContas = false,
    bool AssinarComoProcurador = false);

/// <summary>Resultado da conferência prévia (passo "pendências" do assistente).</summary>
public sealed record PreviaEcd(int Ano, DateOnly Inicio, DateOnly Fim, int NumeroOrdem, string? CodPlanRef, IReadOnlyList<SignatarioEcd> Signatarios,
                               IReadOnlyList<PendenciaEcd> Pendencias, int Lancamentos, int ContasNoPlano)
{
    public bool PodeGerar => !Pendencias.Any(p => p.Bloqueante);
    public IEnumerable<PendenciaEcd> Bloqueantes => Pendencias.Where(p => p.Bloqueante);
    public IEnumerable<PendenciaEcd> Avisos => Pendencias.Where(p => !p.Bloqueante);
}

/// <summary>Arquivo da ECD pronto para importar no PVA (assinado ou não).</summary>
/// <param name="HashSha1">Hash SHA-1 (hexadecimal maiúsculo) do arquivo sem as assinaturas — o hash da escrituração.</param>
public sealed record ArquivoEcd(string NomeArquivo, byte[] Conteudo, string HashSha1, int Linhas, IReadOnlyDictionary<string, int> Registros,
                                IReadOnlyList<AssinaturaSped> Assinaturas, IReadOnlyList<PendenciaEcd> Avisos);

/// <summary>
/// Gerador da ECD (Escrituração Contábil Digital — SPED Contábil), Livro G (Diário Geral), leiaute 9. Fluxo da tela:
/// <list type="number">
/// <item><see cref="ValidarAsync"/> — pendências bloqueantes e avisos, signatários e número do livro sugerido;</item>
/// <item><see cref="GerarAsync"/> — gera o arquivo (ISO-8859-1, CRLF) e, se receber certificados, já o assina (contador e
/// responsável), no formato que o PVA lê; o usuário só importa e valida no PVA;</item>
/// <item><see cref="Assinar"/> — acrescenta assinaturas a um arquivo já gerado (ex.: o responsável assina depois do contador).</item>
/// </list>
/// </summary>
public sealed class ServicoEcd(IConsultasEcd consultas, IRepositorioContabil contabil, ServicoResponsaveis responsaveis, Autorizador autorizador,
                               IAssinadorSped? assinador = null)
{
    private sealed record Preparo(DadosContabeisEcd Contabeis, DadosEcd Dados, CalculoEcd Calculo, List<PendenciaEcd> Pendencias);

    public async Task<Resultado<PreviaEcd>> ValidarAsync(long empresaId, int ano, OpcoesEcd opcoes, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<PreviaEcd>(usuarioId, empresaId, Permissao.SpedEcdGerar, ct) is { } negado) return negado;
        var preparo = await PrepararAsync(empresaId, ano, opcoes, ct);
        if (!preparo.Sucesso) return Resultado<PreviaEcd>.Falha(preparo.Erros);
        var (c, d, calc, pendencias) = preparo.Valor;
        return Resultado<PreviaEcd>.Ok(new PreviaEcd(ano, d.Parametros.Inicio, d.Parametros.Fim, d.Parametros.NumeroOrdem, d.Parametros.CodPlanRef,
            d.Signatarios, pendencias, d.Lancamentos.Count, calc.PlanoUsado.Count));
    }

    /// <summary>Gera a ECD. Com pendências bloqueantes, falha com a lista delas. Sem <paramref name="assinantes"/>, o arquivo sai sem assinatura.</summary>
    public async Task<Resultado<ArquivoEcd>> GerarAsync(long empresaId, int ano, OpcoesEcd opcoes, long usuarioId,
                                                       IReadOnlyList<CertificadoAssinante>? assinantes = null, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ArquivoEcd>(usuarioId, empresaId, Permissao.SpedEcdGerar, ct) is { } negado) return negado;
        var preparo = await PrepararAsync(empresaId, ano, opcoes, ct);
        if (!preparo.Sucesso) return Resultado<ArquivoEcd>.Falha(preparo.Erros);
        var (_, d, calc, pendencias) = preparo.Valor;
        if (pendencias.Any(p => p.Bloqueante))
            return Resultado<ArquivoEcd>.Falha(pendencias.Where(p => p.Bloqueante).Select(p => p.Mensagem));

        var escrituracao = GeradorEcd.Gerar(d, calc);
        var conteudo = escrituracao.Conteudo;
        IReadOnlyList<AssinaturaSped> assinaturas = [];
        if (assinantes is { Count: > 0 })
        {
            var assinado = AssinarConferindo(conteudo, assinantes, d.Signatarios);
            if (!assinado.Sucesso) return Resultado<ArquivoEcd>.Falha(assinado.Erros);
            conteudo = assinado.Valor;
            assinaturas = assinador!.LerAssinaturas(conteudo);
        }

        await consultas.RegistrarAsync(empresaId, ano, d.Parametros.Inicio, d.Parametros.Fim, d.Parametros.NumeroOrdem, d.Parametros.NaturezaLivro,
                                       escrituracao.HashSha1, escrituracao.QuantidadeLinhas, assinaturas.Count, usuarioId, ct);
        var cnpj = new string(d.Empresa.Cnpj.Where(char.IsAsciiLetterOrDigit).ToArray());
        return Resultado<ArquivoEcd>.Ok(new ArquivoEcd($"ECD_{cnpj}_{d.Parametros.Inicio:yyyyMMdd}_{d.Parametros.Fim:yyyyMMdd}_LivroG_{d.Parametros.NumeroOrdem}.txt",
            conteudo, escrituracao.HashSha1, escrituracao.QuantidadeLinhas, escrituracao.Registros, assinaturas,
            pendencias.Where(p => !p.Bloqueante).ToList()));
    }

    /// <summary>Acrescenta assinaturas a um arquivo da ECD já gerado (assinado em parte ou não).</summary>
    public Resultado<byte[]> Assinar(byte[] arquivo, IReadOnlyList<CertificadoAssinante> assinantes)
    {
        if (assinador is null) return Resultado<byte[]>.Falha("Assinatura digital não configurada.");
        if (assinantes.Count == 0) return Resultado<byte[]>.Falha("Escolha ao menos um certificado.");
        var signatarios = LerSignatariosDoArquivo(arquivo, assinador.TamanhoSemAssinaturas(arquivo));
        return AssinarConferindo(arquivo, assinantes, signatarios);
    }

    public IReadOnlyList<AssinaturaSped> LerAssinaturas(byte[] arquivo) => assinador?.LerAssinaturas(arquivo) ?? [];

    public async Task<Resultado<IReadOnlyList<EcdRegistrada>>> ListarGeradasAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<EcdRegistrada>>(usuarioId, empresaId, Permissao.SpedEcdGerar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<EcdRegistrada>>.Ok(await consultas.ListarAsync(empresaId, ct));
    }

    // ------------------------------------------------------------------------------------------------ preparo

    private async Task<Resultado<Preparo>> PrepararAsync(long empresaId, int ano, OpcoesEcd opcoes, CancellationToken ct)
    {
        var c = await consultas.CarregarAsync(empresaId, ano, ct);
        if (c is null) return Resultado<Preparo>.Falha($"Exercício {ano} não cadastrado para a empresa.");

        var numeroOrdem = opcoes.NumeroOrdem ?? c.NumeroOrdemSugerido;
        var indicadorInicio = opcoes.IndicadorSituacaoInicio
            ?? (c.Empresa.DataInicioAtividades == c.InicioExercicio && c.InicioExercicio != new DateOnly(c.InicioExercicio.Year, 1, 1) ? '1' : '0');
        var parametros = new ParametrosEcd(c.InicioExercicio, c.FimExercicio, numeroOrdem, opcoes.NaturezaLivro.Trim(), c.CodPlanRef, indicadorInicio,
            opcoes.IndicadorSituacaoEspecial, opcoes.DataArquivamentoAtos ?? c.Empresa.DataInicioAtividades, opcoes.DataArquivamentoConversao,
            c.FimExercicio, opcoes.NivelAglutinacao, opcoes.Mutacoes, opcoes.IncluirHistoricosPadronizados, opcoes.MudancaPlanoContas,
            c.ResultadoAnterior);
        var signatarios = await SignatariosAsync(empresaId, c.Empresa.Nome, c.Empresa.Cnpj, opcoes.ResponsavelPessoaJuridica, opcoes.CodigoQualificacaoSocio,
            opcoes.NumeroCertidaoCrc, opcoes.ValidadeCertidaoCrc, opcoes.AssinarComoProcurador, "ECD", ct);
        var dados = new DadosEcd(c.Empresa, parametros, c.Plano, c.CentrosCusto, c.Historicos, c.SaldosAbertura, c.Lancamentos, signatarios);
        var calc = new CalculoEcd(dados);
        var pendencias = ValidadorEcd.Validar(dados, calc).ToList();
        if (opcoes.AssinarComoProcurador) await AjustarPendenciasProcuradorAsync(empresaId, signatarios, pendencias, "ECD", ct);

        if (c.PeriodosAbertos.Count > 0)
            pendencias.Add(new(GravidadePendencia.Aviso, "PERIODOS_ABERTOS",
                $"Competências ainda abertas: {string.Join(", ", c.PeriodosAbertos.Select(p => p.ToString("MM/yyyy")))}. Feche os períodos antes de entregar a ECD."));

        // Conferência do cache de saldos (saldo_mensal, usado no balancete e nas demonstrações) com as partidas, mês a mês.
        var lacuna = false;
        for (var mes = new DateOnly(c.InicioExercicio.Year, c.InicioExercicio.Month, 1); mes <= c.FimExercicio; mes = mes.AddMonths(1))
            foreach (var i in await contabil.VerificarInvariantesAsync(empresaId, mes, ct))
                switch (i.Codigo)
                {
                    case "RASCUNHO_PENDENTE":
                        pendencias.Add(new(GravidadePendencia.Aviso, i.Codigo, $"{mes:MM/yyyy}: {i.Descricao} (rascunhos não vão à ECD)."));
                        break;
                    case "LACUNA_NUMERACAO":
                        if (!lacuna) pendencias.Add(new(GravidadePendencia.Aviso, i.Codigo, i.Descricao));
                        lacuna = true;
                        break;
                    case "SALDO_DIVERGENTE":
                        pendencias.Add(new(GravidadePendencia.Bloqueante, i.Codigo,
                            $"{mes:MM/yyyy}: saldos consolidados não batem com os lançamentos (I155 × I250) — {i.Descricao}. Recalcule os saldos."));
                        break;
                    default:
                        pendencias.Add(new(GravidadePendencia.Bloqueante, i.Codigo, $"{mes:MM/yyyy}: {i.Descricao}"));
                        break;
                }
        return Resultado<Preparo>.Ok(new Preparo(c, dados, calc, pendencias));
    }

    /// <summary>
    /// Pendências da assinatura como procurador: sem procuração vigente hoje a geração é bloqueada (no lugar de "sem responsável") e,
    /// com ela, um aviso de que a transmissão exige a procuração eletrônica cadastrada no e-CAC.
    /// </summary>
    public async Task AjustarPendenciasProcuradorAsync(long empresaId, IReadOnlyList<SignatarioEcd> signatarios, List<PendenciaEcd> pendencias, string escrituracao,
                                                       CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        if (signatarios.Any(s => s.CodigoQualificacao == QualificacaoAssinante.Procurador))
        {
            pendencias.Add(new(GravidadePendencia.Aviso, "PROCURACAO_ECAC",
                $"Assinatura como procurador: para transmitir a {escrituracao}, a procuração eletrônica precisa estar cadastrada no e-CAC (Procurações), com poderes para a {escrituracao}, e o certificado usado deve ser o do procurador."));
            return;
        }
        pendencias.RemoveAll(p => p.Codigo == "SEM_RESPONSAVEL");
        var ultimo = await responsaveis.UltimoProcuradorAsync(empresaId, ct);
        pendencias.Add(new(GravidadePendencia.Bloqueante, "PROCURADOR_NAO_VIGENTE", ultimo is null
            ? "Nenhum procurador cadastrado para a empresa (Cadastros › Contadores, seção Procuração eletrônica): marque o contador como procurador, com a vigência da procuração, ou assine como responsável."
            : ultimo.VigenteEm(hoje)
                ? $"A procuração de {ultimo.Nome} não abrange a {escrituracao} (abrangência: {string.Join(", ", ultimo.Abrangencia.Select(AbrangenciaProcuracao.Descricao))}): marque a {escrituracao} na procuração do contador ou assine como responsável."
                : $"A procuração de {ultimo.Nome} não está vigente em {hoje:dd/MM/yyyy} (vigência de {ultimo.VigenciaInicio:dd/MM/yyyy} " +
                  $"{(ultimo.VigenciaFim is { } f ? $"a {f:dd/MM/yyyy}" : "sem fim")}): renove a procuração no cadastro do contador ou assine como responsável."));
    }

    /// <summary>Signatários do livro (contador e responsável), compartilhado com a ECF (0930 usa os mesmos códigos de qualificação).</summary>
    public async Task<IReadOnlyList<SignatarioEcd>> SignatariosAsync(long empresaId, string nomeEmpresa, string cnpjEmpresa, bool responsavelPessoaJuridica,
                                                                      string? codigoQualificacaoSocio, string? numeroCertidaoCrc, DateOnly? validadeCertidaoCrc,
                                                                      bool assinarComoProcurador, string escrituracao, CancellationToken ct)
    {
        var lista = new List<SignatarioEcd>();
        if (await responsaveis.SignatarioEcdEcfAsync(empresaId, ct) is { } contador)
            lista.Add(new SignatarioEcd(contador.Nome, contador.Cpf, QualificacaoAssinante.Descricao(QualificacaoAssinante.Contador),
                QualificacaoAssinante.Contador, false, contador.Crc, contador.UfCrc, contador.Email, contador.Telefone,
                numeroCertidaoCrc?.Trim().ToUpperInvariant(), validadeCertidaoCrc));
        if (assinarComoProcurador)
        {
            // Procurador com procuração vigente hoje assina no lugar do responsável (qualificação 309 – Procurador).
            if (await responsaveis.ProcuradorVigenteAsync(empresaId, DateOnly.FromDateTime(DateTime.Today), escrituracao, ct) is { } procurador)
                lista.Add(new SignatarioEcd(procurador.Nome, procurador.Cpf, QualificacaoAssinante.Descricao(QualificacaoAssinante.Procurador),
                    QualificacaoAssinante.Procurador, true, Email: procurador.Email, Telefone: procurador.Telefone));
        }
        else if (responsavelPessoaJuridica)
            lista.Add(new SignatarioEcd(nomeEmpresa, cnpjEmpresa, QualificacaoAssinante.Descricao(QualificacaoAssinante.PessoaJuridica),
                QualificacaoAssinante.PessoaJuridica, true));
        else if (await responsaveis.SocioResponsavelAsync(empresaId, ct) is { } socio && socio.TipoPessoa == 'F')
        {
            var codigo = codigoQualificacaoSocio ?? QualificacaoAssinante.DeQualificacaoSocio(socio.QualificacaoCodigo, socio.Qualificacao);
            lista.Add(new SignatarioEcd(socio.Nome, socio.CpfCnpj ?? "", QualificacaoAssinante.Descricao(codigo), codigo, true,
                Email: socio.Email, Telefone: socio.Telefone));
        }
        return lista;
    }

    // ------------------------------------------------------------------------------------------------ assinatura

    /// <summary>Confere cada certificado com o J930 (código de qualificação e CPF/CNPJ) e assina.</summary>
    public Resultado<byte[]> AssinarConferindo(byte[] arquivo, IReadOnlyList<CertificadoAssinante> assinantes, IReadOnlyList<SignatarioEcd> signatarios)
    {
        if (assinador is null) return Resultado<byte[]>.Falha("Assinatura digital não configurada.");
        var erros = new List<string>();
        var agora = DateTime.Now;
        foreach (var a in assinantes)
        {
            var cert = a.Certificado;
            if (!cert.HasPrivateKey) { erros.Add($"O certificado de {cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)} não tem a chave privada."); continue; }
            if (cert.NotAfter < agora || cert.NotBefore > agora) { erros.Add($"Certificado de {cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)} fora da validade ({cert.NotAfter:dd/MM/yyyy})."); continue; }
            var candidatos = signatarios.Where(s => s.CodigoQualificacao == a.CodigoQualificacao).ToList();
            if (candidatos.Count == 0) { erros.Add($"Nenhum signatário do J930 tem a qualificação {a.CodigoQualificacao}."); continue; }
            var doc = DocumentoIcpBrasil(cert);
            if (doc is not null && candidatos.All(s => new string(s.CpfCnpj.Where(char.IsAsciiLetterOrDigit).ToArray()) != doc))
                erros.Add($"O certificado ({doc}) não é de nenhum signatário do J930 com a qualificação {a.CodigoQualificacao} " +
                          $"({string.Join(", ", candidatos.Select(s => s.Nome))}).");
        }
        if (erros.Count > 0) return Resultado<byte[]>.Falha(erros);
        try
        {
            return Resultado<byte[]>.Ok(assinador.Assinar(arquivo, assinantes));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            return Resultado<byte[]>.Falha($"Não foi possível assinar: {ex.Message}");
        }
    }

    /// <summary>CPF ou CNPJ do titular de um certificado ICP-Brasil: pelo nome ("NOME:CPF"/"RAZÃO:CNPJ") ou pelas extensões
    /// otherName da ICP-Brasil (2.16.76.1.3.1 = dados da pessoa física, CPF nas posições 9 a 19; 2.16.76.1.3.3 = CNPJ).</summary>
    public static string? DocumentoIcpBrasil(System.Security.Cryptography.X509Certificates.X509Certificate2 cert)
    {
        var nome = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
        var m = Regex.Match(nome ?? "", @":(?<doc>\d{11}|\d{14})$");
        if (m.Success) return m.Groups["doc"].Value;
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17") continue;
            var dados = ext.RawData;
            var texto = System.Text.Encoding.Latin1.GetString(dados);
            var cnpj = Regex.Match(texto, @"\x06\x05\x60\x4C\x01\x03\x03.{2,8}?(?<d>\d{14})", RegexOptions.Singleline);
            if (cnpj.Success) return cnpj.Groups["d"].Value;
            var pf = Regex.Match(texto, @"\x06\x05\x60\x4C\x01\x03\x01.{2,8}?\d{8}(?<d>\d{11})", RegexOptions.Singleline);
            if (pf.Success) return pf.Groups["d"].Value;
        }
        return null;
    }

    /// <summary>Signatários (J930) lidos do próprio arquivo, para assinar um arquivo gerado antes.</summary>
    public static IReadOnlyList<SignatarioEcd> LerSignatariosDoArquivo(byte[] arquivo, int tamanho)
    {
        var texto = FormatoSped.Latin1.GetString(arquivo, 0, tamanho);
        var res = new List<SignatarioEcd>();
        foreach (var linha in texto.Split("\r\n"))
        {
            if (!linha.StartsWith("|J930|", StringComparison.Ordinal)) continue;
            var f = linha.Split('|');
            if (f.Length < 13) continue;   // |J930|NOME|CPF_CNPJ|QUALIF|COD|CRC|EMAIL|FONE|UF_CRC|NUM_SEQ|DT_CRC|RESP|
            res.Add(new SignatarioEcd(f[2], f[3], f[4], f[5], f[12] == "S", f[6], f[9], f[7], f[8]));
        }
        return res;
    }
}
