param([string[]]$RevitYear)
$ErrorActionPreference='Stop'
$packageRoot=$PSScriptRoot
$distRoot=Join-Path $packageRoot 'dist'
$identity='2E766DF8-CFC7-4713-B0B9-B7E26E533DA8'

function Get-DetectedRevitYears {
    $found=New-Object 'System.Collections.Generic.HashSet[string]'
    $locations=@()
    if($env:ProgramFiles){$locations+=Join-Path $env:ProgramFiles 'Autodesk'}
    $locations+=Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
    foreach($location in $locations){
        if(-not (Test-Path -LiteralPath $location)){continue}
        foreach($item in Get-ChildItem -LiteralPath $location -Directory -ErrorAction SilentlyContinue){
            if($item.Name -match '(?:Revit\s+)?(20\d{2})$'){[void]$found.Add($Matches[1])}
        }
    }
    if($found.Count -eq 0){[void]$found.Add('2027')}
    return @($found | Sort-Object)
}

function Install-Build([string]$year,[string]$source){
    $main=Join-Path $source 'BimFamilias.Revit.dll'
    if(-not (Test-Path -LiteralPath $main)){return "Revit $year`: build ausente em $source"}
    $hashInput=[string]::Join('|',(Get-ChildItem -LiteralPath $source -File | Where-Object {$_.Name -like 'BimFamilias.*'} | Sort-Object Name | ForEach-Object {$_.Name+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}))
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashInput)))).Replace('-','')}finally{$sha.Dispose()}
    $addinRoot=Join-Path $env:APPDATA ("Autodesk\Revit\Addins\$year")
    $target=Join-Path $addinRoot ('BimFamilias\1.0-'+$hash.Substring(0,12))
    $manifest=Join-Path $addinRoot 'BimFamilias.addin'
    if(Test-Path -LiteralPath $manifest){
        [xml]$old=[IO.File]::ReadAllText($manifest)
        if($old.RevitAddIns.AddIn.AddInId -ne $identity){throw "O manifesto existente do Revit $year pertence a outro complemento."}
    }
    [IO.Directory]::CreateDirectory($target)|Out-Null
    foreach($file in Get-ChildItem -LiteralPath $source -File){
        if($file.Name -notlike 'BimFamilias.*'){continue}
        $destination=Join-Path $target $file.Name
        if(Test-Path -LiteralPath $destination){
            if((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "Arquivo existente diferente: $destination"}
        }else{Copy-Item -LiteralPath $file.FullName -Destination $destination}
    }
    $installation=[ordered]@{InstallationId=[guid]::NewGuid().ToString('N');InstalledAt=(Get-Date).ToUniversalTime().ToString('o');PackageHash=$hash;RevitYear=$year}
    $installation|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $target 'installation.json') -Encoding UTF8
    if(Test-Path -LiteralPath $manifest){Copy-Item -LiteralPath $manifest -Destination (Join-Path $target ('manifesto-'+(Get-Date -Format 'yyyyMMddHHmmss')+'.bak'))}
    [xml]$xml='<RevitAddIns><AddIn Type="Application"><Name>Biblioteca de Familias</Name><Assembly></Assembly><AddInId>2E766DF8-CFC7-4713-B0B9-B7E26E533DA8</AddInId><FullClassName>BimFamilias.Revit.App</FullClassName><VendorId>DGMX</VendorId><VendorDescription>Biblioteca local de familias BIM</VendorDescription><AllowLoadingIntoExistingSession>Yes</AllowLoadingIntoExistingSession></AddIn></RevitAddIns>'
    $xml.SelectSingleNode('/RevitAddIns/AddIn/Assembly').InnerText=[string](Join-Path $target 'BimFamilias.Revit.dll')
    [IO.Directory]::CreateDirectory($addinRoot)|Out-Null;$xml.Save($manifest)
    return "Revit $year`: instalado em $manifest"
}

$years=if($RevitYear){@($RevitYear|ForEach-Object {[string]$_})}else{Get-DetectedRevitYears}
$installed=0
foreach($year in $years){
    if($year -notmatch '^20\d{2}$'){Write-Output "Ignorado: versão inválida $year";continue}
    $source=Join-Path $distRoot ("revit-$year")
    if($year -eq '2027' -and -not (Test-Path -LiteralPath $source)){$source=Join-Path $distRoot 'revit-final'}
    $result=Install-Build $year $source;Write-Output $result
    if($result -like "Revit $year`: instalado*"){$installed++}
}
if($installed -eq 0){throw 'Nenhuma build compatível foi encontrada. Cada versão do Revit precisa de uma build própria em dist\revit-AAAA.'}
Write-Output 'Instalação concluída. Reinicie o Revit para carregar a aba Familia.'

