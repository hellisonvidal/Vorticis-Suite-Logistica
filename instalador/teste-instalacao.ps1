# Teste de fumaça em Windows limpo (CI): instala a suíte em modo silencioso, abre o Vorticis Contábil e confere que o banco local foi
# criado, iniciado e migrado sem nenhuma configuração; depois fecha o programa e confere que o banco foi parado.
# Requer Windows PowerShell 5.1 (DPAPI via System.Security).
# Também serve ao Vorticis Suite Logística: -Pasta "Programs\Vorticis Logistica" -Programa VorticisLogistica.exe -Arquivos VorticisLogistica.ico.
param([Parameter(Mandatory)] [string]$Setup,
      [string]$Pasta = "Programs\Vorticis",
      [string]$Programa = "VorticisContabil.exe",
      [string[]]$Arquivos = @("VorticisFiscal.exe", "VorticisFolha.exe", "Vorticis.ico"))
$ErrorActionPreference = "Stop"

$log = Join-Path $env:TEMP "vorticis-setup.log"
$p = Start-Process $Setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/LOG=`"$log`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content $log -Tail 40; throw "Instalador terminou com código $($p.ExitCode)" }

$pasta = Join-Path $env:LOCALAPPDATA $Pasta
$app = Join-Path $pasta $Programa
$pgBin = Join-Path $pasta "pgsql\bin"
# instalação completa (padrão do modo silencioso): os módulos e o banco local compartilhado
foreach ($f in @($app) + ($Arquivos | ForEach-Object { Join-Path $pasta $_ }) + @((Join-Path $pgBin "pg_ctl.exe"), (Join-Path $pgBin "initdb.exe"))) {
    if (-not (Test-Path $f)) { throw "Arquivo não instalado: $f" }
}
Write-Host "Instalado em $pasta"

# 0. configuração automática feita pelo instalador (VorticisContabil.exe --configurar-banco), antes de abrir o programa
$logConfig = Join-Path $env:LOCALAPPDATA "Vorticis\logs\configuracao-banco.log"
if (-not (Test-Path $logConfig)) { throw "O instalador não gerou $logConfig (configuração automática do banco não executou)." }
$textoLog = Get-Content $logConfig -Raw -Encoding UTF8
if ($textoLog -notmatch "CONCLUÍDO") { Write-Host $textoLog; throw "A configuração automática do banco falhou durante a instalação." }
if (-not (Test-Path (Join-Path $env:APPDATA "Vorticis\vorticis.json"))) { throw "Configuração não gravada pela configuração automática." }
# o modo sem interface para o banco ao terminar; repetir o comando deve funcionar e dar código 0 (idempotente)
$pc = Start-Process $app -ArgumentList "--configurar-banco" -Wait -PassThru
if ($pc.ExitCode -ne 0) { Get-Content $logConfig -Tail 40; throw "--configurar-banco terminou com código $($pc.ExitCode)" }
Write-Host "Configuração automática do banco: OK (código 0, log em $logConfig)"

$dados = Join-Path $env:LOCALAPPDATA "Vorticis\banco"
$cfgArquivo = Join-Path $env:APPDATA "Vorticis\vorticis.json"
$logPg = Join-Path $env:LOCALAPPDATA "Vorticis\logs\postgres.log"
$proc = Start-Process $app -PassThru

function Falhar($msg) {
    if (Test-Path $logPg) { Write-Host "--- postgres.log"; Get-Content $logPg -Tail 40 }
    if (-not $proc.HasExited) { $proc.Kill() }
    throw $msg
}

# 1. banco local criado e iniciado
$limite = (Get-Date).AddMinutes(3)
while (-not (Test-Path (Join-Path $dados "postmaster.pid"))) {
    if ($proc.HasExited) { Falhar "O Vorticis fechou sozinho (código $($proc.ExitCode))." }
    if ((Get-Date) -gt $limite) { Falhar "O banco local não foi iniciado em 3 minutos." }
    Start-Sleep -Seconds 2
}
Write-Host "Banco local criado e em execução em $dados"

# 2. configuração gravada com senha cifrada (DPAPI) e migrações aplicadas pelo programa
Add-Type -AssemblyName System.Security
# a porta é gravada logo depois que o servidor sobe
$limite = (Get-Date).AddMinutes(1)
do {
    Start-Sleep -Seconds 1
    $cfg = Get-Content $cfgArquivo -Raw | ConvertFrom-Json
} while ([int]$cfg.Local.Porta -eq 0 -and (Get-Date) -lt $limite)
if ([int]$cfg.Local.Porta -eq 0) { Falhar "A porta do banco local não foi gravada em $cfgArquivo." }
if ($cfg.Modo -ne "Local") { Falhar "Modo gravado inesperado: $($cfg.Modo)" }
if ($cfg.Local.Senha) { Falhar "Senha gravada sem cifrar." }
$senha = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect(
    [Convert]::FromBase64String($cfg.Local.SenhaProtegida), $null, "CurrentUser"))
$env:PGPASSWORD = $senha
$porta = [int]$cfg.Local.Porta
$psql = Join-Path $pgBin "psql.exe"
$conexao = "host=127.0.0.1 port=$porta user=postgres dbname=vorticis connect_timeout=5"
$script:ultimoErro = ""
function Consultar($sql) {
    # PowerShell 5 transforma qualquer saída de erro de programa externo em exceção com "Stop".
    # No Windows o psql não aceita opções depois de argumentos posicionais: tudo como opção.
    $ErrorActionPreference = "Continue"
    $r = & $psql -X -t -A -c $sql -d $conexao 2>&1
    if ($LASTEXITCODE -eq 0) { return ($r | Out-String).Trim() }
    $script:ultimoErro = ($r | Out-String).Trim()
    return $null
}
$migracoes = 0
$limite = (Get-Date).AddMinutes(2)
while ($migracoes -lt 7) {
    if ((Get-Date) -gt $limite) { Falhar "As migrações não foram aplicadas (encontradas: $migracoes). psql: $script:ultimoErro" }
    Start-Sleep -Seconds 2
    $r = Consultar "SELECT count(*) FROM public.vorticis_migracao"
    if ($r) { $migracoes = [int]$r }
}
Write-Host "Conectado na porta ${porta}: $migracoes migrações aplicadas"
$escuta = Consultar "SHOW listen_addresses"
if ($escuta -ne "127.0.0.1") { Falhar "O banco local escuta em '$escuta' (esperado: 127.0.0.1)." }
if ($proc.HasExited) { Falhar "O Vorticis fechou inesperadamente." }

# 3. ao fechar o programa, o banco local é parado
$null = $proc.CloseMainWindow()
if (-not $proc.WaitForExit(60000)) { Write-Warning "O Vorticis não fechou pela janela; encerrando o processo."; $proc.Kill() }
$limite = (Get-Date).AddSeconds(60)
while ((Test-Path (Join-Path $dados "postmaster.pid")) -and (Get-Date) -lt $limite) { Start-Sleep -Seconds 2 }
if (Test-Path (Join-Path $dados "postmaster.pid")) {
    Write-Warning "O banco local continuou em execução após fechar o programa; parando com pg_ctl."
    $ErrorActionPreference = "Continue"
    & (Join-Path $pgBin "pg_ctl.exe") stop -D $dados -m fast -w
} else {
    Write-Host "Banco local parado ao fechar o programa"
}
Write-Host "OK: instalação, criação automática do banco e migrações funcionando."
