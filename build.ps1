#Requires -Version 5.1
<#
    构建脚本：把 src\ 下的 C# 源码编译成单文件 EXE（依赖系统自带 .NET Framework 4.8，无需安装运行时）。

    用法：
        powershell -ExecutionPolicy Bypass -File build.ps1                  # 生成图标 + 编译 + 跑逻辑/界面测试
        powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTest        # 只编译，不跑逻辑自测
        powershell -ExecutionPolicy Bypass -File build.ps1 -SkipUiTest      # 跳过界面测试（无人值守环境）
        powershell -ExecutionPolicy Bypass -File build.ps1 -Clean           # 清空 dist / obj 后重建

    产物：
        dist\桌面整理助手.exe          ← 单文件绿色版，双击即可用
        dist\使用说明.txt              ← 简要说明
#>
[CmdletBinding()]
param(
    [switch]$SkipTest,
    [switch]$SkipUiTest,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir   = Join-Path $root 'src'
$toolsDir = Join-Path $root 'tools'
$objDir   = Join-Path $root 'obj'
$distDir  = Join-Path $root 'dist'

function Write-Step($msg) { Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    $msg" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 清理
if ($Clean) {
    Write-Step '清理旧产物'
    foreach ($d in @($objDir, $distDir)) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    }
    Write-Ok '已清理'
}
foreach ($d in @($objDir, $distDir)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

# ---------------------------------------------------------------- 定位编译器
Write-Step '定位 .NET Framework 编译器'
$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw '找不到 csc.exe（.NET Framework 4.x）。请确认系统安装了 .NET Framework 4.x。'
}
Write-Ok "csc: $csc"

$fwDir = Split-Path -Parent $csc
$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll'
) | ForEach-Object {
    $p = Join-Path $fwDir $_
    if (-not (Test-Path $p)) { throw "缺少程序集：$p" }
    $p
}
Write-Ok ("引用程序集 " + $refs.Count + " 个")

