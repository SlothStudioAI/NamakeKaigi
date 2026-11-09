<#
.SYNOPSIS
    NamakeKaigi の自己完結型 (self-contained) Release ビルドを作成し、配布用zipにまとめるスクリプト。

.DESCRIPTION
    dotnet publish を Release / win-x64 / 自己完結型 / シングルファイル出力オプション付きで実行し、
    .NET 10 未導入のWindows PCでもそのまま実行できる配布物を生成する。
    生成された publish フォルダを Compress-Archive で zip 化し、release フォルダに出力する。

.PARAMETER Version
    配布zipのファイル名に付与するバージョン番号。省略すると、NamakeKaigi.csproj の Version を読む(版の正本はそこ1か所)。

.EXAMPLE
    ./scripts/build_release_zip.ps1
    ./scripts/build_release_zip.ps1 -Version "1.1.0"
#>
param(
    [string]$Version = ""
)

# エラー発生時は即座に処理を停止する
$ErrorActionPreference = "Stop"

# スクリプトの配置場所からリポジトリルートを算出する
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

$csprojPath = Join-Path $repoRoot "src/NamakeKaigi/NamakeKaigi.csproj"
# 版の指定がなければ、csproj の <Version> を読む
if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-Path $csprojPath)) { Write-Error "プロジェクトファイルが見つかりません: $csprojPath"; exit 1 }
    $match = Select-String -Path $csprojPath -Pattern "<Version>([^<]+)</Version>" | Select-Object -First 1
    if ($null -eq $match) { Write-Error "csproj に <Version> がありません。-Version で指定してください"; exit 1 }
    $Version = $match.Matches[0].Groups[1].Value.Trim()
}
$publishDir = Join-Path $repoRoot "artifacts/publish/win-x64"
$releaseDir = Join-Path $repoRoot "release"
$zipFileName = "NamakeKaigi-v$Version-win-x64.zip"
$zipPath = Join-Path $releaseDir $zipFileName

Write-Host "=== NamakeKaigi Release ビルドを開始します ===" -ForegroundColor Cyan
Write-Host "プロジェクト: $csprojPath"
Write-Host "publish出力先: $publishDir"
Write-Host "zip出力先    : $zipPath"

if (-not (Test-Path $csprojPath)) {
    Write-Error "プロジェクトファイルが見つかりません: $csprojPath"
    exit 1
}

# 既存のpublish出力があれば作り直すためクリーンアップする
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

# ビルド時の絶対パス（利用者環境に不要な作者のフォルダ構成）が exe に残らないよう、
# リポジトリ直下を固定の仮想パス "/src" に置換する (PathMap)。
# 末尾の区切り文字は付けず、Resolve-Path の結果を文字列化して使う。
$pathMap = "$($repoRoot.Path)=/src"

# 自己完結型・シングルファイルでのpublishを実行する
# DebugType=none / DebugSymbols=false: 利用者に不要なPDB（絶対パスを含む）を出力しない
dotnet publish $csprojPath `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    "-p:PathMap=$pathMap" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish に失敗しました (終了コード: $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# 念のため、PDBが publish 出力に残っていれば除去してからzip化する
Get-ChildItem -Path $publishDir -Filter "*.pdb" -Recurse | Remove-Item -Force

# 既存の同名zipがあれば上書きするため削除する
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Write-Host "publish結果をzip化しています..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

if (-not (Test-Path $zipPath)) {
    Write-Error "zipファイルの生成に失敗しました: $zipPath"
    exit 1
}

Write-Host "=== 完了しました ===" -ForegroundColor Green
Write-Host "配布用zip: $zipPath"
