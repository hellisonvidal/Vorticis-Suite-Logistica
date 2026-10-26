# Primeiros passos

## Instalação e módulos

**Para que serve:** instalar o Vorticis Suite Logística no Windows 10 ou 11 (64 bits).

1. Execute o arquivo `Setup-VorticisSuiteLogistica-<versão>.exe`. Não precisa de senha de administrador do Windows.
2. Ao final, o instalador prepara o banco de dados sozinho. Abra o programa pelo atalho **Vorticis Suite Logística**.

Todos os módulos (Frota, Gerencial, Contábil, Fiscal e Folha) ficam no mesmo programa, com o mesmo banco, o mesmo login, as mesmas empresas e as mesmas configurações.

**Dicas:** para atualizar, basta rodar o instalador novo: os dados são preservados. Desinstalar remove o programa, mas **não** apaga os dados (pasta `%LOCALAPPDATA%\Vorticis`). Há também uma versão portátil (descompacte e execute).

## Vários módulos numa janela

**Para que serve:** trabalhar com todos os módulos (Frota, Gerencial, Contábil, Fiscal e Folha) numa única janela, como se fosse um só programa.
**Onde fica:** atalho **Vorticis Suite Logística** no menu Iniciar e na área de trabalho. É o programa aberto ao final da instalação.

Veja também o capítulo [Vorticis Suite Logística](13-logistica.md).

1. Entre uma vez com o seu usuário: o login vale para todos os módulos.
2. Troque de módulo pela navegação do layout escolhido ou pelas teclas **Ctrl+1**, **Ctrl+2**…. Os módulos aparecem em ordem alfabética em todos os layouts (Contábil, Fiscal, Folha, Frota, Gerencial), e as teclas seguem essa ordem. A empresa escolhida continua a mesma quando também está liberada no outro módulo. As funções abertas de cada módulo ficam guardadas: ao voltar, elas reaparecem.
3. Para mudar o visual, use o botão **⚙ Layout**, que aparece em todos os layouts (na barra de abas, na barra lateral, embaixo da árvore e no topo do painel). Também dá para usar *Configurações › Layout da Suite* ou *Exibir › Layout da Suite*. A escolha fica gravada neste computador.
   - **A – Abas no topo:** uma aba por módulo acima do menu.
   - **B – Barra lateral (padrão):** ícones coloridos dos módulos à esquerda.

   - **C – Árvore com busca:** todos os módulos e funções num painel à esquerda. Digite no campo **Ir para…** (**Ctrl+K**), por exemplo "férias" ou "balancete", e clique no resultado.
   - **D – Painel inicial:** um cartão por módulo, com os atalhos e o botão **Abrir módulo**. O botão **Início** volta ao painel e a caixa **Módulo** troca de módulo direto.

   Na barra lateral (B) e na árvore (C), a navegação fica escondida numa faixa fina à esquerda, para sobrar mais espaço para as telas. Ela abre quando o mouse chega à faixa e se esconde quando ele sai. O botão **Fixar** deixa a navegação sempre aberta; **Soltar** volta a esconder.
4. O número vermelho no ícone ou na aba mostra quantas funções estão abertas naquele módulo. As janelas das funções ficam sempre na frente da janela principal: para alternar entre elas, clique no nome da função na barra de funções abertas.
5. **Sair (trocar de usuário)**, em qualquer módulo, encerra a sessão em todos.

Os executáveis de cada módulo (Vorticis Contábil, Vorticis Fiscal…) continuam disponíveis para quem prefere um módulo por janela.

## Configuração automática do banco de dados

**Para que serve:** você não precisa instalar nem configurar o PostgreSQL. O Vorticis traz um banco embutido que é criado na primeira abertura, escuta só no seu computador e para quando o último módulo é fechado.

