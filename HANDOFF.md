# 项目交接（2026-09-07）

## 1. 目标与已确定需求

中文优先、面向小白的 Windows 视觉小说资源识别、浏览和提取工具。游戏原件只读；默认关注 2D 图片、CG 和动图，少量 3D 游戏按实际资源格式判断，不承诺所有游戏可解包。不要从头设计项目。

- 拖入完整游戏目录或受支持 EXE，应使用一致的识别流程；也支持直接选择资源包。
- 默认输出 `所选游戏根目录\解包结果\资源包名`，不要落入深层 game 目录；可自定义并保存默认输出根目录，界面显示最终完整路径。
- 默认图片筛选，提供背景、CG、角色图层、动图/视频、UI 等分类与搜索；不需要自定义分类关键词。可选目录中文注释，如 bg-背景、cg-事件图。
- 保留最多 200 张的缩略图网格；主预览下方一行五列，含当前项及前后图片，点击切换。动态预览轻量即可，不做完整播放器。
- 可拖出选中资源到资源管理器；拖滚动条绝不能触发提取。支持 Ctrl/Shift 多选。
- 特殊文件名安全改名；单文件失败不应中止批量任务；索引不能无限等待；取消有效；完成信息不能产生堆积弹窗。
- 中文/空格/全角路径、大文件、低成本运行。角色大量图层自动合成仅低成本可行时考虑，用户不强求。
- 优先根因修复、清晰统一实现；必要时重构，不堆 fallback，不做无收益优化或大量低价值测试。中途不要重复确认已明确需求，完成后统一验收。
- 用户授权 GitHub 登录、公开上传、发布和上传两张已打码示意图。中英文 README 已实现；软件本身是否需要切换英语仍未确认，不应假定已要求全面 UI 国际化。

## 2. 架构与决策

.NET 8 / C#；WPF App + 独立 Core + 控制台 Tests。Core 使用 SharpCompress 0.50.4。App 当前主要是 MainWindow.xaml.cs 的代码后置调度，多个按资源类型分派的分支；不能宣称已经实现统一插件接口。MainWindow 和 Tests 存在较多压缩成长行的实现，维护性有改善空间，按实际修改范围处理。

RPA 使用 SafePickleReader 受限解析器，避免执行 pickle；格式类负责索引和提取，SafeExtractionPath 负责输出安全，AssetClassifier 负责分类。unrpyc 使用外部 Python；Enigma EXE 使用捆绑 evbunpack，临时展开后筛选视觉资源。视频用外部 FFmpeg 转前 6 秒、7 fps 静音 GIF，宽度 560。

GARbro 选择性移植格式处理器的原则在 docs/GARBRO_PORTING.md：按真实样本需求、保留许可来源、遵守本项目提取约定。历史理由是避免整个旧 .NET Framework 应用和双套界面/抽象；仍是当前设计方向，但新移植前应重新核验上游技术与许可，不能将策略文档当成“已移植全部能力”。

## 3. 功能与验证证据

现有 README 和代码包含：RPA 2/3/3.2、标准 XP3、RPG Maker MV/MZ 普通及加密资源、带 packageKey 的 data.pak、DPMX、受支持 Enigma EXE、散装图片视频；图片/GIF/视频预览；分类/多选/拖出/中文目录/输出设置/错误记录。格式存在不等于所有变体兼容。

历史上记录通过：本地 9/9 控制台测试、发布程序启动冒烟；GitHub build 流程成功；2026-08-26 Release 构建上传成功。用户多次反馈已高度可用，但多个具体游戏修复的逐项验收记录不可恢复，不要推断全部已修好。

本次已核验（只读文件检查，未运行构建、测试、GUI 或游戏提取）：分支/提交/标签/文件布局、README、工作流、测试入口、依赖、AppSettings、相关预览/拖拽代码、两个截图和本地发布文件存在。SDK `8.0.423` 可执行。未重新联网核对 Release 或 GitHub 登录。

**本次发现需纠正的历史表述：** Tests.csproj 是 OutputType=Exe，没有测试 SDK，9 项测试由 Program.cs 无参数执行。build.yml 正确调用 dotnet run；release.yml 却调用 dotnet test。Release 绿色不能证明实际执行 9 项测试，旧回复“云端发布执行了9项测试”证据不足。应先修正 Release 测试命令并验证。

## 4. 路径与文件职责

