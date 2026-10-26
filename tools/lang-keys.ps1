<#
.SYNOPSIS
  Собирает language/ru.json из текстов в коде и проверяет, что в остальных языковых пакетах есть все ключи.

.DESCRIPTION
  Каждый текст программы записан в коде как L.T("ключ", "русский текст") или L.F("ключ", "русский текст {0}", …).
  Скрипт находит все такие вызовы, пишет language/ru.json (он всегда совпадает со встроенным русским текстом)
  и для каждого другого файла language/*.json перечисляет недостающие и лишние ключи.

  Запуск из корня репозитория:
    powershell -ExecutionPolicy Bypass -File tools\lang-keys.ps1
  Код выхода 1 — один ключ записан с разными текстами или в переводах не хватает ключей.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'MacroPadStudio'
$languages = Join-Path $root 'language'

function Unescape([string] $s) {
    [regex]::Replace($s, '\\(u[0-9A-Fa-f]{4}|.)', {
        param($m)
        $c = $m.Groups[1].Value
        switch -CaseSensitive ($c) {
            'n' { "`n" }
            'r' { "`r" }
            't' { "`t" }
            '0' { [string][char]0 }
            default { if ($c.Length -eq 5) { [string][char][Convert]::ToInt32($c.Substring(1), 16) } else { $c } }
        }
    })
}

function JsonString([string] $s) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $s.ToCharArray()) {
        switch ($ch) {
            '"' { [void]$sb.Append('\"') }
            '\' { [void]$sb.Append('\\') }
            "`n" { [void]$sb.Append('\n') }
            "`r" { [void]$sb.Append('\r') }
            "`t" { [void]$sb.Append('\t') }
            default {
                if ([int]$ch -lt 32) { [void]$sb.Append(('\u{0:x4}' -f [int]$ch)) } else { [void]$sb.Append($ch) }
            }
        }
    }
    [void]$sb.Append('"')
    $sb.ToString()
}

# L.T("ключ", "текст") и L.F("ключ", "текст", …); между аргументами может быть перенос строки.
$pattern = [regex]'\bL\.[TF]\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"'
$strings = @{}
$conflicts = @()
# SelfTest.cs проверяет сам механизм перевода своими тестовыми ключами — в языковые пакеты они не попадают.
$files = Get-ChildItem -Path $source -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.Name -ne 'SelfTest.cs' }
foreach ($file in $files) {
    # Строки-комментарии пропускаем: в них бывают примеры вызовов.
    $code = [System.IO.File]::ReadAllLines($file.FullName) | Where-Object { -not $_.TrimStart().StartsWith('//') }
    $text = $code -join "`n"
    foreach ($m in $pattern.Matches($text)) {
        $key = Unescape $m.Groups[1].Value
        $value = Unescape $m.Groups[2].Value
        if ($strings.ContainsKey($key) -and $strings[$key] -ne $value) {
            $conflicts += "  $key  ($($file.Name)): «$($strings[$key])» ≠ «$value»"
        } else {
            $strings[$key] = $value
        }
    }
}

$keys = $strings.Keys | Sort-Object -CaseSensitive
$lines = foreach ($key in $keys) { '    ' + (JsonString $key) + ': ' + (JsonString $strings[$key]) }
$json = "{`n  `"code`": `"ru`",`n  `"name`": `"Русский`",`n  `"strings`": {`n" + ($lines -join ",`n") + "`n  }`n}`n"
New-Item -ItemType Directory -Force -Path $languages | Out-Null
[System.IO.File]::WriteAllText((Join-Path $languages 'ru.json'), $json, (New-Object System.Text.UTF8Encoding $false))
Write-Host "ru.json: $($keys.Count) ключей"

$failed = $false
if ($conflicts.Count -gt 0) {
    Write-Host "Один ключ — разные тексты:" -ForegroundColor Red
    $conflicts | ForEach-Object { Write-Host $_ }
    $failed = $true
}

foreach ($file in Get-ChildItem -Path $languages -Filter *.json | Where-Object { $_.Name -ne 'ru.json' }) {
    $pack = [System.IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
    $have = @($pack.strings.PSObject.Properties.Name)
    $missing = @($keys | Where-Object { $have -notcontains $_ })
    $extra = @($have | Where-Object { -not $strings.ContainsKey($_) })
    Write-Host "$($file.Name): $($have.Count) ключей, не хватает $($missing.Count), лишних $($extra.Count)"
    $missing | ForEach-Object { Write-Host "  + $_ = $(JsonString $strings[$_])" }
    $extra | ForEach-Object { Write-Host "  - $_" }
    if ($missing.Count -gt 0) { $failed = $true }
}

if ($failed) { exit 1 }
