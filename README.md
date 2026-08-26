# 视觉小说资源工具

[中文](README.md) | [English](README_EN.md)

面向 Windows 的中文视觉小说资源识别与提取工具。程序只读打开原游戏资源，并将内容写入用户选择的独立输出目录。

> 请仅处理你有权访问的资源。提取不代表获得素材的转载或商业使用权，详见 [使用与版权说明](DISCLAIMER.md)。

## 界面预览

启动后可以拖入完整游戏目录、RPA 或 XP3，也可以使用右侧按钮选择。

![程序初始界面](docs/images/home.png)

识别完成后，可筛选资源、查看主预览与前后 5 张缩略图，再按分类、选中项或拖拽方式提取。

![导入游戏后的资源浏览界面](docs/images/game-loaded.png)

## 当前能力

- 扫描单个资源包或完整游戏目录
- 安全浏览并流式提取 Ren'Py RPA 2.0、3.0、3.2（不调用 Python `pickle.loads`）
- 浏览并提取标准 KiriKiri XP3，包括压缩索引和分段资源
- 读取 RPG Maker MV/MZ 的项目密钥，还原加密图片与音频
- 浏览并提取带 `packageKey` 的 RPG Maker MV `data.pak`（ZIP AES）资源
- 浏览并提取 Hunks Workshop 使用的 DPMX (`HWS*.dat`) 容器
- 识别并提取 Enigma Virtual Box 封装的 RPG Maker MV 单文件 EXE；临时展开后只保留图片和动图
- 默认只显示图片，并按 CG/事件图、背景、角色图层、动图/视频、界面和其他图片筛选
- 可浏览并复制已经散放在 Ren'Py、RPG Maker 游戏目录中的普通图片和视频，不再只处理封包或加密后缀
- 提取当前分类、文件名筛选、Ctrl/Shift 多选、只提取选中项；高级模式仍可查看全部资源
- 直接预览 RPA 和 XP3 中的常见图片
- 集成官方 unrpyc 2.x，将 `.rpyc` 复制到输出目录后反编译，不修改游戏
- 默认输出位置持久化设置，界面实时显示最终完整路径
- 选择完整游戏目录时，默认输出到该游戏根目录下的 `解包结果\资源包名`，不再跟随资源包钻入内部 `game` 目录
- 可选为常见首级目录添加中文注释，例如 `bg-背景`、`cg-事件图`、`sprites-角色立绘`
- 当前筛选结果可打开轻量缩略图网格（最多加载 200 张）；GIF 可直接预览，WebM/MP4 等视频由 FFmpeg 转成前 6 秒的静音轻量预览，不依赖 Windows 视频解码器
- 右侧主预览下方显示当前图片前后共 5 张快捷缩略图，可点击切换
- 可将资源列表中的选中项直接拖到资源管理器或桌面；程序会按需提取到受控缓存，旧缓存自动清理
- 直接拖入受支持的 RPG Maker 单文件 EXE，与拖入其游戏目录使用相同识别流程
- 中文进度、取消、跳过/覆盖控制、路径穿越保护
- 提取完成信息显示在主界面，不再产生会累积的完成提示框
- 自动替换 Windows 不允许的文件名字符；单文件损坏不会中断整个任务，并生成 `_提取记录.txt`
- 支持中文、空格、全角符号路径和大文件

## 构建

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-home"
dotnet build VisualNovelResourceTool.sln -c Release
dotnet run --project VisualNovelResourceTool.Tests -c Release
dotnet publish VisualNovelResourceTool.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

`publish` 中的 `视觉小说资源工具.exe` 是自包含版本，不要求电脑预先安装 .NET。`third_party/unrpyc` 目录需和 EXE 一起保留。
`third_party/evbunpack` 是 Apache 2.0 许可的 EXE 虚拟文件系统提取组件，也需和主程序一起保留。

第三方组件和许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。项目自身采用 [MIT License](LICENSE)。

## WebM 预览

WebM/MP4 动态预览需要系统可以从命令行找到 `ffmpeg.exe`。未安装 FFmpeg 时不影响识别和提取，只是不生成视频动态预览。正式 Release 会在说明中明确标注这一可选依赖。

## 输出规则

默认输出为：`所选游戏根目录\解包结果\资源包名`。例如选择 `D:\Games\Example` 后，其中任意深度的 `images.rpa` 都会进入 `D:\Games\Example\解包结果\images`。直接选择单个资源包时，则自动使用资源包所在游戏目录。界面仍可启用固定输出根目录，设置保存在当前 Windows 用户的本地应用数据目录中。

## 安全边界

- 永不修改原 RPA/XP3 或游戏目录中的内容。
- RPA 索引使用受限解析器，任何可能构造对象或执行代码的 Pickle 操作码都会被拒绝。
- 输出路径必须位于选择的输出目录内。
- 单个文件先写入临时文件，完整成功后再原子替换目标文件。
- Windows 非法字符、保留设备名、尾随空格/句点和重名冲突会安全改名并留下映射记录。
- 单个条目失败会留下错误记录并继续提取其余条目；取消操作仍会立即停止。