# ---------------------------------------------------------------- 生成图标
Write-Step '生成应用图标'
$icoPath = Join-Path $objDir 'app.ico'
$iconGenExe = Join-Path $objDir 'IconGen.exe'
& $csc /nologo /target:exe /platform:anycpu /optimize+ /out:$iconGenExe `
    /r:System.Drawing.dll (Join-Path $toolsDir 'IconGen.cs') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '图标生成器编译失败' }
& $iconGenExe $icoPath | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $icoPath)) { throw '图标生成失败' }
Write-Ok ("app.ico " + [math]::Round((Get-Item $icoPath).Length / 1KB, 1) + " KB")

# ---------------------------------------------------------------- 编译主程序
Write-Step '编译主程序（WinForms，单文件）'
$sources = @(
    (Join-Path $srcDir 'Model.cs'),
    (Join-Path $srcDir 'Organizer.cs'),
    (Join-Path $srcDir 'MainForm.cs'),
    (Join-Path $srcDir 'Program.cs'),
    (Join-Path $srcDir 'AssemblyInfo.cs')
)
foreach ($s in $sources) { if (-not (Test-Path $s)) { throw "缺少源文件：$s" } }

$exePath = Join-Path $distDir '桌面整理助手.exe'
$appArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/debug-'
    "/out:$exePath"
    "/win32icon:$icoPath"
    "/win32manifest:" + (Join-Path $srcDir 'app.manifest')
)
$refs | ForEach-Object { $appArgs += "/r:$_" }
$sources | ForEach-Object { $appArgs += $_ }

& $csc @appArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }
if (-not (Test-Path $exePath)) { throw '未生成 exe' }

$exeInfo = Get-Item $exePath
Write-Ok ("dist\桌面整理助手.exe  " + [math]::Round($exeInfo.Length / 1KB, 1) + " KB")

# ---------------------------------------------------------------- 自测
if (-not $SkipTest) {
    Write-Step '运行逻辑自测（控制台版）'
    $testExe = Join-Path $objDir 'DeskTidyTests.exe'
    $testArgs = @(
        '/nologo'
        '/target:exe'
        '/platform:anycpu'
        '/optimize+'
        '/debug-'
        "/out:$testExe"
    )
    $refs | ForEach-Object { $testArgs += "/r:$_" }
    @('Model.cs', 'Organizer.cs', 'SelfTest.cs', 'TestMain.cs') | ForEach-Object {
        $testArgs += (Join-Path $srcDir $_)
    }

    & $csc @testArgs
    if ($LASTEXITCODE -ne 0) { throw '自测程序编译失败' }

    & $testExe
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) {
        throw "自测未通过（exit $testExit）"
    }
    Write-Ok '自测全部通过'
}

# ---------------------------------------------------------------- 界面测试
if (-not $SkipUiTest) {
    Write-Step '运行界面测试（布局 + 端到端流程）'
    $uiSources = @(
        (Join-Path $srcDir 'Model.cs'),
        (Join-Path $srcDir 'Organizer.cs'),
        (Join-Path $srcDir 'MainForm.cs')
    )

    foreach ($case in @(
        @{ Tool = 'UiSelfCheck.cs'; Exe = 'UiSelfCheck.exe'; Report = 'ui-layout.txt'; Label = '界面布局' },
        @{ Tool = 'UiFlowTest.cs';  Exe = 'UiFlowTest.exe';  Report = 'ui-flow.txt';   Label = '端到端流程' }
    )) {
        $uiExe = Join-Path $objDir $case.Exe
        $uiArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-', "/out:$uiExe")
        $refs | ForEach-Object { $uiArgs += "/r:$_" }
        $uiSources | ForEach-Object { $uiArgs += $_ }
        $uiArgs += (Join-Path $toolsDir $case.Tool)

        & $csc @uiArgs
        if ($LASTEXITCODE -ne 0) { throw "$($case.Label)测试程序编译失败" }

        $report = Join-Path $objDir $case.Report
        & $uiExe $report | Out-Null
        $uiExit = $LASTEXITCODE
        if ($uiExit -ne 0) {
            Write-Warn2 ("--- " + $case.Label + " 报告（" + $report + "）---")
            Get-Content $report -Encoding UTF8 | Write-Host
            throw "$($case.Label)测试未通过（exit $uiExit）"
        }
        Write-Ok ($case.Label + '测试通过')
    }
}

# ---------------------------------------------------------------- 使用说明
Write-Step '生成使用说明'
$readme = Join-Path $distDir '使用说明.txt'
@'
桌面整理助手 v1.0
======================================================================

【它做什么】
  把桌面上的“新文件”一次性收进一个以今天日期命名的文件夹
  （例如 2025-03-09），让桌面重新变干净。
  · 只处理桌面第一层的文件，文件夹一律不动，文件夹里的东西也不动。
  · 每天固定要用的文件不会被收走——用“快照”把它们固定住。

【三步上手】
  1. 双击 桌面整理助手.exe 打开界面。
  2. 第一次用：切到「固定文件快照」标签，点「建立 / 更新快照」。
     此刻桌面上所有文件都会被记为“固定”，以后不会被打包。
     之后每天新出现的文件就在快照之外，会被打包。
  3. 回到「整理预览」，确认勾选状态，点右下角「一键整理」。

【界面说明】
  · 整理预览
     列出桌面上每个文件，勾选框决定它是否被打包。
     可点「全部勾选 / 全部取消 / 恢复自动判断」批量调整，
     也可以在右侧直接改单个文件的勾选。
     状态列说明该文件的处理方式：
       打包         —— 会被移入日期文件夹
       保留（快照） —— 命中快照，留在桌面（也可在预览里手动勾上强行打包）
       保留（规则） —— 命中排除规则，留在桌面
       文件夹跳过   —— 永远不会被移动
  · 固定文件快照
     「建立 / 更新快照」把当前桌面全部文件记为固定；
     「把当前桌面全部文件设为固定」效果相同；
     「删除快照」清空，之后桌面上所有文件都会被视为可打包。
     若某个固定文件内容被改过（大小或修改时间变了）：
       宽松模式（默认）—— 仍按文件名保护，留在桌面；
       严格模式         —— 视为新文件，允许打包。
  · 规则与设置
     排除文件名规则：一行一条，支持 * 和 ? 通配，例如  *.tmp  或  ~$*
     排除扩展名：    逗号分隔，例如  .lnk, .exe
     日期文件夹格式： 默认 yyyy-MM-dd，可改成 yyyy年MM月dd日 等
     “同一天多次整理合并到同一文件夹”：勾上则重复使用当天目录，
                                     不勾则每次新建 2025-03-09 (1)。
  · 整理记录 / 撤销
     每次整理都会留一条记录，选中后点「撤销选中的整理」即可把文件原样搬回桌面。
     撤销时若桌面已有同名文件，会自动改名成 “xxx (1).txt”，不会覆盖。
  · 底部「先试运行」：勾上后点整理只做演算，一个文件都不移动。

【数据存放在哪】
  %AppData%\DeskTidy\
      config.json     设置
      snapshot.json   快照
      logs\           每次整理与撤销的记录
  程序不写注册表，不联网，删掉 exe 就等于卸载。
  想换电脑继续用，把上面这个文件夹一起拷走即可。

【安全说明】
  · 所有移动都是同盘移动（桌面 → 桌面子文件夹），不复制、不删除文件。
  · 每一步都有记录，支持撤销。
  · 建议第一次使用前先勾「先试运行」看一眼效果。

【常见问题】
  Q: 双击没反应 / 报错缺少 .NET Framework？
  A: 本程序需要 .NET Framework 4.5 或更高（Win10/11 自带 4.8），
     极老的系统需要先安装 .NET Framework。

  Q: 新下载的文件没被打包？
  A: 检查它是不是命中了快照或排除规则，在「整理预览」里看“状态”列。

  Q: 想让某个文件重新被打包？
  A: 在「整理预览」里直接勾上它，再点一键整理。
'@ | Set-Content -Path $readme -Encoding UTF8
Write-Ok 'dist\使用说明.txt'

# ---------------------------------------------------------------- 汇总
Write-Step '完成'
Write-Host ("    输出目录：" + $distDir)
Get-ChildItem $distDir | ForEach-Object {
    Write-Host ("      " + $_.Name + "  (" + [math]::Round($_.Length / 1KB, 1) + " KB)")
}
