# Photos du système pour prouver qu'un logiciel ne modifie rien (recette T22 sur machine jetable).
# Les fichiers bruts restent dans $env:RUNNER_TEMP (hors artefact : ils peuvent contenir des données de la machine).

$Cles = @(
    'HKLM\SOFTWARE\Policies',
    'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies',
    'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon',
    'HKLM\SOFTWARE\Microsoft\Windows Defender',
    'HKLM\SYSTEM\CurrentControlSet\Services',
    'HKLM\SYSTEM\CurrentControlSet\Control\Power',
    'HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server',
    'HKLM\SYSTEM\CurrentControlSet\Control\Lsa',
    'HKLM\SYSTEM\Setup',
    'HKCU\Software'
)

function Dossier-Photo($nom) { Join-Path $env:RUNNER_TEMP "photo-$nom" }

function Prendre-Photo($nom) {
    $d = Dossier-Photo $nom
    New-Item -ItemType Directory -Force $d | Out-Null
    $i = 0
    foreach ($c in $Cles) {
        $i++
        $f = Join-Path $d "reg$i.reg"
        & reg.exe export $c $f /y 2>&1 | Out-Null
        if (-not (Test-Path $f)) { Set-Content $f "(clé absente)" }
    }
    Get-CimInstance Win32_Service | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.StartMode)|$($_.State)" } |
        Set-Content (Join-Path $d 'services.txt')
    Get-ChildItem $env:TEMP -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName } | Sort-Object |
        Set-Content (Join-Path $d 'temp.txt')
    Get-ScheduledTask | ForEach-Object { "$($_.TaskPath)$($_.TaskName)|$($_.State)" } | Sort-Object |
        Set-Content (Join-Path $d 'taches.txt')
    Write-Output "Photo $nom prise ($((Get-ChildItem $d).Count) fichiers)"
}

# Lignes ajoutées ou retirées entre deux photos, par fichier.
function Ecarts($a, $b) {
    $res = @{}
    foreach ($f in Get-ChildItem (Dossier-Photo $a)) {
        $la = Get-Content $f.FullName -ErrorAction SilentlyContinue
        $lb = Get-Content (Join-Path (Dossier-Photo $b) $f.Name) -ErrorAction SilentlyContinue
        $diff = Compare-Object @($la) @($lb) | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
        $res[$f.Name] = @($diff)
    }
    $res
}

function Comparer-Photos($p1, $p2, $p3) {
    $bruit = Ecarts $p1 $p2
    $apres = Ecarts $p2 $p3
    $total = 0
    Write-Output "Comparaison avant / après les lectures (bruit = ce qui change tout seul en 60 s sans rien lancer)"
    Write-Output ("Clés de registre photographiées : " + ($Cles -join ', '))
    Write-Output "Plus services (mode de démarrage, état), tâches planifiées, contenu de %TEMP%."
    Write-Output ""
    foreach ($nom in $apres.Keys | Sort-Object) {
        $connu = @{}
        foreach ($l in $bruit[$nom]) { $connu[$l.Substring(3)] = $true }
        $nouveaux = @($apres[$nom] | Where-Object { -not $connu.ContainsKey($_.Substring(3)) })
        $total += $nouveaux.Count
        Write-Output ("{0} : {1} écart(s) pendant les lectures, dont {2} hors bruit" -f $nom, $apres[$nom].Count, $nouveaux.Count)
        $nouveaux | Select-Object -First 40 | ForEach-Object { Write-Output "    $_" }
    }
    Write-Output ""
    Write-Output "TOTAL hors bruit : $total"
}
