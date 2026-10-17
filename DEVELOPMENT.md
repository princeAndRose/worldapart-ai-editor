# 从源码构建

Windows x64；系统自带 .NET Framework 4.x 编译器与 WPF。生成参考数据另需 Python 3 和 requirements-dev.txt 中的依赖。普通玩家运行工坊版 EXE 不需要这些开发依赖。

仓库不附带游戏表、头像、DLL 或存档。请从自己安装的游戏生成本地资源。下面命令在仓库根目录执行，路径按实际安装位置修改：

```powershell
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-dev.txt
.venv/Scripts/python extract-tables.py --game 'D:/Steam/steamapps/common/A1' --out local-data
.venv/Scripts/python build-catalog.py --game 'D:/Steam/steamapps/common/A1' --tables local-data --version 0.35.7370692
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

## 结构与贡献

- Core.cs / Extended.cs：参考数据、字段、表补丁、备份与事务保存。
- StudioData.cs / ProxyServer.cs：人物方案、资料库、条件匹配、受限本地 HTTP 代理与内存请求记录。
- StudioTests.cs：方案隔离、损坏恢复、规则预算、真实 TCP 转发、鉴权、错误返回、SSE 首块与停止重启测试（不连接外部模型）。
- Diagnostics.cs：只读生效检查（不读取模型密钥或聊天）。
- FeedbackTests.cs：仅背景角色、方案/预设范围、保留外部人设和占位 ID 排除检查。
- DraftState.cs：草稿、更改对照。
- Workspace.xaml / Workspace*.cs：原生 WPF 界面。
- *Tests.cs：事务、外部修改冲突、语言保留、预设合并及 WPF 渲染检查。

提交问题请附编辑器版本、游戏版本、复现步骤和错误文字。不要附模型密钥、完整聊天日志或存档。提交新版本兼容修改时，请说明表数据差异、变量变化和游戏内验证范围。

源码采用 MIT 许可证。游戏数据和头像不属于编辑器的开源授权范围；提取所用第三方库保留各自许可证。