- 根目录：`D:\Betm\game\解包软件`，保存的 Codex 项目“解包软件”。
- `VisualNovelResourceTool.App/MainWindow.xaml(.cs)`：界面、扫描、筛选、预览、取消、拖放、提取调度。
- App `AppSettings.cs`：输出规则与设置；`ThumbnailWindow.cs`：网格；`EnigmaExtractorService.cs`、`UnrpycService.cs`：外部工具。
- `VisualNovelResourceTool.Core/`：GameScanner、Models、RpaArchive、Xp3Archive、DpmxArchive、RpgMakerPakArchive、RpgMakerProject、LooseResourceProject、SafePickleReader、SafeExtractionPath、AssetClassifier。
- `VisualNovelResourceTool.Tests/Program.cs`：9 项合成测试及真实样本命令行检查入口。
- `.github/workflows/build.yml`：持续构建；`release.yml`：v* 标签发布（目前全部 prerelease）。
- `third_party/`：unrpyc 运行文件和 evbunpack.exe 及许可；勿删除。
- `README.md`、`README_EN.md`：中文/英文说明；`docs/images/home.png`、`game-loaded.png`：用户允许公开的打码截图，原“示意图”目录已迁移到这里。
- LICENSE、THIRD_PARTY_NOTICES.md、DISCLAIMER.md：许可与分发说明。
- `%LOCALAPPDATA%\视觉小说资源工具\settings.json`：用户设置；同目录“拖出缓存”；临时预览使用系统 Temp 下 vnrt-preview-*。设置内容本次未读取。

## 5. Git、版本与需保留的本地状态

本次检查：main；HEAD `55fa0e4`（docs: add interface previews and release automation）；此前 `8999122` Initial public beta；标签 v0.1.0-beta，App Version 0.1.0-beta。git status 显示 main...origin/main 且干净，但未 fetch，因此不代表远端今日无变化。

本次仅新增本 HANDOFF.md，故交接后它是未提交文件；不要遗漏或误称工作区仍干净。没有修改功能代码或工作流。

远端：https://github.com/sqcyx123-cmyk/Visual-Novel-Resource-Tool
历史发布：https://github.com/sqcyx123-cmyk/Visual-Novel-Resource-Tool/releases/tag/v0.1.0-beta
历史 Release run：32928693919；公开附件历史 SHA256：9a53ecb721f9e3476c06666a6d5ab913007d49ee008520f5be17772f1b1a379a（本次未下载核验）。

忽略但需保留：publish/、publish-webm/、根目录 VisualNovelResourceTool-v0.1.0-beta-win-x64.zip、.dotnet-home/、.nuget-packages/；third_party/unrpyc 还有未纳入 Git 的上游材料。两个发布目录 EXE 均在，但大小不同，当前用户使用哪份未知。本地 ZIP 与历史云端 ZIP 不同，不能互换校验值。不要清理这些目录。

## 6. 最近工作与历史问题样本

最近完成的是公开发布和双语说明/截图，随后用户要求结束长对话并交接。GitHub 本地大附件上传曾卡死、TLS/EOF 失败；后改标签触发云端打包上传成功，空草稿已处理。当前没有已确认的新功能开发项，先处理交接核验发现的发布验证缺口。

真实样本在工作区外，原件只读且不可上传。以下四个 2D 目录和 Heatwave 目录本次确认存在，内部资源未核验：
- `D:\Betm\game\2D\家有大猫`：历史疑似只识别 data/media.rpa，散装资源遗漏。
- `D:\Betm\game\2D\友好的小精灵`：历史可提取但无文件。
- `D:\Betm\game\2D\HASV1.2`：历史被识别成散装资源，用户要求增加支持。
- `D:\Betm\game\2D\EMP-Go!Ed's Massage Parlor!`：历史 EXE/目录识别不一致。
- `D:\Betm\game\3D\Heatwave Sams Stay`：历史索引一直读取，后又反馈只能提取少量文件。

其余历史样本位置未重查：2D\夏有天狼 天狼星观测指南（XP3 预览/特殊名）、2D\ExtracurricularActivities（角色图层）、Black Monkey Blits 下 Bacchikoi（输出过深）、Sleep over：reWake（滚动条拖出误触）、Camp Buddy（WebM）。确切现行嵌套路径未知，按需查找。

## 7. 排除的方案

- 用户已删除 UnRen-1.0.11d.bat；不恢复该批处理。已有可控核心解析和单独 unrpyc。
- 不做自定义分类关键词、完整播放器或高成本通用图层合成：用户明确取舍，仍适用。
- 不整体嵌入 GARbro：当前架构决定，移植时核验条件是否变化。
- 不再依赖本地上传大 Release ZIP：云端流程已成功，继续采用。
- 不承诺任意 EXE/加密档案通用解包；未知格式应明确提示。

## 8. 风险与优先级

P1：修正 release.yml 的控制台测试执行方式，验证确实出现 9 PASS/0失败，且失败会阻断发布；不改旧标签、不覆盖既有 Release。
P1（按复现决定）：上述游戏完整性、滚动条误拖、索引取消等历史问题仍须针对当前代码验证。RpaArchive 当前有 30 秒 CancelAfter，仅存在代码不证明全部解析路径及时响应。
P2：PreviewVideoAsync 当前等待 FFmpeg 无显式超时/取消；版本号只抑制旧结果，StopVideoPreview 未见终止该 FFmpeg 进程。快速切换时进程/缓存生命周期有风险，本次未复现，先验证再修。
P2：UI 回归、真 XP3/DPMX/PAK、视频等未被现有 9 项测试全面覆盖；测试名“识别 XP3”只验证文件头，不能当成完整解包测试。
P2：旧 Actions Node 20 退役警告历史存在，按需重新核查版本。未签名程序可能触发 SmartScreen；签名/FFmpeg 分发是未来改进，不是已完成项。
P3：软件 UI 英语切换未确认；按真实需求扩格式、适度改善长方法维护性。

