# KeyStats for Windows · 快速开始

## 方式一：直接用现成程序

1. 解压便携包，双击 `KeyStats.exe`
2. 首次运行若弹出 UAC，选「是」（程序需要读取全局键鼠输入，并以管理员权限常驻才能覆盖提权运行的游戏）
3. 启动后没有主窗口，**图标在任务栏右下角托盘区**；点它打开统计面板

## 方式二：从源码构建

### 环境

| 项 | 要求 |
|---|---|
| 系统 | Windows 10 (1903+) / Windows 11 |
| SDK | .NET SDK 6 及以上（用于构建）+ .NET Framework 4.8 目标包 |
| 运行时 | .NET Framework 4.8（Win10 1903+ 自带） |

### 构建

```bash
dotnet build KeyStats.Windows/KeyStats/KeyStats.csproj -c Release -o dist/KeyStats-Portable
```

产物：`dist/KeyStats-Portable/KeyStats.exe`，双击运行。

> 若 NuGet 还原失败（离线环境），加 `--no-restore` 复用已有 `obj/`。

## 数据在哪

| 内容 | 路径 |
|---|---|
| 统计与设置 | `%LOCALAPPDATA%\KeyStats\` |
| 崩溃日志 | `%LOCALAPPDATA%\KeyStats\crash.log` |
| LCU / 英雄识别日志 | `%LOCALAPPDATA%\KeyStats\lcu.log` |
| 英雄头像缓存 | `%LOCALAPPDATA%\KeyStats\champion-icons\` |

卸载：删程序文件夹即可；要连数据一起清，再删 `%LOCALAPPDATA%\KeyStats\`。

## 开机自启

程序在设置页提供自启开关。若程序以管理员权限常驻，写注册表 `Run` 键的自启方式在登录时会被系统静默跳过 —— 这种情况请改用**任务计划程序**（触发器 = 登录时，勾选「使用最高权限运行」）。

## 常见问题

**Q：托盘没有图标？**
按 `Ctrl+Shift+Esc` 看进程里有没有 `KeyStats.exe`；没有就重新双击。启动失败看 `%LOCALAPPDATA%\KeyStats\crash.log`。

**Q：计数不涨？**
多半是权限不匹配：普通权限的钩子抓不到以管理员权限运行的前台程序（如游戏客户端）。给 `KeyStats.exe` 加上「以管理员身份运行」的兼容性设置后重启。

**Q：英雄名显示「未知」？**
英雄识别依赖《英雄联盟》客户端（LCU 接口）。客户端没开、或对局结束时客户端已退出，就会抓不到 —— 可在对局详情窗手动点「选择英雄」补上。
