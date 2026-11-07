# Unity 에디터 창을 앞으로 가져오거나, 이전 창으로 포커스를 돌려놓는다.
# 에디터가 백그라운드면 플레이 모드가 멈추므로 실시간 검증 동안에만 쓴다 (DECISIONS.md D-015).
#
#   pwsh Tools/focus-window.ps1 -Unity            # Unity를 앞으로. 이전 창 핸들을 출력한다
#   pwsh Tools/focus-window.ps1 -Restore <핸들>   # 출력해 둔 핸들의 창으로 되돌린다
param(
    [switch]$Unity,
    [long]$Restore = 0,
    [string]$Project = 'Project_Brawl'
)

Add-Type -Namespace Knockbox -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool IsIconic(System.IntPtr h);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, System.IntPtr pid);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint to, bool on);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(System.IntPtr h);
[DllImport("user32.dll")] public static extern void SwitchToThisWindow(System.IntPtr h, bool altTab);
'@

function Test-Foreground([System.IntPtr]$handle) {
    Start-Sleep -Milliseconds 300
    return [Knockbox.Win32]::GetForegroundWindow() -eq $handle
}

function Invoke-Activate([System.IntPtr]$handle) {
    if ([Knockbox.Win32]::IsIconic($handle)) { [void][Knockbox.Win32]::ShowWindow($handle, 9) }  # SW_RESTORE
    # 1) Alt 키를 한 번 눌렀다 떼면 Windows의 포그라운드 잠금이 풀린다.
    [Knockbox.Win32]::keybd_event(0x12, 0, 0, [System.UIntPtr]::Zero)
    [Knockbox.Win32]::keybd_event(0x12, 0, 2, [System.UIntPtr]::Zero)
    [void][Knockbox.Win32]::SetForegroundWindow($handle)
    if (Test-Foreground $handle) { return $true }
    # 2) 지금 앞에 있는 창의 입력 스레드에 잠깐 붙어서 넘겨받는다(데스크톱 앱 안에서 실행될 때).
    $front = [Knockbox.Win32]::GetForegroundWindow()
    $frontThread = [Knockbox.Win32]::GetWindowThreadProcessId($front, [System.IntPtr]::Zero)
    $self = [Knockbox.Win32]::GetCurrentThreadId()
    [void][Knockbox.Win32]::AttachThreadInput($self, $frontThread, $true)
    [void][Knockbox.Win32]::BringWindowToTop($handle)
    [void][Knockbox.Win32]::SetForegroundWindow($handle)
    [void][Knockbox.Win32]::AttachThreadInput($self, $frontThread, $false)
    if (Test-Foreground $handle) { return $true }
    # 3) 작업 전환과 같은 방식.
    [Knockbox.Win32]::SwitchToThisWindow($handle, $true)
    return Test-Foreground $handle
}

if ($Restore -ne 0) {
    $ok = Invoke-Activate ([System.IntPtr]$Restore)
    if (-not $ok) { Write-Error "포커스 복원 실패: $Restore"; exit 1 }
    exit 0
}

if ($Unity) {
    $previous = [Knockbox.Win32]::GetForegroundWindow()
    $editor = Get-Process Unity -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -like "$Project - *" } |
        Select-Object -First 1
    if (-not $editor) { Write-Error "Unity 에디터 창을 찾을 수 없음: $Project"; exit 1 }
    $ok = Invoke-Activate $editor.MainWindowHandle
    if (-not $ok) { Write-Error 'Unity 창을 앞으로 가져오지 못함'; exit 1 }
    Write-Output $previous.ToInt64()
    exit 0
}

Write-Error '-Unity 또는 -Restore <핸들> 중 하나를 지정한다'
exit 2