## 9. 启动、构建、测试与环境

Windows PowerShell，在根目录运行：
```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-home"
dotnet build VisualNovelResourceTool.sln -c Release
dotnet run --project VisualNovelResourceTool.Tests -c Release
dotnet run --project VisualNovelResourceTool.App -c Release
dotnet publish VisualNovelResourceTool.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# 只读识别示例
dotnet run --project VisualNovelResourceTool.Tests -c Release -- --scan 'D:\Betm\game\2D\家有大猫'
```
publish 命令会更新本地发布目录，先确认现有程序是否占用并避免丢失用户文件。测试 Program.cs 还有 --loose-check、--rpg-check、--pak-check 等，会在 Temp 写入样本后清理；--extract 会写指定输出且覆盖，使用前读实现。

GUI 需要 Windows；发布 ZIP 解压后保留第三方目录，不要只复制 EXE。视频需 PATH 中 ffmpeg.exe，本次找到 `C:\Users\10519\Documents\Codex\tools\ffmpeg\ffmpeg-8.1.2-essentials_build\bin\ffmpeg.exe`，未试运行。Python PATH 当前指向 WindowsApps/python.exe 别名，不证明可用；unrpyc 也会搜索游戏 lib/python.exe。

GitHub CLI 路径 `C:\Program Files\GitHub CLI\gh.exe`，历史账户 sqcyx123-cmyk，当前登录需自行检查，禁止读取/记录令牌。网络受限，历史出现 TLS/EOF、NuGet NU1900；需要时按环境申请命令执行权限，不反复询问已授权发布意图。真实游戏位于工作区外，写入可能需额外环境权限。

## 10. 新任务首轮与完成标准

1. 在同一项目**当前本地目录**继续，先读此文档与实际文件，检查 git status/log 和必要本地文件。旧任务交接完成后停止写文件；无需新工作树和迁移忽略文件。
2. 修复 Release 测试命令这一具体缺口；执行真正的控制台测试，确认9项被运行、失败退出码可阻断后续步骤。必要时调整工作流，保持方案简单。仅针对变更验证，不顺带重写项目。
3. 检查远端实际状态后按既有授权提交/推送相关修复及交接资料；不擅自重打 v0.1.0-beta 或发布新版本。若无法联网，明确本地/远端分别完成到哪。
4. 报告实际修改与验证范围、历史问题哪些仍未知；没有明确后续需求时不要凭空扩展功能。未来接到游戏问题时依据此文档样本复现，不把历史结论直接当现状。

验收：新任务使用 HEAD 55fa0e4 的实际工作目录及新增交接文档，必要忽略文件可访问；测试流程执行真实测试；未丢失用户数据。最终按用户约定简要报告主要修改、根因/架构问题、未必要优化之处、未来优化、项目评价和仍不明确需求。

## 11. 接手任务验证补充（2026-09-07）

以上“本次”检查范围描述的是旧任务交接时的状态，本节记录新任务的实际后续结果。

- 在原本地目录接手，起始 main HEAD 为 55fa0e4，仅 HANDOFF.md 未提交。已 fetch 并核对 origin/main 同为 55fa0e4；旧标签 v0.1.0-beta 未改动。
- 已确认 publish、publish-webm 的程序、原 ZIP、third_party/evbunpack/evbunpack.exe、.dotnet-home 和 .nuget-packages 存在，未清理或重新发布这些文件。
- release.yml 的 Test 步骤改为 `dotnet run --project VisualNovelResourceTool.Tests -c Release --no-restore`，显式使用 pwsh 并 `exit $LASTEXITCODE`。保留控制台测试架构，无需引入测试 SDK；后续发布步骤没有 continue-on-error 或 always() 绕过失败。
- 正常执行实际输出 9 PASS、0 失败，退出码 0。临时将首项版本断言改为必失败，从工作流读取 Test 脚本并在独立 pwsh 执行，实际输出 8 PASS、1 FAIL、退出码 1；本地成功条件门控未进入后续发布分支。这是本地故障注入验证，未触发标签 Release，也未声称已在 GitHub 实测失败发布。
- 故障注入后在 finally 中按原始字节恢复 Program.cs；第一次恢复后运行仍出现注入失败，强制 Rebuild Tests 后再执行正常命令得到 9 PASS、0 失败、退出码 0。最终 git diff 确认测试源码无修改。Tests/Core Release 重建成功，0 错误；运行期间出现 NU1900（NuGet 漏洞服务无法访问），漏洞数据检查未完成。
- 本轮未运行 GUI、真实游戏提取、App 发布或全量游戏回归；第 6、8 节的历史游戏问题仍待复现。本次仅修复发布验证入口，不扩大功能或重构范围，不重打旧标签或新增版本。
