# 一份成品用于使用和分享

本机固定成品：`D:\Betm\game\视觉小说资源工具`。
桌面入口：`视觉小说资源工具.lnk`，目标为成品内同名 EXE，工作目录也是成品目录。
源码与开发环境：`D:\Betm\game\解包软件`，不机械迁移工程。

成品根目录仅有主程序、使用说明、许可/使用声明及 third_party。当前共 19 个文件，约 162.88 MiB。主程序为 Windows x64 自包含单文件，.NET/native 库由运行时展开到用户临时缓存；外部解包工具、Python 脚本与许可显式排除在单文件嵌入之外，保持 AppContext.BaseDirectory 下的真实路径可访问。

## 个人数据及运行限制

- 设置和拖出缓存仍位于 `%LOCALAPPDATA%\视觉小说资源工具`，不在成品内。整理前后 settings.json 的 SHA256 相同。
- 提取内容在界面指定的输出位置。更新/打包脚本不迁移、删除或上传游戏、输出资源与个人数据。
- 程序无需账号登录；本次没有把本机凭据或个人设置复制到成品。
- .NET SDK/runtime、源码和 NuGet 缓存不需要由接收者安装。`third_party` 需随文件夹分享。
- FFmpeg 仍是视频预览的可选 PATH 依赖；Python 3 仍是反编译的可选依赖。没有额外捆绑本机工具或游戏自带解释器；使用说明明确这些限制。正常图片/资源提取不要求这两者。
- unrpyc 通过 PYTHONDONTWRITEBYTECODE 避免向成品写入 Python 缓存。

## 维护命令

在开发目录中运行：

```powershell
pwsh -NoProfile -File scripts/Publish-Portable.ps1
# 仅在需要分享 ZIP 时执行；默认写到成品目录旁，不生成第二份成品
pwsh -NoProfile -File scripts/Compress-Portable.ps1
```

发布脚本先构建开发区的临时目录，检查必需文件，再替换固定成品；运行中的软件会阻止更新。`.artifacts/portable-manifest.json` 保存成品文件清单/哈希。更新或打包时遇到新增、被修改的文件会停止并保留，避免误删除/误分享个人文件。打包不覆盖已有 ZIP，也不允许 ZIP 放进成品文件夹。

更新现有成品时，脚本暂存上一份到 `.artifacts/portable-previous`；确认新版本运行正常后由维护者清理或作为唯一回退备份替换旧备份。脚本不允许多次堆叠未处理的 previous。第一次整理没有 previous。

`.dotnet-home`、`.nuget-packages`、正常 bin/obj 和 third_party 上游材料保留，具有开发价值。`.artifacts` 是忽略的本地维护区，不进入 Git、成品或分享包。

## 本次验证与清理（2026-09-11）

- 当前源码的 30 项控制台测试通过；此前系统审查提交 de71ccd 的云端 build 已成功。
- 将成品按真实分享方式压缩再解压到系统 Temp，工作目录设为 Windows 目录，PATH 只保留 Windows/System32，DOTNET_ROOT 指向不存在的目录并禁用共享 .NET 查找。主程序成功启动并显示正常主窗口和操作按钮，不依赖源码目录启动。
- 从固定成品实际通过桌面快捷方式启动、正常关闭成功；成品内 evbunpack --help 可直接执行。
- 清点资源与运行组件完整，未发现 settings.json、PDB、pyc 或游戏素材。打包/解压临时副本已清理，不保留重复分享版。
- 原生文件夹对话框的 UIAutomation 自动操作未可靠完成，因此没有将这次独立成品启动测试宣称为全流程导入/提取通过。没有干净 Windows 虚拟机上的全功能测试；FFmpeg/Python 功能需接收者具备相应可选依赖。上一轮的实际提取和 WPF 流程验证详见 REVIEW-2026-09.md。
- 整理前 ZIP 与旧桌面入口的 publish 逐文件 SHA256 比对：484/484 完全相同。将该 ZIP 移到 `.artifacts/recovery/整理前桌面版.zip`，只保留这一个明确可恢复的旧版本备份。
- 已从开发目录移除 publish（484 文件）、publish-webm（22 文件）、上轮临时 ReviewHost（64 文件），合计约 338.57 MiB；为便于撤回采用回收站，未宣称已释放这些磁盘空间。永久清理命令被执行策略拒绝后，改用可恢复的回收站操作完成。
- 原根目录 ZIP 已移动而非复制；未保留当前成品的长期 ZIP。测试 ZIP、解压目录和一次性自动化脚本也移入回收站。桌面原快捷方式原位更新，没有增建多个启动入口。

本轮不发布新版本、不改旧 Git 标签。项目目录中的旧 HANDOFF 段落保留历史记录，以本说明和最新补充为当前布局。
