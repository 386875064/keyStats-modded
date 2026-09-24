[![Awesome](https://awesome.re/mentioned-badge.svg )](https://github.com/jaywcjlove/awesome-mac#menu-bar-tools)

English | [简体中文](./README_ZH.md)

---

# ⚠️ 非官方修改版 / Unofficial modified build

这是 [keyStats](https://github.com/debugtheworldbot/keyStats) 的**第三方修改版（fork）**。
**与官方版本无关，原作者 pipizhu 不提供支持或背书。**

| | |
|---|---|
| 上游项目 | [debugtheworldbot/keyStats](https://github.com/debugtheworldbot/keyStats) |
| 原作者 | pipizhu（MIT License，见 [`LICENSE`](./LICENSE)） |
| 改造起点 | 上游 1.52 版源码 |
| 本版本 | 1.72（仅 Windows） |

> 想要官方版本、官方更新、macOS 版本 → 请前往上游仓库。

## 为什么改

我平时用 KeyStats 记录自己的键鼠数据，玩《英雄联盟》时发现它统计不到游戏里的操作。
翻了自己 205 天的历史记录，是这么回事：

| 进程 | 记录情况 |
|---|---|
| `LeagueClientUx`（游戏客户端） | 每天有 1~15 次点击被记录 —— 正常 |
| `League of Legends`（游戏本体） | **按键数恒为 0**，只有零星 1~6 次点击 |

进了对局之后，键盘钩子基本收不到任何事件。原因是游戏进程（国服是 TP / ACE 反作弊）
在运行期间会摘除第三方的全局低级钩子（`SetWindowsHookEx`）—— 这在反作弊里很常见。
所以不是"没识别到游戏"，而是"钩子被拆了"。上游短期不会处理，就自己补了一版。

## 改了什么

只改了 Windows（.NET Framework 4.8 + WPF）部分，**macOS（Swift）代码一行都没动**。

| | 改动 |
|---|---|
| 🎮 **游戏对局统计** | 自动识别对局、逐局落盘；**双采集通道**：Raw Input（`RIDEV_INPUTSINK`）为主 + 低级钩子兜底，反作弊摘钩子也不影响；实时 APM、峰值 KPM/CPM、本局按键分布 |
| 🖼 **游戏内 HUD** | 游戏进行中的置顶小浮窗（时长 \| APM \| 按键 \| 点击），可拖动锁定；不受上游「全屏自动隐藏浮窗」逻辑影响 |
| 📈 **对局详情** | 双击历史里一局查看：每分钟操作时间轴柱状图 + 本局键盘热力图 |
| 📊 **使用洞察** | 退格率（打字准确度）、修饰键使用量、应用切换与最长连续专注、24 小时活跃分布、前台时间 Top 8、久坐提醒 |
| 🔌 **纯离线** | 移除云端同步入口且启动时不实例化同步服务；停用分析上报并清除上游硬编码的 key。**实测运行期间无任何 TCP/UDP 连接** |
| 🩺 **采集自检** | 显示当前用的是哪条通道、钩子被重建过几次 —— 便于排查"为什么统计不到" |

### 数据关系（改动前请先看这张表）

| 数据层 | 文件 | 游戏期间是否计数 |
|---|---|---|
| 使用洞察（工作活跃） | `insights.json` | 不计数（游戏不算"工作"） |
| 每日总量 / 分应用 | `daily_stats.json` | 计数 |
| 对局记录（明细） | `game_sessions.json` | 计数（独立） |

游戏里的操作默认**同时计入**每日总量（否则"今天敲了多少键"会缺一大块）；对局记录始终独立。
任何时刻只有一条计数路径，不会重复计数。不想让游戏数据进总量，可在「游戏统计」窗口里关掉。

完整来源声明与改动清单见 [`NOTICE.md`](./NOTICE.md)。

## 关于作者（说实话）

免得你有过高的期待，先把话讲清楚：

- **我不是专业开发者。** 我没学过编程。只是天天用这个软件，觉得少了几样自己需要的功能。
- **代码不是我写的。** 全部改动是在 AI 助手（WorkBuddy）协助下完成的 —— 我描述需求、反馈现象，
  它来读源码、改代码、编译、测试。整个过程我就是个提需求和按"继续"的人。
- **我能保证的**：它在我自己的 Windows 机器上跑通了，关键路径都有实测数据
  （比如游戏内注入 40 次按键，对局记录里就是 40 次，一次不差）。
- **我不能保证的**：代码质量、性能、安全性达到专业水准。我看不懂大部分改动内容。
- **不支持、不背锅**。提 Issue 我大概率也看不懂，但会转给 AI 一起研究。用不用请自行判断。

如果你是开发者，看到某些实现觉得别扭 —— 那很正常，欢迎指正，我拿去问 AI 改。

## 怎么用

1. 退出正在运行的官方版 KeyStats（托盘图标右键 → 退出）
2. 从 [Releases](../../releases) 下载 `keyStats-modded-*.zip`，解压到固定目录
3. 双击 `KeyStats.exe`

| 入口 | 位置 |
|---|---|
| 游戏统计 | 托盘右键 → 游戏统计（双击某局看时间轴与热力图） |
| 使用洞察 | 托盘右键 → 使用洞察 |
| 游戏内 HUD | 托盘右键 → 游戏内 HUD（默认开） |
| 主面板 | 托盘图标左键 |

**如果游戏里统计不到**：先看「游戏统计」窗口顶部的「采集状态」，
它会直接告诉你用的哪条通道、钩子被重建过几次。

| 已知问题 | 说明 |
|---|---|
| 独占全屏下没有 HUD | Windows 限制，任何置顶窗口都一样；统计不受影响。想看到就改「无边框窗口」 |
| 只认英雄联盟 | 游戏列表写死在 `Services/GameProfile.cs`，加游戏需改代码 |
| 连 Raw Input 都被拦 | 极少见，但真发生了就没辙 |

## 自己编译（Windows）

需要 .NET SDK 8.0（.NET Framework 4.8 是 Windows 自带的运行时）：

```bash
cd KeyStats.Windows
dotnet build KeyStats/KeyStats.csproj -c Release -o dist/KeyStats
```

## 免责声明

本修改版由第三方维护，**与原作者无关**。软件按 MIT 条款以「原样」提供，不附带任何担保。
使用本软件（尤其是涉及游戏内运行时的采集行为）的风险由使用者自行承担。

本软件只保存**聚合计数**（每日按键次数、各按键累计次数、鼠标点击次数、鼠标移动距离），
**不记录**输入的文字内容、按键顺序或鼠标光标坐标。所有数据默认只保存在本机。

## 许可与署名

原始项目 Copyright (c) 2026 pipizhu，MIT License。
本仓库完整保留原始 [`LICENSE`](./LICENSE)，未做任何删改。详见 [`NOTICE.md`](./NOTICE.md)。

---

*下面是上游项目的原始说明 / The original upstream README follows.*

---

<img width="128" height="128" alt="ICON-iOS-Default-256x256@2x" src="https://github.com/user-attachments/assets/842780ed-c7a1-4c1b-a901-1f1d8babe51a" />


# KeyStats - macOS/Windows Keyboard & Mouse Statistics Menu Bar App

KeyStats is a lightweight native menu bar application for macOS and Windows that tracks daily keyboard keystrokes, mouse clicks, mouse movement distance, and scroll distance, with optional end-to-end encrypted multi-device sync.

<img width="305" height="632" alt="image" src="https://github.com/user-attachments/assets/85c0b483-ad4a-458c-8bf4-c4f054b951bb" />

<img width="320" height="581" alt="image" src="https://github.com/user-attachments/assets/b363093e-9aad-4d8a-8b12-1918ef843b3f" />

<img width="1920" height="816" alt="image" src="https://github.com/user-attachments/assets/7f3e93bf-57de-415d-857f-7416efa67eda" />
<img width="1304" height="966" alt="image" src="https://github.com/user-attachments/assets/79dd41e4-94fc-46ef-826d-160a4b75ad35" />



## Installation & Usage

### macOS

#### Option 1: Install via Homebrew

```bash
# Tap the repository
brew tap debugtheworldbot/keystats

# Install the app
brew install keystats
```

Update the app:
```bash
brew upgrade keystats
```

#### Option 2: [Download from GitHub Releases](https://github.com/debugtheworldbot/keyStats/releases)

### Windows

#### Option 1: Install via Scoop

```bash
scoop bucket add keystats https://github.com/debugtheworldbot/scoop-keystats
scoop install keystats
```

#### Option 2: [Download from GitHub Releases](https://github.com/debugtheworldbot/keyStats/releases)

> **No dependencies required**: The Windows version uses .NET Framework 4.8, which is pre-installed on Windows 10 (1903+) and Windows 11 - ready to use out of the box. If your Windows 10 version is older (before 1903), you can upgrade your system or [manually install .NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).

### Linux

[Download the Linux version](https://github.com/0x5c0f/keyStats/releases)

## Features

- **Keyboard Keystroke Statistics**: Real-time tracking of daily key presses
- **Mouse Click Statistics**: Separate tracking of left and right clicks
- **Mouse Movement Distance**: Track total distance of mouse movement
- **Scroll Distance Statistics**: Record cumulative page scroll distance
- **Menu Bar Display**: Core data displayed directly in macOS menu bar
- **Detailed Panel**: Click menu bar icon to view complete statistics
- **Daily Auto-Reset**: Statistics automatically reset at midnight
- **Data Persistence**: Data persists after application restart
- **Multi-Device Sync**: End-to-end encrypted synchronization of supported daily keyboard and mouse-click statistics across paired devices

## System Requirements

### macOS
- macOS 13.0 (Ventura) or higher

### Windows
- **Windows 10 (1903+) or Windows 11**
- **No dependencies required**: Uses .NET Framework 4.8 (pre-installed on Windows 10/11, ready to use out of the box)
- **App size**: ~5-10 MB (lightweight, no additional runtime needed)

> **Note**: If your Windows 10 version is older (before 1903), you can:
> 1. Upgrade to Windows 10 1903 or higher (recommended)
> 2. Or manually install .NET Framework 4.8: [Download link](https://dotnet.microsoft.com/download/dotnet-framework/net48)


## First Run Permission Setup

### macOS

KeyStats requires **Accessibility permissions** to monitor keyboard and mouse events. On first run:

1. The app will prompt a permission request dialog
2. Click "Open System Settings"
3. Find KeyStats in "Privacy & Security" > "Accessibility"
4. Enable the permission toggle for KeyStats
5. Once authorized, the app will automatically start tracking

> **Note**: Without granting permissions, the app will not be able to track any data.
>
> **Reinstall/upgrade tip**: Because the app is not signed, macOS will not automatically update Accessibility authorization after each reinstall. Remove the existing KeyStats entry in "Privacy & Security" > "Accessibility", then return to the app and click the "Get Permission" button to request access again.
>
> **Auto-update tip (unsigned builds, macOS Ventura+)**: The first Sparkle update may fail. Enable KeyStats in "Privacy & Security" > "App Management" first. If it already failed, click the system authorization notification, turn on the toggle in Settings, and click "Update Now" again.

### Windows

The Windows version **requires no additional permission setup**. The app will automatically start tracking once launched.

> **Note**: On first launch, Windows may show a security warning. Click "Run anyway" to proceed.

## Privacy Statement

KeyStats stores aggregate statistics and **does NOT record**:
- Typed text or the order of keystrokes
- Specific cursor or click locations

Data stays on the local device by default. When you explicitly enable multi-device sync, supported daily key-press totals, per-key/key-combination counts, mouse-button click counts, and encrypted device information are encrypted locally before upload. Mouse movement distance, scroll distance, and per-app statistics are not currently synced, and the service cannot read the encrypted statistics.

## License

MIT License
