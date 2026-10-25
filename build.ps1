# ============================================================================
#  构建脚本（纯 Win32 / 无第三方库）
#  用法：
#    .\build.ps1                 # 完整版客户端
#    .\build.ps1 -Lite           # 单机版（/DLOCAL_ONLY，无服务器相关页面）
#    .\build.ps1 -Config debug   # 调试版（/Od /Zi + PDB）
#    .\build.ps1 -Tests          # M1 公共库测试（控制台 m1_tests.exe）
#    .\build.ps1 -Lite -Run      # 构建后启动
#  依赖：..\toolchain\env.ps1（本机无 Windows SDK，用 NuGet 解压包；可用
#        USBCOPY_TOOLCHAIN 环境变量指定其他工具链目录）
# ============================================================================
param(
    [ValidateSet('release', 'debug')][string]$Config = 'release',
    [switch]$Lite,
    [switch]$Tests,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ---- 工具链 ----
$toolchain = $env:USBCOPY_TOOLCHAIN
if (-not $toolchain) { $toolchain = Join-Path (Split-Path $root -Parent) 'toolchain' }
$envScript = Join-Path $toolchain 'env.ps1'
if (-not (Test-Path $envScript)) {
    throw "找不到工具链环境脚本：$envScript（可用 USBCOPY_TOOLCHAIN 指定）"
}
. $envScript

# ---- 输出目录 ----
$suffix = if ($Tests -and $Lite) { '-tests-lite' } elseif ($Tests) { '-tests' } elseif ($Lite) { '-lite' } else { '' }
$objDir = Join-Path $root "out\obj\$Config$suffix"
$binDir = Join-Path $root "out\bin\$Config$suffix"
New-Item -ItemType Directory -Force -Path $objDir, $binDir | Out-Null

# ---- 编译选项（/WX：零警告是硬门槛）----
$cxxFlags = @(
    '/nologo', '/c', '/std:c++20', '/permissive-', '/W4', '/WX', '/utf-8', '/EHsc', '/MT', '/GR-',
    '/DUNICODE', '/D_UNICODE', '/DWIN32_LEAN_AND_MEAN', '/DNOMINMAX', '/D_WIN32_WINNT=0x0A00',
    "/I$root\src\common", "/I$root\src\client\app"
)
if ($Config -eq 'debug') {
    $cxxFlags += @('/Od', '/Zi', '/DDEBUG')
} else {
    $cxxFlags += @('/O2', '/GL')
}
if ($Lite) { $cxxFlags += '/DLOCAL_ONLY' }

# ---- 源文件 ----
$commonSources = @(
    'src\common\win_util.cpp',
    'src\common\path_util.cpp',
    'src\common\json.cpp',
    'src\common\xml_lite.cpp',
    'src\common\settings.cpp',
    'src\common\log.cpp',
    'src\common\file_filter.cpp',
    'src\common\name_util.cpp',
    'src\common\copy_callbacks.cpp'
)

if ($Tests) {
    $name = 'm1_tests'
    $subsystem = 'CONSOLE'
    $sources = $commonSources + @('src\tests\m1_tests.cpp')
    $libs = @('kernel32.lib', 'user32.lib', 'gdi32.lib', 'advapi32.lib', 'crypt32.lib',
              'shell32.lib', 'ole32.lib')
} else {
    $name = if ($Lite) { 'UsbCopyLite' } else { 'UsbCopy' }
    $subsystem = 'WINDOWS'
    $sources = $commonSources + @(
        'src\client\app\main.cpp',
        'src\client\app\MainWindow.cpp'
    )
    $libs = @('user32.lib', 'gdi32.lib', 'comctl32.lib', 'shell32.lib', 'ole32.lib', 'uuid.lib',
              'comdlg32.lib', 'advapi32.lib', 'crypt32.lib')
}

# ---- 编译 ----
$objects = @()
foreach ($src in $sources) {
    $leaf = [IO.Path]::GetFileNameWithoutExtension($src)
    $obj = Join-Path $objDir "$leaf.obj"
    Write-Host "[cc] $src"
    & cl.exe @cxxFlags "/Fo$obj" $src
    if ($LASTEXITCODE -ne 0) { throw "编译失败：$src" }
    $objects += $obj
}

# ---- 资源（图标 + 清单 + 版本信息；测试程序不需要）----
$resourceArgs = @()
if (-not $Tests) {
    Write-Host '[rc] src\client\res\app.rc'
    $res = Join-Path $objDir 'app.res'
    $resDir = Join-Path $root 'src\client\res'
    Push-Location $resDir
    try {
        & rc.exe /nologo "/fo$res" app.rc
        if ($LASTEXITCODE -ne 0) { throw '资源编译失败（app.rc）' }
    } finally {
        Pop-Location
    }
    $resourceArgs += $res
}

# ---- 链接 ----
$exe = Join-Path $binDir "$name.exe"
$ldFlags = @('/nologo', "/SUBSYSTEM:$subsystem", "/OUT:$exe")
if ($Config -eq 'debug') {
    $ldFlags += '/DEBUG'
} else {
    $ldFlags += @('/LTCG', '/OPT:REF', '/OPT:ICF')
}

Write-Host "[link] $exe"
& link.exe @ldFlags $objects @resourceArgs @libs
if ($LASTEXITCODE -ne 0) { throw '链接失败' }

Write-Host "[OK] $exe ($((Get-Item $exe).Length) 字节)"

if ($Run) {
    if ($Tests) {
        & $exe
        Write-Host "[exit] $LASTEXITCODE"
    } else {
        Start-Process -FilePath $exe | Out-Null
    }
}
