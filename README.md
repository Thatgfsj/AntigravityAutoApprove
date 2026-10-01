# Antigravity Auto Approve（反重力权限弹窗自动批准）

后台静默自动点击 Google Antigravity（反重力）IDE 中 Agent 的权限批准弹窗，让你完全解放双手。

> 弹窗出现后约 1 秒内自动选中第 1 项 **「Yes, allow this time」**，两段式弹窗自动补点 **Submit**，全程不抢焦点、不碰鼠标。

## ✨ 功能特性

- **后台静默运行** — 最小化到系统托盘，无窗口干扰；Antigravity 在后台/被遮挡/最小化时同样可以自动点击
- **固定选 A** — 永远自动选择第 1 项 "Yes, allow this time"（仅本次批准），权限不残留
- **两段式弹窗兜底** — 点完选项后若 Submit 按钮仍在，自动补点
- **三重点击机制** — UIA Invoke/SelectionItem/Toggle 模式优先（无障碍接口，不抢焦点），坐标点击兜底（点击前校验落点归属，被其他窗口遮挡时短暂置前再还原，绝不误点别的程序）
- **蓝白主题界面** — 自绘标题栏 + 状态卡片 + 现代拨动开关
- **开机自启开关** — 界面内一键开关（写/删 HKCU 注册表 Run 键，带 `--minimized` 参数静默启动），完全可逆
- **实时统计** — 状态卡片 + 托盘提示实时显示"已自动批准 N 次"
- **零依赖** — 纯系统自带 .NET Framework 4.x 编译的单文件 exe，无需安装任何运行库

## 🖥️ 界面预览

- 蓝色渐变标题栏（可拖动，右上角显示累计批准次数）
- 状态卡片：绿点 = 监视中，实时状态 + 累计批准次数
- 两个拨动开关：`自动批准` / `开机自动启动`
- 底部：隐藏到托盘 / 打开日志 / 退出程序

## 🚀 使用方法

### 方式一：下载编译好的版本

到 [Releases](../../releases) 下载 `AntigravityAutoApprove.exe`，双击运行，打开「自动批准」开关即可。

### 方式二：自己编译（推荐，30 秒）

```cmd
build.cmd
```

使用系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，任何 Windows 10/11 都自带，无需安装任何东西。编译产物输出到 `%LOCALAPPDATA%\AntigravityAutoApprove\AntigravityAutoApprove.exe`。

### 配套设置（重要）

为了让 UIA 看得到 IDE 内部弹窗元素，Antigravity 需开启无障碍支持（二选一）：

1. Antigravity 设置 → 搜索 `accessibility.support` → 设为 `on`（推荐，写入 `%APPDATA%\Antigravity\User\settings.json`）
2. 或启动参数带 `--force-renderer-accessibility`

## 🔧 工作原理

1. 每 400ms 用 UIA **哨兵查询**（原生侧过滤，空闲时近乎零开销）探测 Antigravity 窗口内是否存在 `Skip` / `Submit` / `Yes, allow this time` 元素
2. 命中后全量扫描可访问树，找到名字包含 `Yes, allow` 的**任意类型元素**（真实弹窗选项是带数字前缀的列表行，不是标准按钮），取树序第一个即第 1 项
3. 通过无障碍接口静默点击；每 5 个 tick 强制全量扫描一次兜底，防止元素命名不一致漏检
4. 点击后检查 Submit 是否仍在（两段式弹窗），250ms 后补点

## 🔒 隐私与安全

- 纯本地运行，**不联网、不上传任何数据**
- 日志仅记录批准事件，保存在 `%APPDATA%\AntigravityAutoApprove\log.txt`
- 源码完全开放，一个文件 `AutoApprover.cs`（约 800 行），自己编译自己看

## ⚠️ 免责声明

自动批准意味着 **Agent 的命令将不经确认直接执行**（包括删除文件等危险操作）。请在理解风险的前提下使用，建议在受信任的项目中使用本工具。工具关闭或停用时，权限弹窗自动恢复询问，相当于多一道安全闸。

## 📄 License

MIT
