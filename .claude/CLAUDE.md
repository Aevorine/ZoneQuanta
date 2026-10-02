# ZoneQuanta — 项目规则
> 只放本项目专属且稳定的内容。通用工作方式在 `~/.claude/CLAUDE.md`，不要复读。现在做到哪 → `.claude/SPEC.md`；背景 → `README.md`。

## 这是什么
Windows 桌面时钟组件（本地 + 洛杉矶时间），WPF / .NET 8，单文件 exe，GitHub Releases 自更新。架构与特性见 `README.md`，选型见 `docs/adr/0001-gui-stack.md`。

## 命令
- 构建：`dotnet build src/ZoneQuanta -c Release`
- 发布：`pwsh scripts/release.ps1 -Version X.Y.Z -Notes "…"`（需环境变量 `ZONEQUANTA_SECRET_DIR` 指向签名密钥目录；构建自带运行时的单文件→签名→建 Release→只留最近两版→清理本地产物）
- 生成签名密钥：`scripts/New-SigningKey.ps1`（只在换密钥时用；换密钥后旧版本无法再自更新）
- 重新生成图标：`scripts/New-Icon.ps1`

## 约束
- 界面不写说明 / 提示文字；所有页面共用 `UI/Theme/Theme.xaml` 的样式与调色板，不要在页面里写死颜色
- 签名私钥与密码只在仓库之外（`ZONEQUANTA_SECRET_DIR`），公钥在 `Core/Update/UpdateKey.cs`
- `Core` 不依赖 `UI`；Win32 调用只放 `Platform`
- 只发布自带运行时的单文件 exe；`Flavor` 属性仍保留（旧版轻量版客户端的更新清单同时提供 lite 与 full 两个键，指向同一文件）

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues in this repo's GitHub Issues, driven through the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` plus `docs/adr/` at the repo root. See `docs/agents/domain.md`.
