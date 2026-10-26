namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>
/// Ponto de extensão do casco: cada executável da suíte (Contábil, Fiscal, Folha) descreve a si mesmo com uma
/// implementação desta interface. O casco cuida de banco, login, empresas, usuários, perfis, certificados, backup,
/// configurações e ajuda; o módulo fornece só o que é dele: nome, menus, atalhos e as telas.
/// <para>
/// Como um módulo se registra: implementa <see cref="IModuloDesktop"/>, devolve seus menus (cada item aponta para uma
/// chave de tela) e, em <see cref="RegistrarTelas"/>, declara as telas (instância do ViewModel + chave + título +
/// permissão + carga). O XAML do módulo associa cada ViewModel à sua View (DataTemplate) em App.axaml.
/// </para>
/// </summary>
public interface IModuloDesktop
{
    /// <summary>Nome completo do módulo ("Vorticis Contábil"): título da janela, tela Sobre e mensagens.</summary>
    string Nome { get; }

    /// <summary>Indicador deste módulo no cadastro da empresa (core.empresa): a seleção do topo lista só as empresas ativas nele.</summary>
    Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa { get; }

    /// <summary>Nome curto, mostrado na tela inicial ("Contábil").</summary>
    string NomeCurto { get; }

    /// <summary>Descrição de uma linha, mostrada no login e na tela Sobre ("Escrituração Fiscal Contábil").</summary>
    string Subtitulo { get; }

    /// <summary>Nome do executável sem extensão ("VorticisContabil"); também nomeia o ícone (Assets/VorticisContabil.ico).</summary>
    string NomeExecutavel { get; }

    /// <summary>
    /// Cor de destaque do módulo (#RRGGBB), para o usuário não confundir qual sistema está usando: faixa sob os menus,
    /// nome do módulo no login e na tela inicial, cabeçalho das janelas das funções. Contábil verde, Fiscal laranja,
    /// Folha roxo (os ícones de instalador/icone/gerar_icone.py usam as mesmas cores).
    /// </summary>
    string CorDestaque { get; }

    /// <summary>Cor da barra de menus (#RRGGBB): um tom escuro da família da cor de destaque.</summary>
    string CorBarra { get; }

    /// <summary>
    /// Itens do módulo no menu Cadastros, depois dos cadastros comuns (Empresas, Contadores, Clientes, Fornecedores).
    /// Vazio se o módulo não tem cadastros próprios.
    /// </summary>
    IReadOnlyList<ItemMenu> ItensCadastros { get; }

    /// <summary>Mostra Clientes e Fornecedores no menu Cadastros (a Folha não usa esses cadastros).</summary>
    bool UsaClientesFornecedores => true;

    /// <summary>Mostra o calendário de obrigações (menu e "Próximas obrigações" da tela inicial); o ERP não usa.</summary>
    bool UsaCalendarioObrigacoes => true;

    /// <summary>Nome do cadastro de empresas no menu Cadastros (o Condomínio chama de "Condomínios"; os dados são os mesmos).</summary>
    string RotuloEmpresas => "Empresas";

    /// <summary>
    /// O módulo administra a suíte: usuários e controle de acesso, conexão, backup, restauração e limpeza do banco (o Escritório).
    /// Instalado o Escritório, os demais módulos deixam essas funções para ele.
    /// </summary>
    bool GerenciaSistema => false;

    /// <summary>Rótulo da escolha da empresa ativa na barra superior (o Escritório escolhe o escritório; o Condomínio, o condomínio).</summary>
    string RotuloEmpresaAtiva => "Empresa";

    /// <summary>Mostra a aba Síndico no cadastro de empresas (só o Condomínio: o síndico pode assinar os livros contábeis e fiscais).</summary>
    bool UsaSindico => false;

    /// <summary>
    /// Mostra em Cadastros os cadastros compartilhados da suíte (Contadores, Clientes, Fornecedores). O Escritório não os usa: tem
    /// cadastros próprios, que não aparecem nos demais módulos; ali Cadastros mostra só Empresas (o próprio escritório).
    /// </summary>
    bool UsaCadastrosComuns => true;

    /// <summary>
    /// Recursos ligados nos parâmetros da empresa ativa (ex.: "gerencial" no Contábil): os itens de menu marcados com
    /// <see cref="ItemMenu.SoCom"/> só aparecem quando o recurso está na lista.
    /// </summary>
    Task<IReadOnlySet<string>> RecursosAtivosAsync(ViewModels.MainViewModelBase main) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    /// <summary>
    /// Chamado ao conectar ao banco: o módulo compõe os seus serviços, que assim registram na Contabilização em lote o processador das suas
    /// pendências (Fiscal, Folha...), mesmo que nenhuma tela do módulo tenha sido aberta ainda.
    /// </summary>
    void PrepararServicos(Vorticis.Infra.VorticisApp app) { }

    /// <summary>
    /// Rótulo do período de trabalho mostrado ao lado do seletor de empresas ("Competência"), com a situação (aberto/fechado);
    /// null se o módulo não trabalha por período. A situação vem de <see cref="ViewModels.MainViewModelBase.ConsultarPeriodoAsync"/>.
    /// </summary>
    string? RotuloPeriodo => null;

    /// <summary>Menus do módulo, entre Cadastros e Configurações (que, como Ajuda, pertencem ao casco).</summary>
    IReadOnlyList<ItemMenu> Menus { get; }

    /// <summary>Atalhos de teclado do módulo (listados na tela Sobre; os que têm chave abrem a tela).</summary>
    IReadOnlyList<AtalhoTela> Atalhos { get; }

    /// <summary>Módulos marcados ao cadastrar uma empresa por este programa (o Vorticis Logística marca todos os que reúne).</summary>
    Vorticis.Application.Cadastros.ModulosEmpresa ModulosAoCadastrar => ModuloEmpresa;

    /// <summary>Áreas do manual mostradas sem marcar "todos os módulos" (o programa que junta módulos mostra as de todos eles).</summary>
    IReadOnlyList<string> AreasDoManual => [NomeCurto];

    /// <summary>Declara as telas do módulo; chamado uma vez na criação da janela principal.</summary>
    void RegistrarTelas(CatalogoTelas catalogo);
}
