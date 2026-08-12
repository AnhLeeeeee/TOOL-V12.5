param(
    [string]$Root = ""
)

$ErrorActionPreference = "Stop"

function Find-SourceRoot {
    param([string]$Start)

    $candidates = @()
    if ($Start) { $candidates += (Resolve-Path $Start).Path }
    $candidates += (Get-Location).Path
    $candidates += $PSScriptRoot
    $candidates += (Split-Path $PSScriptRoot -Parent)

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $current = $candidate
        for ($i = 0; $i -lt 5; $i++) {
            $target = Join-Path $current "ManagerShared\Services\TikTokProfileService.cs"
            if (Test-Path $target) { return $current }
            $parent = Split-Path $current -Parent
            if (!$parent -or $parent -eq $current) { break }
            $current = $parent
        }
    }

    throw "Khong tim thay thu muc source V12.5. Hay chay script trong thu muc goc ToolTikTok V12.5 SOURCE, hoac truyen -Root."
}

$sourceRoot = Find-SourceRoot $Root
$target = Join-Path $sourceRoot "ManagerShared\Services\TikTokProfileService.cs"

Write-Host "Source root: $sourceRoot"
Write-Host "Target     : $target"

$content = [System.IO.File]::ReadAllText($target)
$newLine = '    public static string ProfilesRoot => Path.Combine(AppContext.BaseDirectory, "TikTokProfiles");'

if ($content.Contains($newLine.Trim())) {
    Write-Host "FIX da duoc ap dung truoc do. Khong thay doi file." -ForegroundColor Green
    exit 0
}

# Match the old hard-coded declaration only. Do not touch LegacyImportedProfilePath.
$pattern = '(?m)^\s*public\s+const\s+string\s+ProfilesRoot\s*=\s*@"D:\\TOOL V2\\TikTokProfiles";\s*$'
if (-not [regex]::IsMatch($content, $pattern)) {
    throw "Khong tim thay dong ProfilesRoot hard-code mong doi. File co the da thay doi; dung PATCH.diff de sua thu cong."
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = "$target.bak_dynamic_path_$stamp"
Copy-Item $target $backup -Force

$content = [regex]::Replace($content, $pattern, $newLine, 1)
[System.IO.File]::WriteAllText($target, $content, [System.Text.UTF8Encoding]::new($true))

Write-Host ""
Write-Host "DA CAP NHAT THANH CONG" -ForegroundColor Green
Write-Host "Backup: $backup"
Write-Host ""
Write-Host "Profile moi se luu tai:"
Write-Host "  <thu_muc_chua_EXE>\TikTokProfiles\<profile>\chrome_profile"
Write-Host ""
Write-Host "Buoc tiep theo: chay BUILD_V12_5.bat de build lai."
