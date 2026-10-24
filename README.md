# Urbek 资源调试 Mod

面向 **Urbek City Builder** 的 BepInEx 插件，提供游戏状态查看、资源与建筑数据检查/修改、建筑百科和成就解锁请求等调试功能。插件通过 Harmony 与反射访问游戏程序集中的运行时对象；它依赖当前游戏版本的类型、字段和 API，游戏更新后可能需要适配。

> 本项目是调试工具。修改游戏数据或提交平台成就请求可能影响存档或账号状态；请先备份存档，并自行承担使用风险。

## 功能

- 在游戏中按 **F2** 打开或关闭资源面板；快捷键可通过 BepInEx 配置项 `Hotkeys/ToggleUI` 修改。
- 查看游戏状态，包括模拟步数、昼夜时刻、地图种子、人口上限、暂停和游戏结束状态。
- 搜索和查看仓库、背包、建筑储存/解锁/局部变量，以及不同类别的资源速率和全局变量。
- 修改受支持的数值字段；全局变量修改会在游戏每帧更新后重写。重新载入玩家时会清除这些临时覆盖。
- 搜索建筑并检查其动态字段；查看建筑百科中的成本、产出/消耗、存储、解锁条件、需求和局部变量等信息。
- 对当前平台提交成就解锁请求。游戏 API 不返回解锁结果，需在 Steam 或 Epic 平台确认；提交后可能同步至账号，面板无法撤回。
- 使用随插件输出的 `lang.ini` 翻译资源、字段和建筑数据；面板提供翻译重载操作。

## 环境要求

- Urbek City Builder
- BepInEx 及 Harmony（游戏运行时提供）
- Visual Studio/MSBuild 和 .NET Framework 4.7.2 targeting pack（从源码构建时）
- 项目 `libs\` 目录下的游戏与运行时程序集引用。项目文件配置了 `Assembly-CSharp.dll`、BepInEx、Harmony 和 Unity 程序集。

## 构建

在 Windows 开发环境中打开 `Urbek.sln`，选择 `Release` 配置并构建；或在已安装 MSBuild 的开发者命令行中运行：

```powershell
msbuild Urbek.sln /p:Configuration=Release
```

生成的插件位于 `bin\Release\Urbek.dll`。`lang.ini` 会在构建时复制到输出目录。若构建提示找不到程序集，请确认 `libs\` 中有项目文件引用的 DLL，并且目标 .NET Framework 4.7.2 targeting pack 已安装。

## 安装

1. 备份存档，并确认已安装与游戏版本兼容的 BepInEx。
2. 将 `Urbek.dll` 和 `lang.ini` 放入游戏的 `BepInEx\plugins\` 目录（可放在单独的插件子目录中）。
3. 启动游戏，检查 BepInEx 日志中的插件加载信息；进入游戏后按 **F2** 打开面板。

升级或移除插件前，建议先关闭游戏。不要将 `libs\` 中的引用程序集作为插件文件复制到游戏目录。

## 项目结构

| 文件 | 作用 |
| --- | --- |
| `TutorPlugin.cs` | BepInEx 插件入口、F2 快捷键、Harmony 补丁及运行时游戏对象查找。 |
| `ResourceUI.cs` | Unity IMGUI 面板、分类标签、搜索/编辑交互、建筑百科和成就请求界面。 |
| `ResourceData.cs` | 读取和格式化游戏数据、反射查找字段、写入可编辑值，以及全局变量覆盖。 |
| `lang.ini` | UI 使用的名称翻译映射。 |
| `Urbek.csproj` / `Urbek.sln` | .NET Framework 4.7.2 类库项目及 Visual Studio 解决方案。 |
| `libs\` | 编译所需的游戏、BepInEx、Harmony 和 Unity 引用程序集。 |

## 注意事项

- 插件使用 Harmony 补丁跟踪玩家载入与 `Motor.Update`，用于管理全局变量覆盖。
- 游戏字段名称和类型来自游戏程序集，并通过反射读取；游戏更新可能导致面板数据缺失或功能失效。
- 仅数值字段支持编辑。输入必须能转换为目标字段类型且在其范围内。
- `lang.ini` 使用 `键=值` 格式，分号开头的行用于注释；避免改动游戏内部键名左侧的原始名称。
- 仓库当前未包含独立测试项目；修改后至少应使用目标配置构建，并在兼容的游戏环境中验证。
