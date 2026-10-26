using System.IO.Compression;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Fiscal.Documentos;

/// <summary>Dados da empresa necessários para identificar entrada/saída (CNPJ da matriz e dos estabelecimentos).</summary>
public sealed record EmpresaFiscal(long Id, string Cnpj, IReadOnlyList<string> CnpjsEstabelecimentos, string RazaoSocial, DateOnly InicioAtividade)
{
    public IEnumerable<string> Cnpjs => CnpjsEstabelecimentos.Append(Cnpj);
}

/// <summary>Documento gravado (sem os itens, que vêm por <see cref="IRepositorioDocumentos.ListarItensAsync"/>).</summary>
public sealed record DocumentoFiscal(
    long Id, ModeloDocumento Modelo, string Chave, TipoOperacao Operacao, string Numero, string Serie, DateOnly Emissao, DateOnly DataLivro,
    string? NaturezaOperacao, SituacaoDocumento Situacao, ParteDocumento Emitente, ParteDocumento? Destinatario, long? ParticipanteId,
    string? ParticipanteCodigo, TotaisDocumento Totais, long? LancamentoId, bool Contabilizado,
    string Origem = "XML", long? CentroCustoId = null, string? CentroCustoCodigo = null,
    string? Observacoes = null, string? ContaDebito = null, string? ContaCredito = null)
{
    public bool Digitado => Origem == "DIGITADO";

    /// <summary>Nome da outra parte: cliente (saída) ou fornecedor (entrada).</summary>
    public string? NomeParticipante => Operacao == TipoOperacao.Entrada ? Emitente.Nome : Destinatario?.Nome;
    public string? DocumentoParticipante => Operacao == TipoOperacao.Entrada ? Emitente.Documento : Destinatario?.Documento;
}

public sealed record FiltroDocumentos(
    DateOnly? Inicio = null, DateOnly? Fim = null, TipoOperacao? Operacao = null, ModeloDocumento? Modelo = null,
    SituacaoDocumento? Situacao = null, string? Texto = null, string? Cfop = null, bool? Contabilizado = null, int Limite = 5000);

/// <summary>Resultado de gravar um documento: <see cref="Duplicado"/> quando a chave já existia na empresa.</summary>
public sealed record GravacaoDocumento(bool Duplicado, long DocumentoId, bool ParticipanteCriado, string? ParticipanteNome);

public sealed record CancelamentoRegistrado(bool Encontrado, bool JaCancelado, bool Contabilizado, long DocumentoId, string? Numero);

/// <summary>Transação de importação em lote. <see cref="ConfirmarAsync"/> grava o que foi aceito; sem confirmar, o descarte desfaz tudo.</summary>
public interface ILoteGravacao : IAsyncDisposable
{
    /// <summary>Grava a nota; se falhar, desfaz só esta nota (a exceção é propagada) e o lote continua utilizável.</summary>
    Task<GravacaoDocumento> GravarAsync(DocumentoLido documento, Identificacao identificacao, CancellationToken ct);
    Task ConfirmarAsync(CancellationToken ct);
}

public interface IRepositorioDocumentos
{
    Task<EmpresaFiscal?> ObterEmpresaAsync(long empresaId, CancellationToken ct);

    /// <summary>Grava documento e itens numa transação; vincula (ou cria) o participante. Chave já importada: <see cref="GravacaoDocumento.Duplicado"/>.</summary>
    Task<GravacaoDocumento> GravarAsync(long empresaId, DocumentoLido documento, Identificacao identificacao, long usuarioId, CancellationToken ct);

    /// <summary>Abre uma transação para gravar muitas notas de uma vez (importação em lote); cada nota é isolada por savepoint.</summary>
    Task<ILoteGravacao> IniciarLoteAsync(long empresaId, long usuarioId, CancellationToken ct);

    /// <summary>Marca o documento da chave como cancelado (evento de cancelamento importado).</summary>
    Task<CancelamentoRegistrado> CancelarPorChaveAsync(long empresaId, string chave, CancellationToken ct);

    Task<IReadOnlyList<DocumentoFiscal>> ListarAsync(long empresaId, FiltroDocumentos filtro, CancellationToken ct);
    Task<IReadOnlyList<ItemFiscal>> ListarItensDoDocumentoAsync(long documentoId, CancellationToken ct);

    /// <summary>Itens de todos os documentos da empresa no período (data do livro), com os dados do documento.</summary>
    Task<IReadOnlyList<LinhaItemFiscal>> ListarItensAsync(long empresaId, DateOnly inicio, DateOnly fim, TipoOperacao? operacao, CancellationToken ct);

