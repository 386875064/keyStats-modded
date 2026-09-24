# KeyStats 快速开始指南

## 在 Mac 上编译运行

### 步骤 1：下载项目

将 `KeyStats.zip` 下载到您的 Mac，然后解压缩。

### 步骤 2：打开 Xcode 项目

```bash
cd KeyStats
open KeyStats.xcodeproj
```

或者双击 `KeyStats.xcodeproj` 文件。

### 步骤 3：配置签名（可选）

1. 在 Xcode 中选择左侧项目导航器中的 "KeyStats" 项目
2. 选择 "Signing & Capabilities" 标签
3. 在 "Team" 下拉菜单中选择您的开发者账号
   - 如果没有开发者账号，可以选择 "Add an Account..." 使用 Apple ID 登录

> **提示**：如果只是本地测试，可以跳过签名配置，直接运行。

### 步骤 4：编译运行

1. 确保顶部工具栏的目标设备选择为 "My Mac"
2. 点击运行按钮 ▶️ 或按 `⌘R`

### 步骤 5：授予权限

首次运行时会弹出权限请求：

1. 点击 "打开系统设置"
2. 在 "隐私与安全性" > "辅助功能" 中
3. 找到 "KeyStats" 并开启开关
4. 可能需要输入系统密码确认

授权后，应用会自动开始统计！

## 使用应用

### 查看菜单栏

运行后，您会在菜单栏右侧看到类似这样的显示：

```
⌨️0 🖱️0
```

随着您使用键盘和鼠标，数字会实时更新。

### 查看详细统计

点击菜单栏上的统计数字，会弹出详细面板，显示：

- 键盘敲击次数
- 左键点击次数
- 右键点击次数
- 鼠标移动距离
- 滚动距离

### 重置或退出

在详细面板底部：
- 点击 "重置统计" 清零今日数据
- 点击 "退出应用" 关闭 KeyStats

## 常见问题

### Q: 为什么统计数据一直是 0？

A: 请检查是否已授予辅助功能权限。前往 "系统设置" > "隐私与安全性" > "辅助功能"，确保 KeyStats 已启用。

### Q: 如何让应用开机自启动？

A: 前往 "系统设置" > "通用" > "登录项"，点击 "+" 添加 KeyStats.app。

### Q: 数据存储在哪里？

A: 数据存储在 `~/Library/Preferences/com.keystats.app.plist` 中。

### Q: 如何完全卸载？

A: 
1. 退出 KeyStats
2. 将 KeyStats.app 移到废纸篓
3. 删除 `~/Library/Preferences/com.keystats.app.plist`

## 构建发布版本

如果想创建可分发的应用：

1. 在 Xcode 中选择 Product > Archive
2. 在 Organizer 窗口中选择刚创建的归档
3. 点击 "Distribute App"
4. 选择 "Copy App" 导出未签名版本，或选择其他分发方式

## 技术支持

如有问题，请查看 README.md 或提交 Issue。
