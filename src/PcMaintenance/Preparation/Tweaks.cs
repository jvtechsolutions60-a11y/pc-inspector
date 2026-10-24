using System;
using System.Collections.Generic;
using System.Linq;

namespace PcMaintenance.Preparation;

/// <summary>
/// Réglages gardés pour l'acheteur : pas de publicités ni de suggestions dans le menu Démarrer, pas d'applications
/// installées toutes seules, pas de Bing dans la recherche, pas de widgets. Écrits dans le profil par défaut
/// (C:\Users\Default), dont Windows copie le compte que l'acheteur créera, et en stratégies machine.
/// Rien n'est désactivé de ce qui sert à la sécurité ou au fonctionnement (Windows Update, Defender, Store, Edge).
/// </summary>
internal static class Tweaks
{
    /// <summary>Valeurs DWORD à 0 dans ContentDeliveryManager : suggestions, applications « sponsorisées », astuces.</summary>
    private static readonly string[] ContentDelivery =
    {
        "ContentDeliveryAllowed", "OemPreInstalledAppsEnabled", "PreInstalledAppsEnabled", "PreInstalledAppsEverEnabled",
        "SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "SubscribedContentEnabled",
        "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
        "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled",
    };

    public static List<string> Run(IProgress<string> log)
    {
        var cdm = string.Join("\n", ContentDelivery.Select(v => $"Set-Dword ($u + '\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager') '{v}' 0"));
        var script = @"
function Set-Dword($path, $name, $value) {
  try {
    if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
    New-ItemProperty -Path $path -Name $name -Value $value -PropertyType DWord -Force | Out-Null
  } catch { Write-Output ('ERREUR|' + $path + '\' + $name + '|' + $_.Exception.Message) }
}

# Stratégies machine
Set-Dword 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 0
Set-Dword 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1
Set-Dword 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableConsumerAccountStateContent' 1
Set-Dword 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo' 'DisabledByGroupPolicy' 1
Set-Dword 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Chat' 'ChatIcon' 3
Write-Output 'OK|Widgets, applications suggérées et identifiant publicitaire coupés'

# Profil par défaut : copié pour le compte de l'acheteur
$hive = Join-Path $env:SystemDrive 'Users\Default\NTUSER.DAT'
& reg.exe load 'HKU\JVTECH-Default' $hive | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output ('ERREUR|' + $hive + '|chargement impossible'); return }
$u = 'Registry::HKEY_USERS\JVTECH-Default'
" + cdm + @"
Set-Dword ($u + '\Software\Policies\Microsoft\Windows\Explorer') 'DisableSearchBoxSuggestions' 1
Set-Dword ($u + '\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced') 'Start_IrisRecommendations' 0
Set-Dword ($u + '\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced') 'TaskbarDa' 0
Set-Dword ($u + '\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced') 'ShowCopilotButton' 0
Set-Dword ($u + '\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo') 'Enabled' 0
Set-Dword ($u + '\Software\Microsoft\Windows\CurrentVersion\Privacy') 'TailoredExperiencesWithDiagnosticDataEnabled' 0
Set-Dword ($u + '\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement') 'ScoobeSystemSettingEnabled' 0
Write-Output 'OK|Menu Démarrer sans publicités ni suggestions, recherche sans Bing, rappels « terminer la configuration » coupés'
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
Start-Sleep -Seconds 2
& reg.exe unload 'HKU\JVTECH-Default' | Out-Null
";
        var done = new List<string>();
        foreach (var line in PowerShell.Run(script, TimeSpan.FromMinutes(5)))
        {
            var parts = line.Split(new[] { '|' }, 3);
            if (parts[0] == "OK" && parts.Length >= 2) { done.Add(parts[1]); log.Report(parts[1] + "."); }
            else if (parts[0] == "ERREUR" && parts.Length == 3) log.Report($"Réglage non appliqué {parts[1]} : {parts[2]}");
        }
        return done;
    }
}