    /// <summary>Exclui documentos que não estão contabilizados; devolve quantos foram excluídos.</summary>
    Task<int> ExcluirAsync(long empresaId, IReadOnlyCollection<long> ids, CancellationToken ct);

    /// <summary>XML original (descompactado) do documento.</summary>
    Task<byte[]?> ObterXmlAsync(long empresaId, long documentoId, CancellationToken ct);
}

public enum SituacaoImportacao { Importado, Duplicado, Cancelado, Ignorado, Erro, OutraEmpresa }

public sealed record ArquivoXml(string Nome, byte[] Conteudo);

/// <summary>Resultado de um arquivo. <paramref name="ParticipanteNovo"/>: CPF/CNPJ do fornecedor/cliente cadastrado agora a partir do XML.</summary>
public sealed record ResultadoArquivo(string Arquivo, SituacaoImportacao Situacao, string Mensagem, string? Chave = null, string? Numero = null,
                                      TipoOperacao? Operacao = null, decimal Valor = 0, DateOnly? DataLivro = null, string? ParticipanteNovo = null);

/// <summary>Andamento da importação em lote (<see cref="Total"/> = 0 quando a quantidade não é conhecida de antemão).</summary>
public sealed record ProgressoImportacao(int Processados, int Total, string Arquivo);

/// <summary>
/// Resultado da importação. <see cref="Interrompida"/> = o usuário cancelou: o que já tinha sido gravado foi mantido e
/// <see cref="NaoProcessados"/> arquivos ficaram de fora.
/// </summary>
public sealed record ResultadoImportacao(IReadOnlyList<ResultadoArquivo> Arquivos, bool Interrompida = false, int NaoProcessados = 0)
{
    public int Importados => Arquivos.Count(a => a.Situacao == SituacaoImportacao.Importado);
    public int Duplicados => Arquivos.Count(a => a.Situacao == SituacaoImportacao.Duplicado);
    public int Cancelamentos => Arquivos.Count(a => a.Situacao == SituacaoImportacao.Cancelado);
    public int Ignorados => Arquivos.Count(a => a.Situacao == SituacaoImportacao.Ignorado);
    public int OutrasEmpresas => Arquivos.Count(a => a.Situacao == SituacaoImportacao.OutraEmpresa);
    public int Erros => Arquivos.Count(a => a.Situacao == SituacaoImportacao.Erro);

    public string Resumo => $"{Arquivos.Count} arquivo(s): {Importados} importado(s), {Duplicados} já importado(s), {Cancelamentos} cancelamento(s), " +
                            $"{Ignorados} ignorado(s), {OutrasEmpresas} de outra empresa, {Erros} com erro." +
                            (Interrompida ? $" Importação cancelada pelo usuário: {NaoProcessados} arquivo(s) não processado(s)." : "");

    /// <summary>Relatório do resumo em CSV (separador ";"): uma linha por arquivo com situação e motivo, para conferência.</summary>
    public string ParaCsv()
    {
        static string C(string? t) => "\"" + (t ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Arquivo;Resultado;Numero;Operacao;Valor;Chave;Mensagem");
        foreach (var a in Arquivos)
            sb.AppendLine(string.Join(';', C(a.Arquivo), C(RotuloSituacao(a.Situacao)), C(a.Numero), C(a.Operacao?.Rotulo()),
                a.Valor == 0 ? "" : a.Valor.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), C(a.Chave), C(a.Mensagem)));
        sb.AppendLine();
        sb.AppendLine(C(Resumo));
        return sb.ToString();
    }

    public static string RotuloSituacao(SituacaoImportacao s) => s switch
    {
        SituacaoImportacao.Importado => "Importado", SituacaoImportacao.Duplicado => "Já importado", SituacaoImportacao.Cancelado => "Cancelamento",
        SituacaoImportacao.Ignorado => "Ignorado", SituacaoImportacao.OutraEmpresa => "Outra empresa", _ => "Erro",
    };
}

/// <summary>
/// Documentos fiscais eletrônicos: importação de XML (NF-e/NFC-e e NFS-e nacional), consulta e livros. Toda operação verifica
/// a permissão do usuário na empresa antes de qualquer outra regra.
/// </summary>
public sealed class ServicoDocumentosFiscais(IRepositorioDocumentos repositorio, Autorizador autorizador)
{
    // ------------------------------------------------------------------ importação

    /// <summary>Notas gravadas por transação: o lote é confirmado a cada tanto (e antes de cancelamentos) para não segurar o banco por muito tempo.</summary>
    internal const int TamanhoLote = 200;

