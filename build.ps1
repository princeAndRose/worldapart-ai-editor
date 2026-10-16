$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$out = Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($out) | Out-Null
Push-Location $PSScriptRoot
try {
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /reference:System.Web.Extensions.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.Xaml.dll "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" /resource:catalog.json,AIEditor.Catalog /resource:portraits.zip,AIEditor.Portraits /resource:Workspace.xaml,AIEditor.Workspace /out:dist\WorldApart.AI.Editor.exe Workspace.cs WorkspaceExtended.cs DraftState.cs Core.cs Extended.cs Tests.cs DraftTests.cs ExtendedTests.cs RenderTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $out -Force
Write-Output "Built $out/WorldApart.AI.Editor.exe"

