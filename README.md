# Pika Desktop Pet

一个常驻 Windows 桌面右下角的悬浮电气小宠物。

## 当前功能

- 小尺寸透明悬浮窗口，不占任务栏
- 呼吸浮动、轻微摆动、电光粒子、触碰弹跳、聊天脉冲动画
- 鼠标触碰时展示 20 条轮换语录，一轮内不重复
- 点击宠物打开精致圆角聊天气泡
- 中文 / English 对话模式切换
- DeepSeek API 聊天；连接失败时自动使用本地兜底回复
- 拖拽移动；右键菜单退出
- 优先加载本地 `assets/pikachu.png`，不存在时尝试在线加载透明高清素材

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建产物为项目根目录下的 `DesktopPetMVP.exe`。

## 配置 DeepSeek

```powershell
setx DEEPSEEK_API_KEY "your-key"
setx DEEPSEEK_MODEL "deepseek-chat"
```

设置完成后重新启动桌宠。

## 使用

- 鼠标碰到宠物：展示一条随机反馈语
- 单击宠物：打开或关闭聊天气泡
- 拖拽宠物：移动位置
- 聊天气泡顶部：切换中文 / English
- 右键宠物：打开聊天或退出

## 素材说明

本项目是非商业桌面交互演示。Pokémon、Pikachu 及相关素材的权利归 Nintendo、Creatures、GAME FREAK 等权利方所有。详见 [ATTRIBUTION.md](ATTRIBUTION.md)。