    /// <summary>
    /// Importa os arquivos em lotes: várias notas por transação, cada uma isolada por savepoint, de modo que um arquivo com erro não impede
    /// os demais. Arquivos .zip (inclusive com subpastas) têm os XMLs de dentro importados. Chaves já importadas são ignoradas (deduplicação
    /// por empresa). Se <paramref name="ct"/> for cancelado, o que já foi aceito é mantido e o resultado vem marcado como interrompido.
    /// </summary>
    public async Task<Resultado<ResultadoImportacao>> ImportarAsync(long empresaId, IEnumerable<ArquivoXml> arquivos, long usuarioId,
                                                                   IProgress<ProgressoImportacao>? progresso = null, CancellationToken ct = default, int total = 0)
    {
        if (await autorizador.NegarAsync<ResultadoImportacao>(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, CancellationToken.None) is { } negado) return negado;
        var empresa = await repositorio.ObterEmpresaAsync(empresaId, CancellationToken.None);   // cancelar antes de começar não é erro: resulta em importação interrompida
        if (empresa is null) return Resultado<ResultadoImportacao>.Falha("Empresa não encontrada.");

        var resultados = new List<ResultadoArquivo>();
        var lote = new LoteAberto(repositorio, empresaId, usuarioId);
        var interrompida = false;
        try
        {
            foreach (var arquivo in Expandir(arquivos))
            {
                if (ct.IsCancellationRequested) { interrompida = true; break; }
                try { resultados.Add(await ImportarUmAsync(empresa, arquivo, lote, ct)); }
                catch (OperationCanceledException) { interrompida = true; break; }
                if (lote.Gravadas >= TamanhoLote) await lote.FecharAsync();
                progresso?.Report(new ProgressoImportacao(resultados.Count, Math.Max(total, resultados.Count), arquivo.Nome));
            }
        }
        finally { await lote.FecharAsync(); }
        return Resultado<ResultadoImportacao>.Ok(new ResultadoImportacao(resultados, interrompida, interrompida ? Math.Max(0, total - resultados.Count) : 0));
    }

