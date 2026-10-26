# Baixa os binários oficiais do PostgreSQL para Windows (EDB) e monta a pasta "pgsql" distribuída com o Vorticis:
# só bin, lib e share (sem pgAdmin, StackBuilder, documentação, cabeçalhos e símbolos de depuração).
param(
    [string]$Destino = "pgsql",
    [string[]]$Versoes = @("16.14", "16.13", "16.12", "16.11", "16.10", "16.9")
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$temp = Join-Path ([IO.Path]::GetTempPath()) "vorticis-pg-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $temp | Out-Null
$zip = Join-Path $temp "pg.zip"
$versao = $null
foreach ($v in $Versoes) {
    $url = "https://get.enterprisedb.com/postgresql/postgresql-$v-1-windows-x64-binaries.zip"
    try {
        Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
        $versao = $v
        Write-Host "PostgreSQL $v baixado de $url"
        break
    } catch {
        Write-Host "PostgreSQL $v indisponível ($($_.Exception.Message))"
    }
}
if (-not $versao) { throw "Nenhuma versão do PostgreSQL pôde ser baixada." }

Expand-Archive $zip -DestinationPath $temp
$origem = Join-Path $temp "pgsql"
if (Test-Path $Destino) { Remove-Item $Destino -Recurse -Force }
New-Item -ItemType Directory $Destino | Out-Null
foreach ($d in "bin", "lib", "share") { Copy-Item (Join-Path $origem $d) (Join-Path $Destino $d) -Recurse }
Get-ChildItem $Destino -Recurse -Include *.pdb, *.lib | Remove-Item -Force
foreach ($d in "share\doc", "share\locale\*\LC_MESSAGES\pgadmin*") {
    Get-Item (Join-Path $Destino $d) -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
}

# Runtime do Visual C++ ao lado dos executáveis (implantação local permitida pela Microsoft),
# para funcionar em Windows sem o "Visual C++ Redistributable" instalado.
$bin = Join-Path $Destino "bin"
foreach ($dll in "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll") {
    if (-not (Test-Path (Join-Path $bin $dll))) {
        $origemDll = Join-Path $env:SystemRoot "System32\$dll"
        if (-not (Test-Path $origemDll)) { throw "$dll não encontrado em System32: instale o Visual C++ Redistributable 2015-2022 (x64) nesta máquina." }
        Copy-Item $origemDll $bin
        Write-Host "Copiado $dll do sistema"
    }
}
foreach ($f in "initdb.exe", "pg_ctl.exe", "postgres.exe", "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll") {
    if (-not (Test-Path (Join-Path $bin $f))) { throw "Faltou $f em $bin" }
}

Copy-Item (Join-Path $PSScriptRoot "LICENCA-PostgreSQL.txt") $Destino
Set-Content (Join-Path $Destino "VERSAO.txt") "PostgreSQL $versao (binários oficiais EDB para Windows x64)"
Remove-Item $temp -Recurse -Force
$tamanho = (Get-ChildItem $Destino -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Pasta {0} pronta: PostgreSQL {1}, {2:N0} MB" -f $Destino, $versao, $tamanho)
