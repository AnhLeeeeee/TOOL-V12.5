param(
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y,
    [Parameter(Mandatory=$true)][int]$Width,
    [Parameter(Mandatory=$true)][int]$Height,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [Parameter(Mandatory=$false)][string]$DebugFile = ""
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-Text([string]$Path, [string]$Text) {
    $dir = [IO.Path]::GetDirectoryName($Path)
    if ($dir -and -not [IO.Directory]::Exists($dir)) {
        [IO.Directory]::CreateDirectory($dir) | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Find-Tesseract {
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if ($env:TESSERACT_EXE) { $candidates.Add($env:TESSERACT_EXE) }
    $candidates.Add((Join-Path $PSScriptRoot 'tesseract\tesseract.exe'))
    $candidates.Add((Join-Path $PSScriptRoot 'tesseract.exe'))
    if ($env:ProgramFiles) {
        $candidates.Add((Join-Path $env:ProgramFiles 'Tesseract-OCR\tesseract.exe'))
    }
    if (${env:ProgramFiles(x86)}) {
        $candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Tesseract-OCR\tesseract.exe'))
    }
    try {
        $cmd = Get-Command tesseract.exe -ErrorAction Stop
        $candidates.Add($cmd.Source)
    }
    catch { }

    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath $p)) { return $p }
    }
    return $null
}

function Invoke-Tesseract([string]$Exe, [string]$ImagePath, [int]$Psm) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = '"' + $ImagePath + '" stdout --psm ' + $Psm + ' -l eng -c tessedit_char_whitelist=0123456789.,KMBkmb'

    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    return [pscustomobject]@{
        Code  = $process.ExitCode
        Text  = $stdout.Trim()
        Error = $stderr.Trim()
    }
}

try {
    if ($Width -le 0 -or $Height -le 0) {
        throw 'Kích thước vùng Tesseract không hợp lệ.'
    }

    Add-Type -AssemblyName System.Drawing

    $tesseract = Find-Tesseract
    if (-not $tesseract) {
        throw 'Không tìm thấy tesseract.exe. Hãy cài Tesseract hoặc đặt tesseract.exe trong thư mục tesseract cạnh tool.'
    }

    $tempDir = Join-Path $env:TEMP ('tiktok_tesseract_' + $PID + '_' + [DateTime]::UtcNow.Ticks)
    [IO.Directory]::CreateDirectory($tempDir) | Out-Null
    $rawPath = Join-Path $tempDir 'raw.png'
    $processedPath = Join-Path $tempDir 'processed.png'

    # Dùng constructor .NET trực tiếp thay cho New-Object Type(args).
    # Cách cũ khiến PowerShell ghép các đối số thành System.Object[] rồi lỗi op_Multiply.
    $source = [System.Drawing.Bitmap]::new(
        [int]$Width,
        [int]$Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )

    $graphics = [System.Drawing.Graphics]::FromImage($source)
    try {
        $graphics.CopyFromScreen(
            [int]$X,
            [int]$Y,
            0,
            0,
            $source.Size,
            [System.Drawing.CopyPixelOperation]::SourceCopy
        )
    }
    finally {
        $graphics.Dispose()
    }
    $source.Save($rawPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $scale = [int]5
    $scaledWidth = [int]($Width * $scale)
    $scaledHeight = [int]($Height * $scale)

    $scaled = [System.Drawing.Bitmap]::new(
        $scaledWidth,
        $scaledHeight,
        [System.Drawing.Imaging.PixelFormat]::Format24bppRgb
    )

    $graphics2 = [System.Drawing.Graphics]::FromImage($scaled)
    try {
        $graphics2.Clear([System.Drawing.Color]::White)
        $graphics2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics2.DrawImage($source, 0, 0, $scaledWidth, $scaledHeight)
    }
    finally {
        $graphics2.Dispose()
        $source.Dispose()
    }

    # Chữ sáng trên nền tối -> chữ đen trên nền trắng.
    for ($yy = 0; $yy -lt $scaledHeight; $yy++) {
        for ($xx = 0; $xx -lt $scaledWidth; $xx++) {
            $color = $scaled.GetPixel($xx, $yy)
            $red = [int]$color.R
            $green = [int]$color.G
            $blue = [int]$color.B
            $gray = [int][Math]::Round((0.299 * $red) + (0.587 * $green) + (0.114 * $blue))

            if ($gray -ge 105) { $value = 0 }
            else { $value = 255 }

            $scaled.SetPixel(
                $xx,
                $yy,
                [System.Drawing.Color]::FromArgb($value, $value, $value)
            )
        }
    }

    $scaled.Save($processedPath, [System.Drawing.Imaging.ImageFormat]::Png)

    if ($DebugFile) {
        $debugDir = [IO.Path]::GetDirectoryName($DebugFile)
        if ($debugDir -and -not [IO.Directory]::Exists($debugDir)) {
            [IO.Directory]::CreateDirectory($debugDir) | Out-Null
        }
        $scaled.Save($DebugFile, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $scaled.Dispose()

    $logs = New-Object 'System.Collections.Generic.List[string]'
    $logs.Add(('tesseract={0}' -f $tesseract))
    $logs.Add(('region=X:{0};Y:{1};W:{2};H:{3}' -f $X, $Y, $Width, $Height))

    $scores = @{}
    $weights = @{ 8 = 4; 13 = 4; 7 = 2; 6 = 1 }

    foreach ($imagePath in @($processedPath, $rawPath)) {
        foreach ($psm in @(8, 13, 7, 6)) {
            $result = Invoke-Tesseract $tesseract $imagePath $psm
            $logs.Add((
                'image={0}; psm={1}; code={2}; text={3}; error={4}' -f `
                ([IO.Path]::GetFileName($imagePath)),
                $psm,
                $result.Code,
                $result.Text,
                $result.Error
            ))

            $match = [regex]::Match($result.Text, '(?i)\d+(?:[\.,]\d+)?\s*[KMB]?')
            if ($match.Success) {
                $candidate = (($match.Value -replace '\s+', '').ToUpperInvariant())
                if (-not $scores.ContainsKey($candidate)) { $scores[$candidate] = 0 }
                $scores[$candidate] += $weights[$psm]
            }
        }
    }

    if ($scores.Count -gt 0) {
        $winner = $scores.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1
        $logs.Add(('winner={0}; score={1}' -f $winner.Key, $winner.Value))

        if ($DebugFile) {
            Write-Text ([IO.Path]::ChangeExtension($DebugFile, '.txt')) ($logs -join [Environment]::NewLine)
        }
        Write-Text $OutFile ([string]$winner.Key)
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        exit 0
    }

    if ($DebugFile) {
        Write-Text ([IO.Path]::ChangeExtension($DebugFile, '.txt')) ($logs -join [Environment]::NewLine)
    }
    Write-Text $OutFile 'ERROR:TESSERACT_NO_NUMBER'
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 2
}
catch {
    $message = $_.Exception.Message
    try {
        if ($DebugFile) {
            $details = 'ERROR:' + $message + [Environment]::NewLine + $_.InvocationInfo.PositionMessage
            Write-Text ([IO.Path]::ChangeExtension($DebugFile, '.txt')) $details
        }
        Write-Text $OutFile ('ERROR:' + $message)
    }
    catch { }
    exit 1
}
