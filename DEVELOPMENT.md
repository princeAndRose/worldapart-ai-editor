# 从源码构建

Windows x64；系统自带 .NET Framework 4.x 编译器与 WPF。生成参考数据另需 Python 3 和 requirements-dev.txt 中的依赖。普通玩家运行工坊版 EXE 不需要这些开发依赖。

仓库不附带游戏表、头像、DLL 或存档。请从自己安装的游戏生成本地资源。下面命令在仓库根目录执行，路径按实际安装位置修改：

```powershell
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-dev.txt
.venv/Scripts/python extract-tables.py --game 'D:/Steam/steamapps/common/A1' --out local-data
.venv/Scripts/python build-catalog.py --game 'D:/Steam/steamapps/common/A1' --tables local-data --version 0.38.7394395
.venv/Scripts/python build-extended.py --game 'D:/Steam/steamapps/common/A1'
./build.ps1
```

结果在 `dist/WorldApart.AI.Editor.exe`。`catalog.json` 和 `portraits.zip` 为本地生成资源，编译时嵌入 EXE，Git 不跟踪它们。不要用新版本号为未经验证的数据强行声明兼容；提取成功不等于游戏内加载和请求行为已验证。

## 检查

```powershell
$editor = Join-Path $PWD 'dist/WorldApart.AI.Editor.exe'
$output = Join-Path $PWD 'test-output'
Start-Process $editor -ArgumentList ('--self-test "'+$output+'"') -Wait
$render = Join-Path $PWD 'render-output'
Start-Process $editor -ArgumentList ('--render-test "'+$render+'"') -Wait
```

数据检查应产生 PASSED.txt、DRAFT-PASSED.txt、EXTENDED-PASSED.txt、STUDIO-PASSED.txt、FEEDBACK-PASSED.txt；界面检查产生 UI-PASSED.txt。出现 FAILED.txt 时先查看错误。测试目录请使用新目录，避免把上次结果误认为本次通过。

`--ui-test` 用 EXE 旁的隔离目录及内存启用记录启动交互测试，不保存到真实游戏 Mod。正式模式写入用户自己的游戏 Mod 目录。

### 不需要游戏资源的代理回归

在 Windows PowerShell 中运行以下命令。它只编译代理相关源文件，在临时目录保存测试资料库，网络请求只发往测试创建的本机服务，不需要游戏、模型或密钥：

```powershell
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('worldapart-proxy-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$entry = Join-Path $testDir 'ProxyRunner.cs'
'class ProxyRunner { static void Main(string[] args) { WorldApartAI.StudioTests.RunProxyTests(args[0]); } }' | Set-Content -Encoding UTF8 $entry
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$runner = Join-Path $testDir 'ProxyRunner.exe'
& $compiler /nologo /target:exe /codepage:65001 /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$runner" Core.cs Extended.cs StudioData.cs ProxyServer.cs StudioTests.cs $entry
if ($LASTEXITCODE -ne 0) { throw 'Proxy test build failed' }
& $runner $testDir
if ($LASTEXITCODE -ne 0) { throw 'Proxy tests failed' }
Get-Content (Join-Path $testDir 'PROXY-PASSED.txt')
```

也可使用 Mono 的 C# 5 编译器编译相同文件和入口，以 `mono ProxyRunner.exe <新测试目录>` 运行。测试覆盖默认拒绝 LAN HTTP、标准 RFC1918 IPv4 范围、精确 IP/端口授权、旧配置兼容、保存/重载/撤销/恢复备份、重定向隔离，以及真实回环 TCP 转发、鉴权、错误、SSE 首块和改地址中断进行中的请求。测试不会连接示例中的局域网地址。

网络测试重启时使用新端口，避免 Mono/Linux 的 `ExclusiveAddressUse` 与 TCP `TIME_WAIT` 干扰；Windows 上停止后使用同一端口重启仍需单独检查。Linux 编译和回环测试不能代替 Windows WPF 渲染、实际游戏或局域网 LM Studio 验证。`RenderTests.cs` 已包含默认打开工作台、菜单首位、最小窗口及 LAN 授权变更/撤销检查；发布前还应在 Windows 下执行 `--render-test` 和 `--ui-test`，检查滚动区域与完整接入流程。

## 结构与贡献

- Core.cs / Extended.cs：参考数据、字段、表补丁、备份与事务保存。
- StudioData.cs / ProxyServer.cs：人物方案、资料库、条件匹配、受限本地 HTTP 代理与内存请求记录。
- StudioTests.cs：方案隔离、损坏恢复、规则预算、精确 LAN 授权与撤销、真实 TCP 转发、鉴权、重定向、错误返回、SSE 首块与停止重启测试（不连接外部模型）。
- Diagnostics.cs：只读生效检查（不读取模型密钥或聊天）。
- FeedbackTests.cs：仅背景角色、方案/预设范围、保留外部人设和占位 ID 排除检查。
- DraftState.cs：草稿、更改对照。
- Workspace.xaml / Workspace*.cs：原生 WPF 界面。
- *Tests.cs：事务、外部修改冲突、语言保留、预设合并及 WPF 渲染检查。

提交问题请附编辑器版本、游戏版本、复现步骤和错误文字。不要附模型密钥、完整聊天日志或存档。提交新版本兼容修改时，请说明表数据差异、变量变化和游戏内验证范围。

源码采用 MIT 许可证。游戏数据和头像不属于编辑器的开源授权范围；提取所用第三方库保留各自许可证。
