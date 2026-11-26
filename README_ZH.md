# KeyStats for Windows

一个轻量级的 Windows 键鼠统计工具。统计每日按键次数、鼠标点击次数、鼠标移动距离和滚动距离 —— **只记数量，不记内容**。

> **仅支持 Windows。** 本仓库只包含 Windows 实现（WPF + .NET Framework 4.8），上游的 macOS / Linux 源码**不在本仓库内**，也不针对这些平台构建或测试。

## 功能

- 全局输入统计：按键次数、点击次数（左 / 右 / 中 / 侧键）、鼠标移动距离、滚动距离
- 键盘热力图、按应用过滤、小时趋势
- 《英雄联盟》对局追踪 + 英雄自动识别（走 LCU 接口，不注入游戏）
- 统计报告：周 / 月环比、Top 键、手指移动公里数
- 纯离线，数据只存本机 `%LOCALAPPDATA%\KeyStats\`

## 运行环境

- Windows 10 (1903+) 或 Windows 11
- .NET Framework 4.8（Win10 1903+ 自带）

## 构建

```
dotnet build KeyStats.Windows/KeyStats/KeyStats.csproj -c Release -o dist/KeyStats-Portable
```

运行 `dist/KeyStats-Portable/KeyStats.exe`。

## 文档

| 文件 | 内容 |
|---|---|
| [KeyStats.Windows/README.md](./KeyStats.Windows/README.md) | 功能与使用说明 |
| [QUICKSTART.md](./QUICKSTART.md) | 快速开始 |
| [NOTICE.md](./NOTICE.md) | 本修改版的改动、上游署名、隐私说明 |

## 许可证

MIT —— 见 [LICENSE](./LICENSE)。

上游项目：[keyStats](https://github.com/debugtheworldbot/keyStats)，Copyright (c) 2026 pipizhu。
本仓库是非官方的 Windows 专用修改版。
