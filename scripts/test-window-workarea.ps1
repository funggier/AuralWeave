param(
    [string]$ExecutablePath = ""
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (!$ExecutablePath) {
    $ExecutablePath = Join-Path $root 'dist\AuralWeave-win-x64\AuralWeave.exe'
}
if (!(Test-Path -LiteralPath $ExecutablePath)) {
    throw "Desktop executable not found: $ExecutablePath"
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class AuralWeaveChromeTestWin32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
'@

$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
$process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
try {
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) {
        throw "AuralWeave UI failed to start"
    }

    $hwnd = [IntPtr]$process.MainWindowHandle
    $rootWindow = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $tree = [System.Windows.Automation.TreeScope]::Descendants
    $nameKey = [System.Windows.Automation.AutomationElement]::NameProperty
    $caption = $rootWindow.FindFirst($tree,
        (New-Object System.Windows.Automation.PropertyCondition(
            $nameKey, 'Maximize or restore')))
    $minimize = $rootWindow.FindFirst($tree,
        (New-Object System.Windows.Automation.PropertyCondition($nameKey, 'Minimize')))
    $close = $rootWindow.FindFirst($tree,
        (New-Object System.Windows.Automation.PropertyCondition($nameKey, 'Close')))
    if (!$caption -or !$minimize -or !$close) {
        throw "A custom window caption control is missing"
    }
    $maximize = $caption.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)

    $screens = [System.Windows.Forms.Screen]::AllScreens
    $tested = 0
    foreach ($screen in $screens) {
        # Move the NORMAL window to the center of each monitor, including
        # monitors whose desktop coordinates are nonzero or negative.
        $x = $screen.Bounds.Left + [int](($screen.Bounds.Width - 1000) / 2)
        $y = $screen.Bounds.Top + [int](($screen.Bounds.Height - 720) / 2)
        if (![AuralWeaveChromeTestWin32]::SetWindowPos(
            $hwnd, [IntPtr]::Zero, $x, $y, 1000, 720, 0x0014)) {
            throw "Unable to position window on $($screen.DeviceName)"
        }
        Start-Sleep -Milliseconds 150
        $target = [System.Windows.Forms.Screen]::FromHandle($hwnd)
        if ($target.DeviceName -ne $screen.DeviceName) {
            throw "Window did not move to requested monitor $($screen.DeviceName)"
        }
        $maximize.Invoke()
        Start-Sleep -Milliseconds 250

        $rect = New-Object AuralWeaveChromeTestWin32+RECT
        if (![AuralWeaveChromeTestWin32]::GetWindowRect($hwnd, [ref]$rect)) {
            throw "GetWindowRect failed"
        }
        $work = $screen.WorkingArea
        if ($rect.Left -ne $work.Left -or $rect.Top -ne $work.Top -or
            $rect.Right -ne $work.Right -or $rect.Bottom -ne $work.Bottom) {
            throw "Maximized window $($screen.DeviceName) lies outside work area: $($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom); expected $work"
        }

        foreach ($button in @($caption, $minimize, $close)) {
            $bounds = $button.Current.BoundingRectangle
            $cx = $bounds.X + $bounds.Width / 2
            $cy = $bounds.Y + $bounds.Height / 2
            if ($cx -lt $work.Left -or $cx -ge $work.Right -or
                $cy -lt $work.Top -or $cy -ge $work.Bottom) {
                throw "Caption button '$($button.Current.Name)' is hidden on $($screen.DeviceName): $bounds"
            }
        }

        $maximize.Invoke()
        Start-Sleep -Milliseconds 180
        $state = $rootWindow.GetCurrentPattern(
            [System.Windows.Automation.WindowPattern]::Pattern
        ).Current.WindowVisualState
        if ($state -ne [System.Windows.Automation.WindowVisualState]::Normal) {
            throw "Restore failed on $($screen.DeviceName): $state"
        }
        $tested++
        Write-Output "PASS: $($screen.DeviceName) maximize fits work area and all caption controls are accessible; restore works"
    }

    $minimize.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
    $minState = $rootWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern
    ).Current.WindowVisualState
    if ($minState -ne [System.Windows.Automation.WindowVisualState]::Minimized) {
        throw "Minimize control did not minimize: $minState"
    }
    [void][AuralWeaveChromeTestWin32]::ShowWindow($hwnd, 9)
    Start-Sleep -Milliseconds 200
    $restoredState = $rootWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern
    ).Current.WindowVisualState
    if ($restoredState -ne [System.Windows.Automation.WindowVisualState]::Normal) {
        throw "Cannot restore minimized window: $restoredState"
    }
    $close.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (!$process.WaitForExit(4500)) {
        throw "Close control did not terminate the application"
    }
    Write-Output "PASS: minimize, restore, and close window controls"
    Write-Output "ALL CUSTOM CHROME TESTS PASSED on $tested monitor(s)"
}
finally {
    $process.Refresh()
    if (!$process.HasExited) {
        [void]$process.CloseMainWindow()
        if (!$process.WaitForExit(2500)) {
            Stop-Process -Id $process.Id -Force
        }
    }
}