    /// <summary>Importa os .xml (e .zip) de uma pasta, opcionalmente com as subpastas (em qualquer profundidade).</summary>
    public Task<Resultado<ResultadoImportacao>> ImportarPastaAsync(long empresaId, string pasta, bool subpastas, long usuarioId,
                                                                  IProgress<ProgressoImportacao>? progresso = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(pasta)) return Task.FromResult(Resultado<ResultadoImportacao>.Falha("Pasta não encontrada."));
        return ImportarArquivosAsync(empresaId, ListarArquivosDaPasta(pasta, subpastas), usuarioId, progresso, ct);
    }

    public Task<Resultado<ResultadoImportacao>> ImportarArquivosAsync(long empresaId, IEnumerable<string> caminhos, long usuarioId,
                                                                     IProgress<ProgressoImportacao>? progresso = null, CancellationToken ct = default)
    {
        var lista = caminhos.ToList();
        return ImportarAsync(empresaId, LerArquivos(lista), usuarioId, progresso, ct, ContarArquivos(lista));
    }

    /// <summary>Caminhos dos .xml e .zip de uma pasta (com as subpastas, se pedido), ignorando pastas sem acesso.</summary>
    public static List<string> ListarArquivosDaPasta(string pasta, bool subpastas)
    {
        var opcoes = new EnumerationOptions { RecurseSubdirectories = subpastas, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        return Directory.EnumerateFiles(pasta, "*.*", opcoes)
            .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Quantidade de XMLs a processar (os .zip contam pelos .xml de dentro), para a barra de progresso.</summary>
    public static int ContarArquivos(IEnumerable<string> caminhos)
    {
        var n = 0;
        foreach (var c in caminhos)
        {
            if (!c.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { n++; continue; }
            try
            {
                using var zip = ZipFile.OpenRead(c);
                var xmls = zip.Entries.Count(e => EhXmlDoZip(e));
                n += Math.Max(1, xmls);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { n++; }
        }
        return n;
    }

    private static bool EhXmlDoZip(ZipArchiveEntry e) =>
        e.Name.Length > 0 && !e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal) && e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ArquivoXml> LerArquivos(IEnumerable<string> caminhos)
    {
        foreach (var c in caminhos)
        {
            byte[]? conteudo = null;
            try { conteudo = File.ReadAllBytes(c); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            yield return new ArquivoXml(Path.GetFileName(c), conteudo ?? []);
        }
    }

    private static IEnumerable<ArquivoXml> Expandir(IEnumerable<ArquivoXml> arquivos)
    {
        foreach (var a in arquivos)
        {
            if (!a.Nome.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { yield return a; continue; }
            foreach (var interno in ExtrairZip(a, 0)) yield return interno;
        }
    }

    /// <summary>XMLs de um .zip em qualquer subpasta (e de .zip dentro de .zip, até 3 níveis), lidos um a um.</summary>
    private static IEnumerable<ArquivoXml> ExtrairZip(ArquivoXml a, int nivel)
    {
        ZipArchive? zip = null;
        try { zip = new ZipArchive(new MemoryStream(a.Conteudo), ZipArchiveMode.Read); }
        catch (InvalidDataException) { }
        if (zip is null) { yield return new ArquivoXml(a.Nome, []); yield break;   /* vira erro "arquivo vazio ou ilegível" */ }
        using (zip)
        {
            foreach (var e in zip.Entries)
            {
                if (e.Name.Length == 0 || e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)) continue;
                var zipInterno = e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                if (!zipInterno && !EhXmlDoZip(e)) continue;
                using var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                var nome = $"{a.Nome}/{e.FullName}";
                if (!zipInterno) yield return new ArquivoXml(nome, ms.ToArray());
                else if (nivel < 3) foreach (var x in ExtrairZip(new ArquivoXml(nome, ms.ToArray()), nivel + 1)) yield return x;
            }
        }
    }

    /// <summary>Transação de importação aberta sob demanda e confirmada a cada lote, antes de cancelamentos e ao final.</summary>
    private sealed class LoteAberto(IRepositorioDocumentos repositorio, long empresaId, long usuarioId)
    {
        private ILoteGravacao? _lote;
        public int Gravadas { get; private set; }

        public async Task<GravacaoDocumento> GravarAsync(DocumentoLido doc, Identificacao ident, CancellationToken ct)
        {
            _lote ??= await repositorio.IniciarLoteAsync(empresaId, usuarioId, ct);
            var g = await _lote.GravarAsync(doc, ident, ct);
            Gravadas++;
            return g;
        }

        public async Task FecharAsync()
        {
            if (_lote is null) return;
            var l = _lote;
            (_lote, Gravadas) = (null, 0);
            await using (l) await l.ConfirmarAsync(CancellationToken.None);
        }
    }

    private async Task<ResultadoArquivo> ImportarUmAsync(EmpresaFiscal empresa, ArquivoXml arquivo, LoteAberto lote, CancellationToken ct)
    {
        if (arquivo.Conteudo.Length == 0) return new(arquivo.Nome, SituacaoImportacao.Erro, "Arquivo vazio ou ilegível.");
        var leitura = LeitorXmlFiscal.Ler(arquivo.Conteudo);
        switch (leitura)
        {
            case LeituraInvalida inv:
                return new(arquivo.Nome, inv.Motivo.StartsWith("Evento", StringComparison.Ordinal) ? SituacaoImportacao.Ignorado : SituacaoImportacao.Erro, inv.Motivo);

            case LeituraCancelamento canc:
            {
                await lote.FecharAsync();   // a nota cancelada pode estar no lote ainda não confirmado
                var r = await repositorio.CancelarPorChaveAsync(empresa.Id, canc.Chave, ct);
                if (!r.Encontrado) return new(arquivo.Nome, SituacaoImportacao.Ignorado, "Cancelamento de documento que não está importado.", canc.Chave);
                if (r.JaCancelado) return new(arquivo.Nome, SituacaoImportacao.Duplicado, "Documento já estava cancelado.", canc.Chave, r.Numero);
                return new(arquivo.Nome, SituacaoImportacao.Cancelado,
                    r.Contabilizado ? $"Documento {r.Numero} cancelado. Atenção: ele está contabilizado; desfaça a contabilização." : $"Documento {r.Numero} cancelado.",
                    canc.Chave, r.Numero);
            }

            case LeituraDocumento lido:
            {
                var doc = lido.Documento;
                var ident = IdentificadorOperacao.Identificar(doc, empresa.Cnpjs);
                if (!ident.Sucesso) return new(arquivo.Nome, SituacaoImportacao.OutraEmpresa, string.Join(" ", ident.Erros), doc.Chave, doc.Numero);
                try
                {
                    var g = await lote.GravarAsync(doc, ident.Valor, ct);
                    if (g.Duplicado) return new(arquivo.Nome, SituacaoImportacao.Duplicado, "Documento já importado (mesma chave de acesso).", doc.Chave, doc.Numero, ident.Valor.Operacao, doc.Totais.Total, doc.DataLivro);
                    // conferência do XML (chave e itens): não impede a importação, mas avisa o que não confere
                    var avisos = ConferenciaXml.Conferir(doc);
                    var aviso = avisos.Count == 0 ? "" : " Confira: " + string.Join(" ", avisos.Take(6)) + (avisos.Count > 6 ? $" (e mais {avisos.Count - 6} aviso(s))" : "");
                    var part = g.ParticipanteCriado ? $" Participante cadastrado: {g.ParticipanteNome}." : "";
                    return new(arquivo.Nome, SituacaoImportacao.Importado,
                        $"{doc.Modelo.Rotulo()} {doc.Numero} ({ident.Valor.Operacao.Rotulo().ToLowerInvariant()}) importada.{part}{aviso}", doc.Chave, doc.Numero, ident.Valor.Operacao, doc.Totais.Total, doc.DataLivro,
                        g.ParticipanteCriado ? ident.Valor.Contraparte?.Documento : null);
                }
                catch (ViolacaoRegraException ex)
                {
                    return new(arquivo.Nome, SituacaoImportacao.Erro, ex.Message, doc.Chave, doc.Numero);
                }
                catch (Npgsql.PostgresException ex)
                {
                    return new(arquivo.Nome, SituacaoImportacao.Erro, $"Erro ao gravar no banco: {ex.MessageText}", doc.Chave, doc.Numero);
                }
            }
            default:
                return new(arquivo.Nome, SituacaoImportacao.Erro, "Arquivo não reconhecido.");
        }
    }

    // ------------------------------------------------------------------ consultas

    public async Task<Resultado<IReadOnlyList<DocumentoFiscal>>> ConsultarAsync(long empresaId, FiltroDocumentos filtro, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<DocumentoFiscal>>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<DocumentoFiscal>>.Ok(await repositorio.ListarAsync(empresaId, filtro, ct));
    }

    public async Task<Resultado<IReadOnlyList<ItemFiscal>>> ItensAsync(long empresaId, long documentoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<ItemFiscal>>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<ItemFiscal>>.Ok(await repositorio.ListarItensDoDocumentoAsync(documentoId, ct));
    }

    public async Task<Resultado<byte[]>> ObterXmlAsync(long empresaId, long documentoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<byte[]>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return await repositorio.ObterXmlAsync(empresaId, documentoId, ct) is { } xml ? Resultado<byte[]>.Ok(xml) : Resultado<byte[]>.Falha("Documento não encontrado.");
    }

    /// <summary>Linhas do Livro de Entradas (<paramref name="operacao"/> = Entrada) ou de Saídas, em ordem de data e número.</summary>
    public async Task<Resultado<IReadOnlyList<LinhaLivro>>> LivroAsync(long empresaId, TipoOperacao operacao, DateOnly inicio, DateOnly fim, long usuarioId,
                                                                      CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaLivro>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<IReadOnlyList<LinhaLivro>>.Falha("Data final anterior à inicial.");
        var itens = await repositorio.ListarItensAsync(empresaId, inicio, fim, operacao, ct);
        return Resultado<IReadOnlyList<LinhaLivro>>.Ok(LivrosFiscais.Montar(itens, operacao));
    }

    /// <summary>Resumo por CFOP (entradas e/ou saídas) do período.</summary>
    public async Task<Resultado<IReadOnlyList<ResumoCfop>>> ResumoPorCfopAsync(long empresaId, TipoOperacao? operacao, DateOnly inicio, DateOnly fim,
                                                                              long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<ResumoCfop>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) is { } negado) return negado;
        if (fim < inicio) return Resultado<IReadOnlyList<ResumoCfop>>.Falha("Data final anterior à inicial.");
        var itens = await repositorio.ListarItensAsync(empresaId, inicio, fim, operacao, ct);
        return Resultado<IReadOnlyList<ResumoCfop>>.Ok(LivrosFiscais.ResumirPorCfop(itens));
    }

    public async Task<Resultado<int>> ExcluirAsync(long empresaId, IReadOnlyCollection<long> ids, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return negado;
        if (ids.Count == 0) return Resultado<int>.Falha("Selecione ao menos um documento.");
        int n;
        try { n = await repositorio.ExcluirAsync(empresaId, ids, ct); }
        catch (ViolacaoRegraException ex) { return Resultado<int>.Falha(ex.Message); }
        return n == 0 ? Resultado<int>.Falha("Nenhum documento excluído: documentos contabilizados precisam ter a contabilização desfeita antes.")
                      : Resultado<int>.Ok(n);
    }
}