Se o programa abrir direto na tela **Conexão com o banco de dados**, veja [Conexão com o banco](08-configuracoes.md#conexao-com-o-banco-de-dados) e [Solução de problemas](08-configuracoes.md#solucao-de-problemas).

## Primeiro acesso (vorticis / vorticis)

Em instalação nova não existe nenhum usuário. Por isso:

1. Na tela de entrada, informe usuário **vorticis** e senha **vorticis**. Esse é o login de implantação do programa (não é senha do banco).
2. O cadastro de usuários abre sozinho. Inclua o primeiro usuário com perfil **Administrador** (login, nome e senha).
3. Ao salvar, o sistema volta à tela de login. A combinação vorticis/vorticis **deixa de funcionar**. Entre com o administrador.
4. Defina os **perfis de acesso** (obrigatório na primeira vez) e clique em **Concluir configuração dos perfis**. Só então as demais funções são liberadas.
5. Cadastre a primeira empresa em *Cadastros › Empresas*.

**Dica:** na tela de entrada, **Salvar login neste computador** guarda usuário e senha (cifrados pelo Windows). Não use em computador compartilhado.

## Usuários, perfis e empresas por usuário

**Onde fica:** *Configurações › Usuários* , em quatro abas: *Usuários*, *Perfis de acesso*, *Alterar minha senha* e *Registro de acessos*. Usuários comuns veem apenas a troca de senha.

### Cadastrar um usuário (administrador)
1. Aba *Usuários* › **Incluir**.
2. Informe login, nome e **senha inicial** (o usuário troca no primeiro acesso).
3. Escolha o **perfil** (obrigatório).
4. Em **Empresas que o usuário pode acessar**, marque as empresas (busque por nome, código ou CNPJ; *Marcar todas* e *Desmarcar todas* valem para as listadas). Usuário novo vem com todas marcadas e passa a receber também as empresas cadastradas depois; ao desmarcar alguma, isso deixa de valer. Dá para ter um perfil diferente por empresa.
5. **Salvar**.

Para esquecimentos use **Redefinir senha** (gera senha temporária). Excluir um usuário mantém o histórico de lançamentos, sem o vínculo com ele; ninguém exclui o próprio usuário.

### Perfis de acesso
Um perfil é um conjunto de funções liberadas (lançar, estornar, fechar períodos, relatórios, exportar, folha, fiscal etc.). Na aba *Perfis de acesso*: **Incluir** (ou **Duplicar perfil**), marque as funções e salve. Perfis padrão, que não se excluem: **Administrador** (tudo, inclusive empresas, usuários, conexão e backup), **Contador** (todas as funções, inclusive *Manter o orçamento*), **Assistente** (lança e estorna, não fecha períodos) e **Consulta** (relatórios).

### Segurança
- Após 5 tentativas de senha errada, o usuário fica bloqueado por 15 minutos.
- Toda tentativa de login fica na aba *Registro de acessos*.
- Para trocar sua senha: aba *Alterar minha senha*.

## Certificados digitais

Usados para assinar arquivos (ECD, ECF, SPED) e transmitir o eSocial. A tela fica em *Configurações › Certificados digitais* e é exclusiva do administrador. Veja [Certificados digitais](08-configuracoes.md#certificados-digitais).

## Tela inicial e ajuda

Depois do login abre a **tela inicial**, com as próximas obrigações da empresa. *Ajuda › Tela inicial* volta a ela; *Ajuda › Licença e registro* mostra o titular da licença; *Ajuda › Sobre* mostra versão, atalhos de teclado e a conexão em uso. O seletor de empresa fica no topo da janela: escolha a empresa antes de trabalhar. Fechar o programa pede confirmação.

Logo abaixo dos menus fica, fixa, a **barra de funções abertas**: cada função aberta (que abre na sua própria janela) aparece ali com o nome; clique no nome para trazê-la para a frente e no **✕** para fechá-la. As janelas das funções nunca cobrem essa barra. Na Folha, a competência de trabalho fica na mesma linha, à direita.

### Tecla Esc

A tecla **Esc** fecha a tela (janela de função) que está na frente, em qualquer módulo. Dentro de um pop-up (por exemplo, a digitação do lançamento contábil), Esc fecha só o pop-up. Se houver algo digitado que ainda não foi salvo, o sistema pergunta antes: *Fechar sem salvar* descarta o que foi digitado e *Continuar editando* volta para a tela. Quando uma lista de opções está aberta, o primeiro Esc fecha a lista.
