; Instalador do Vorticis Suite Logística (Inno Setup 6): o programa único da transportadora — frota, manutenção, financeiro, fiscal,
; contábil e folha num só menu. Gerado pelo CI.
; Entrada: ..\publish (VorticisLogistica.exe + pasta pgsql com os binários do PostgreSQL).
; Compilar: iscc /DVersao=0.4.0 instalador\VorticisLogistica.iss

#ifndef Versao
  #define Versao "0.0.0"
#endif

[Setup]
; Produto próprio (AppId diferente do Vorticis): instala ao lado, em outra pasta, e usa o mesmo banco local (AppData\Local\Vorticis).
AppId={{8C1F4A6D-3B52-4E07-A9D1-6F2E7B4C9A30}
AppName=Vorticis Suite Logística
AppVersion={#Versao}
AppVerName=Vorticis Suite Logística {#Versao}
AppPublisher=Vorticis Tecnologia Financeira
DefaultDirName={autopf}\Vorticis Logistica
DefaultGroupName=Vorticis Suite Logística
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\saida
OutputBaseFilename=Setup-VorticisSuiteLogistica-{#Versao}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=icone\VorticisLogisticaInstalador.ico
UninstallDisplayIcon={app}\VorticisLogistica.ico
UninstallDisplayName=Vorticis Suite Logística
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.FinishedLabel=O Vorticis Suite Logística foi instalado e o banco de dados foi configurado automaticamente (na pasta AppData\Local\Vorticis\banco do seu usuário), compartilhado por todos os módulos; não é preciso instalar nem configurar o PostgreSQL.%n%nSe o banco não estiver pronto, ao abrir o programa a tela de Conexão traz o botão "Configurar automaticamente".

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Um programa só (arquivo único, sem exigir .NET instalado) e o banco de dados local
Source: "..\publish\VorticisLogistica.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "icone\VorticisLogistica.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\pgsql\*"; DestDir: "{app}\pgsql"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifexist "..\publish\vc_redist.x64.exe"
; Microsoft Visual C++ Redistributable 2015-2022 (x64), instalado em silêncio só se ainda não existir (ver InstalarVcRuntime)
Source: "..\publish\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not VcRuntimeInstalado
#endif

[Dirs]
; Pasta padrão dos backups (dentro da instalação), gravável pelos usuários; não é apagada na desinstalação.
Name: "{app}\Backups"; Permissions: users-modify; Flags: uninsneveruninstall

[InstallDelete]
; Atalhos do nome antigo (Vorticis Logística), trocados pelos de Vorticis Suite Logística ao atualizar
Type: files; Name: "{autoprograms}\Vorticis Logística.lnk"
Type: files; Name: "{autodesktop}\Vorticis Logística.lnk"

[Icons]
Name: "{autoprograms}\Vorticis Suite Logística"; Filename: "{app}\VorticisLogistica.exe"; IconFilename: "{app}\VorticisLogistica.ico"
Name: "{autodesktop}\Vorticis Suite Logística"; Filename: "{app}\VorticisLogistica.exe"; IconFilename: "{app}\VorticisLogistica.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\VorticisLogistica.exe"; Description: "{cm:LaunchProgram,Vorticis Suite Logística}"; Flags: nowait postinstall skipifsilent

[Code]
// O banco local roda a partir da pasta pgsql do programa; é preciso pará-lo antes de substituir ou remover
// esses arquivos. Os dados (%LOCALAPPDATA%\Vorticis) NÃO são apagados na desinstalação.
procedure PararBancoLocal();
var
  Codigo, I: Integer;
  PgCtl, Dados: String;
begin
  PgCtl := ExpandConstant('{app}\pgsql\bin\pg_ctl.exe');
  // Perseus: pasta de dados das versões anteriores à troca de nome (o programa a renomeia ao abrir).
  for I := 0 to 1 do
  begin
    if I = 0 then Dados := ExpandConstant('{localappdata}\Vorticis\banco')
    else Dados := ExpandConstant('{localappdata}\Perseus\banco');
    if FileExists(PgCtl) and FileExists(Dados + '\postmaster.pid') then
      Exec(PgCtl, 'stop -D "' + Dados + '" -m fast -w -t 60', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  end;
end;

var
  Progresso: TOutputProgressWizardPage;

// Runtime do Visual C++ 2015-2022 (x64): exigido pelos binários do PostgreSQL e pelo próprio programa.
function VcRuntimeInstalado(): Boolean;
var
  Instalado: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Instalado) and (Instalado = 1);
  if not Result then
    Result := FileExists(ExpandConstant('{sys}\vcruntime140_1.dll')) and FileExists(ExpandConstant('{sys}\msvcp140.dll'));
end;

// Instala o redistribuível em silêncio (o instalador do Visual C++ pede elevação por conta própria: "runas").
procedure InstalarVcRuntime();
var
  Codigo: Integer;
  Arquivo: String;
begin
  Arquivo := ExpandConstant('{tmp}\vc_redist.x64.exe');
  if (not FileExists(Arquivo)) or VcRuntimeInstalado() then Exit;
  Progresso.SetText('Instalando o Microsoft Visual C++ Redistributable (x64)...', '');
  // Se o usuário recusar a elevação, segue: as DLLs também vão em pgsql\bin, e a tela de Conexão explica o que faltar.
  ShellExec('runas', Arquivo, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;

function ModuloParaConfigurar(): String;
begin
  Result := 'VorticisLogistica.exe';
end;

// Configuração automática do banco: o primeiro módulo selecionado roda com --configurar-banco (sem janela; código 0 = pronto).
procedure ConfigurarBanco();
var
  Codigo: Integer;
  ArquivoLog: String;
begin
  ArquivoLog := ExpandConstant('{localappdata}\Vorticis\logs\configuracao-banco.log');
  Progresso.SetText('Configurando o banco de dados do Vorticis (pode levar até um minuto)...', '');
  Progresso.SetProgress(0, 0);
  if (not Exec(ExpandConstant('{app}\' + ModuloParaConfigurar()), '--configurar-banco', ExpandConstant('{app}'), SW_HIDE,
               ewWaitUntilTerminated, Codigo)) then
    Codigo := -1;
  Log('Configuração automática do banco: código de saída ' + IntToStr(Codigo));
  if Codigo = 0 then
    Progresso.SetText('Banco de dados configurado com sucesso.', '')
  else
  begin
    Progresso.SetText('O banco de dados não pôde ser configurado agora.', '');
    MsgBox('Não foi possível configurar o banco de dados automaticamente (código ' + IntToStr(Codigo) + ').' + #13#10#13#10 +
           'A instalação foi concluída. Ao abrir o Vorticis, a tela de Conexão mostrará o botão "Configurar automaticamente" ' +
           'para tentar de novo.' + #13#10#13#10 + 'Detalhes no registro: ' + ArquivoLog, mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  Progresso := CreateOutputProgressPage('Configurando o Vorticis', 'Preparando o runtime e o banco de dados');
  Progresso.Show;
  try
    InstalarVcRuntime();
    ConfigurarBanco();
  finally
    Progresso.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  PararBancoLocal();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  PararBancoLocal();
  Result := True;
end;
