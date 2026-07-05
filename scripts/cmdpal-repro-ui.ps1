# UI repro runner — navigation + log verification + screenshot capture.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CmdPalKeySend
{
    private const int KEYEVENTF_KEYUP = 0x0002;
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public static void WinAltSpace()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x20, 0, 0, UIntPtr.Zero);
        keybd_event(0x20, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(0x5B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
"@

$logPath = Join-Path $env:LOCALAPPDATA 'QuickShell\cmdpal-repro.log'
$shotDir = Join-Path $env:TEMP 'cmdpal-repro-shots'
New-Item -ItemType Directory -Path $shotDir -Force | Out-Null
if (Test-Path $logPath) { Remove-Item $logPath -Force }

function Send-Hotkey { [CmdPalKeySend]::WinAltSpace() }
function Type-Text { param([string]$Text) [System.Windows.Forms.SendKeys]::SendWait($Text) }
function Press-Enter { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}') }
function Press-Tab { [System.Windows.Forms.SendKeys]::SendWait('{TAB}') }
function Press-Down { [System.Windows.Forms.SendKeys]::SendWait('{DOWN}') }
function Press-Esc { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') }
function Press-Right { [System.Windows.Forms.SendKeys]::SendWait('{RIGHT}') }

function Save-Screenshot {
    param([string]$Name)
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $path = Join-Path $shotDir "$Name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    return $path
}

function Open-CmdPal { Send-Hotkey; Start-Sleep -Milliseconds 900 }

function Open-PageBySearch {
    param([string]$Query)
    Open-CmdPal
    Type-Text $Query
    Start-Sleep -Milliseconds 900
    Press-Enter
    Start-Sleep -Seconds 2
}

$cmdpalExe = 'A:\PowerToys\x64\Debug\WinUI3Apps\CmdPal\Microsoft.CmdPal.UI.exe'
Get-Process Microsoft.CmdPal.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
Start-Process -FilePath $cmdpalExe -WorkingDirectory (Split-Path $cmdpalExe -Parent)
Start-Sleep -Seconds 3

Write-Host 'Reload extension...' -ForegroundColor Cyan
Open-PageBySearch 'Reload Command Palette Extension'
Start-Sleep -Seconds 2

Write-Host '=== Issue 1 ===' -ForegroundColor Cyan
Open-PageBySearch 'Repro when DataJson refresh'
Save-Screenshot 'issue1-before' | Out-Null
Press-Tab; Start-Sleep -Milliseconds 120
Press-Down; Start-Sleep -Milliseconds 120
Press-Down; Start-Sleep -Milliseconds 120
Press-Enter; Start-Sleep -Milliseconds 300
for ($i = 0; $i -lt 8; $i++) { Press-Tab; Start-Sleep -Milliseconds 100 }
Press-Enter
Start-Sleep -Seconds 2
$shot1 = Save-Screenshot 'issue1-after-apply'
Write-Host "Screenshot: $shot1"

Write-Host '=== Issue 2 ===' -ForegroundColor Cyan
Press-Esc; Start-Sleep -Milliseconds 400
Open-PageBySearch 'Repro changeAction ChoiceSet'
Save-Screenshot 'issue2-before' | Out-Null
for ($i = 0; $i -lt 3; $i++) { Press-Tab; Start-Sleep -Milliseconds 100 }
Press-Down; Start-Sleep -Milliseconds 120
Press-Down; Start-Sleep -Milliseconds 120
Press-Enter
Start-Sleep -Seconds 2
$shot2 = Save-Screenshot 'issue2-after-pick'
Write-Host "Screenshot: $shot2"

Write-Host "`nLog contents:" -ForegroundColor Yellow
if (Test-Path $logPath) { Get-Content $logPath } else { Write-Host '(no log — SubmitForm may not have fired)' }

Write-Host "`nScreenshots: $shotDir" -ForegroundColor DarkGray
Press-Esc
