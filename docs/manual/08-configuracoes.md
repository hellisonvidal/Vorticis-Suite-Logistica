# Configurações

Menu *Configurações*. As telas de backup, configurações gerais e conexão são exclusivas do administrador.

## Backup e restauração

**Para que serve:** guardar cópias de segurança de todo o banco de dados e voltar a elas se for preciso.
**Onde fica:** *Configurações › Backup e restauração*.

### Fazer backup
1. Veja a **pasta** de destino. O padrão é a pasta *Backups* dentro da instalação (ou *Documentos\Vorticis Contabil\Backups*, se não for possível gravar). **Escolher pasta...** muda o destino dos próximos backups, inclusive os automáticos.
2. Clique em **Fazer backup agora**. O arquivo aparece na lista (nome, data e tamanho).

### Restaurar
1. Selecione o arquivo e clique em **Restaurar backup selecionado...**.
2. Informe a **senha de administrador** (padrão `VORTICIS`, alterável em Configurações gerais).
3. O sistema faz antes um **backup de segurança** do estado atual e encerra todas as sessões.

**Excluir backup selecionado** apaga o arquivo (com confirmação). **Dica:** copie os backups também para outro disco ou nuvem; um backup na mesma máquina não protege contra defeito do computador.

## Configurações gerais

**Onde fica:** *Configurações › Configurações gerais*.

### Senha de administrador
Usada para confirmar restauração de backup e limpeza do banco. Informe a senha atual, a nova e a confirmação e clique em **Alterar senha**. Troque a senha padrão (`VORTICIS`) logo no início.

### Limpar banco de dados (não recomendado)
Apaga **todas** as empresas, cadastros, usuários e perfis personalizados. Gera um backup antes.
1. Digite **LIMPAR** e confirme.
2. Informe a senha de administrador.
3. O programa volta à tela de login como numa instalação nova: só entra `vorticis` / `vorticis`, para cadastrar o administrador.

## Conexão com o banco de dados

**Onde fica:** *Configurações › Conexão com o banco de dados*.

- **Configuração automática** (recomendado): preenche o usuário e a senha do PostgreSQL só se for usar um servidor local já instalado; clique em **Configurar automaticamente**. O sistema mostra cada passo.
- **Banco de dados local do Vorticis**: porta, pasta dos dados e troca da senha do banco (deixe em branco para manter). **Recriar o banco local** cria um novo e guarda o antigo como `banco.backup-<data>`.
- **Servidor PostgreSQL (rede ou nuvem)**: informe servidor, porta, banco, usuário e senha (*Mostrar senhas* ajuda a conferir), **Testar conexão** e **Salvar e reconectar**. É assim que vários computadores usam o mesmo banco. O botão para usar o banco local volta ao embutido.

## Certificados digitais

**Para que serve:** guardar os certificados usados para assinar ECD, ECF, SPED, PDFs e para transmitir o eSocial.
**Onde fica:** *Configurações › Certificados digitais* (administrador).

### Certificado A1 (arquivo .pfx ou .p12)
1. Em *Importar certificado A1*, escolha o **arquivo** e digite a **senha**.
2. **Conferir** mostra titular, CNPJ/CPF e validade.
3. Informe uma descrição (ex.: e-CNPJ da empresa) e clique em **Importar**. O arquivo é guardado como está, protegido pela própria senha; **a senha não é gravada** e será pedida no uso (ou cadastrada para uso do sistema, quando permitido).

### Certificado A3 (token ou cartão)
Clique em **Ler certificados instalados no Windows**, selecione e **Registrar A3**. Só o registro é guardado; a chave fica no token. Certificados A1 instalados no Windows (com chave exportável) podem ser importados para o Vorticis.

### Certificado padrão
Selecione o certificado e marque **Padrão do contador**, **do responsável** ou **do procurador** para que os assistentes (ECD, ECF, EFD, PDF assinado, eSocial) o escolham automaticamente. **Retirar padrão** desfaz; **Excluir do repositório** remove.

A lista avisa os certificados **vencidos ou que vencem em até 30 dias**.

## Solução de problemas

### O programa abriu na tela de conexão
1. Clique em **Configurar automaticamente**. Ele localiza o PostgreSQL embutido, confere o Visual C++, remove travas antigas, cria e inicia o banco, aplica as atualizações e testa a conexão.
2. Se falhar, clique em **Copiar diagnóstico** e envie ao suporte. Os logs ficam em `%LOCALAPPDATA%\Vorticis\logs`.

### Problemas frequentes
| Situação | O que fazer |
|---|---|
| "vorticis/vorticis" não entra | Só vale em instalação sem nenhum usuário. Use o login do administrador cadastrado. |
| Existe banco local, mas a senha não é conhecida | Use **Recriar o banco local** (a pasta antiga é preservada como `banco.backup-<data>`). |
| Faltam DLLs do Visual C++ | Instale o `vc_redist.x64.exe` (vem no instalador e nos portáteis). |
| Usuário bloqueado | Aguarde 15 minutos ou peça ao administrador para redefinir a senha. |
| Não vejo uma opção do menu | O perfil do usuário não libera a função, ou não há empresa selecionada. |
| Dois módulos abertos | É normal: compartilham o mesmo banco. O banco só para quando o último fecha. |
| Lançamento recusado por período | Reabra a competência em *Fechamento de períodos* ou revise a data de bloqueio da empresa. |

Sem o PostgreSQL embutido ao lado do programa, a configuração tenta um servidor em `localhost:5432` e pede apenas usuário e senha.
