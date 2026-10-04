$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$installerDir = Join-Path $root 'dist\instalador-exe'
$output = Join-Path $root 'dist\BimFamilias-Instalador-Unico.exe'
$iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
if (!(Test-Path -LiteralPath $iexpress)) { throw 'IExpress não está disponível neste Windows.' }

$required = @('BimFamilias.Instalador.exe','BimFamilias.Instalador.dll','BimFamilias.Instalador.deps.json','BimFamilias.Instalador.runtimeconfig.json')
foreach ($name in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $installerDir $name))) { throw "Arquivo do instalador ausente: $name" }
}

$stage = Join-Path $env:TEMP ('bimfamilias-single-' + [Guid]::NewGuid().ToString('N'))
$sed = Join-Path $env:TEMP ('bimfamilias-single-' + [Guid]::NewGuid().ToString('N') + '.sed')
$success = $false
try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($name in $required) { Copy-Item -LiteralPath (Join-Path $installerDir $name) -Destination (Join-Path $stage $name) -Force }

    $stringLines = @('InstallPrompt=','DisplayLicense=','FinishMessage=','FriendlyName=Biblioteca de Famílias — Instalador único','PostInstallCmd=<None>','AdminQuietInstCmd=','UserQuietInstCmd=','AppLaunched=BimFamilias.Instalador.exe')
    $sourceLines = @()
    for ($i = 0; $i -lt $required.Count; $i++) {
        $stringLines += "FILE$i=$($required[$i])"
        $sourceLines += "%FILE$i%="
    }
    $sedLines = @('[Version]','Class=IEXPRESS','SEDVersion=3','[Options]','PackagePurpose=InstallApp','ShowInstallProgramWindow=1','HideExtractAnimation=1','UseLongFileName=1','InsideCompressed=1','CAB_FixedSize=0','CAB_ResvCodeSigning=0','RebootMode=I','InstallPrompt=%InstallPrompt%','DisplayLicense=%DisplayLicense%','FinishMessage=%FinishMessage%',"TargetName=$output",'FriendlyName=%FriendlyName%','AppLaunched=%AppLaunched%','PostInstallCmd=%PostInstallCmd%','AdminQuietInstCmd=%AdminQuietInstCmd%','UserQuietInstCmd=%UserQuietInstCmd%','SourceFiles=SourceFiles','[Strings]') + $stringLines + @('[SourceFiles]',"SourceFiles0=$stage",'[SourceFiles0]') + $sourceLines
    [IO.File]::WriteAllLines($sed, $sedLines, [Text.Encoding]::ASCII)
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
    $iexpressProcess = Start-Process -FilePath $iexpress -ArgumentList @('/N','/Q','/M',$sed) -Wait -PassThru
    $iexpressExitCode = $iexpressProcess.ExitCode
    if (!(Test-Path -LiteralPath $output)) { throw "O IExpress não gerou o executável único (código $iexpressExitCode). SED: $sed" }
    $success = $true
    Get-Item -LiteralPath $output | Select-Object FullName,Length,LastWriteTime
}
finally {
    if ($success) {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        if (Test-Path -LiteralPath $sed) { Remove-Item -LiteralPath $sed -Force }
    } else {
        Write-Warning "Arquivos de diagnóstico preservados: $stage e $sed"
    }
}
