# RailDisplay

面向游戏数据的铁路设备模拟与显示客户端，使用 .NET 10 和 Avalonia。当前迁移了 CTCS2-200H 已有行为；监督曲线完整算法、CTCS3 专属规则、DLL 通信尚未实现，业务规则由项目维护者提供。

当前桌面支持目标为 Windows 和 macOS；Linux 已退出支持范围，已有 ALSA 代码暂时保留。

## 项目分类

```text
RailDisplay.Core/                   纯业务类库，不引用 UI 或游戏程序集
  Models/CTCS/                      数据、等级、模式、消息
  Controllers/CTCS/                 CTCS2/3 共用调度入口、设备逻辑、运行逻辑及接口
RailDisplay.Infrastructure/         接入类库，引用 Core；设备音频及 NAudio 播放，通信实现待补
RailDisplay/                        桌面应用，引用 Core；启动组装引用 Infrastructure
  Models/CTCS/                      屏型能力、菜单和显示模型
  ViewModels/CTCS/                  显示状态、通知和操作协调
  Controls/CTCS/                    速度表、距离标尺、制动图标
  Controls/PixelDisplay.cs          共用的像素渲染和缩放
  Views/CTCS2_200H.axaml             屏型页面，Views 下不再增加系统或型号目录
  Views/MainWindow.axaml            主窗口，向屏型页面传入 CTCS ViewModel
  Assets/Themes/CTCS/               颜色等视觉资源
  Debug/                            原生菜单及可选 Debug DLL 加载入口
RailDisplay.Debug/                  可选本地 Debug 插件，只引用 Core、日志抽象和 UI 依赖
  ViewModels/、Views/                手柄输入与调试窗口
  CTCS/                             本地样例数据和模拟调度
checks/
  CTCS/                            业务、显示和屏型能力检查
  Rendering/                       像素、图标、实际页面绑定和缩放检查
  Core/                            只依赖 Core 的检查入口
```

后续 LKJ、CIR 的代码分别进入对应职责下的 `LKJ`、`CIR` 目录，例如 `Core/Controllers/LKJ` 和 `RailDisplay/ViewModels/LKJ`；页面继续直接放在 `Views` 中。只在有实际实现时新增目录，不创建空控制器。

## 各部分负责什么，后续如何修改

| 位置 | 职责 | 修改方式与关联检查 |
| --- | --- | --- |
| [CtcsData](RailDisplay.Core/Models/CTCS/CtcsData.cs)、[CtcsTextMessage](RailDisplay.Core/Models/CTCS/CtcsTextMessage.cs) | 业务字段、有效枚举、消息内容和确认状态 | 新增字段时明确单位、缺失值和范围，同步修改核心校验与检查；显示文字不放在这里 |
| [CtcsController](RailDisplay.Core/Controllers/CTCS/CtcsController.cs) | 对外统一入口，调度设备逻辑和运行逻辑，将最新运行模式映射为交互根 | 新增协调操作在这里接线并同步接口；设备和运行状态不在控制器另存副本 |
| [CtcsDevice](RailDisplay.Core/Controllers/CTCS/CtcsDevice.cs) | 实体按键分发、功能步骤校验、当前交互及返回历史 | 设备交互规则改这里，导航机制继续复用共用导航器；同步交互与显示通知检查，不持有运行数据或计算状态 |
| [CtcsCore](RailDisplay.Core/Controllers/CTCS/CtcsCore.cs) | 运行输入校验、唯一数据快照、监视区、超限判断、制动锁存、预警推进及运行消息确认 | 后续监督、制动等运行规则在这里扩展；复杂计算可像现有预警预测器一样按职责拆分，等级差异按 `Data.CurrentLevel` 处理，同步核心和预测检查 |
| [ICtcsController](RailDisplay.Core/Controllers/CTCS/ICtcsController.cs) | 展示层使用的核心契约 | 核心新增实际操作时同步实现类、调用方及检查；不暴露控件、网络对象或颜色 |
| [KeyHandler](RailDisplay.Core/Controllers/Interaction/KeyHandler.cs) | 共用按键表校验、复制和分发 | 初始化时将设备按键映射到处理委托；具体处理器读取最新模式与步骤，规则变化同步交互自检 |
| [InteractionNavigator](RailDisplay.Core/Controllers/Interaction/InteractionNavigator.cs) | 共用当前交互和返回历史 | 状态使用不可变快照，导航机制变化同步交互自检；不包含设备模式、文字或坐标 |
| [CtcsViewModel](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs) | 颜色状态、灯带终点、文字格式、滚动、闪烁及绑定通知 | 调整显示组合或交互改这里。当前界面更新通过 `Update`、`ConfirmTextMessage`、`TrySwitchLevel`，以保证核心结果和通知同步；不要另外维护一份业务 Data |
| [CtcsScreenProfile](RailDisplay/Models/CTCS/CtcsScreenProfile.cs) | 屏型允许的本地等级切换能力 | 调整 `SupportsLevel3`，在 App 组装时选用对应配置；用屏型检查验证开关两种情况 |
| [CtcsTextMessageRow](RailDisplay/Models/CTCS/CtcsTextMessageRow.cs)、[FunctionKeyMenu](RailDisplay/Models/CTCS/FunctionKeyMenu.cs) | 消息行外观、功能键文字及导航配置 | `TargetStep` 生成进入步骤的绑定，`IsBack` 显式标记返回，不按文字猜动作；修改同步展示与交互检查 |
| [CtcsNavigationKeys](RailDisplay.Core/Controllers/CTCS/CtcsNavigationKeys.cs)、[CtcsNavigationBinding](RailDisplay.Core/Models/CTCS/CtcsNavigationBinding.cs) | 将初始化传入的步骤/实体键配置转换为共用导航处理器 | 拒绝非法枚举、重复绑定及进入/返回冲突；查询和执行共用同一可用条件，修改同步交互检查 |
| [CTCS2_200H.axaml](RailDisplay/Views/CTCS2_200H.axaml)、[CTCS 资源](RailDisplay/Assets/Themes/CTCS/CTCSColors.axaml) | 屏幕布局与样式 | 修改区域尺寸、布局、颜色在这里；新增屏型使用独立 View 文件，并在启动或实际导航入口选择它 |
| [CTCS 控件](RailDisplay/Controls/CTCS)、[PixelDisplay](RailDisplay/Controls/PixelDisplay.cs) | 指针、刻度、图标和统一缩放 | 修改几何与渲染在这里，不加入业务计算；运行像素和实际页面渲染检查 |
| [CtcsDebugData](RailDisplay.Debug/CTCS/CtcsDebugData.cs) | 生成当前本地模拟数据 | 仅在首次启用插件时生成样例输入，经主程序提供的入口送入 Core；修改样例改这里，不发送游戏操作 |
| [App](RailDisplay/App.axaml.cs)、[MainViewModel](RailDisplay/ViewModels/MainViewModel.cs)、[HotReloadHandler](RailDisplay/HotReloadHandler.cs) | 统一组装实例、传入页面状态和界面重绘 | 更换屏型或数据来源改组装位置；热重载只刷新界面，不重新生成模拟数据，不重建核心或声音实例 |
| [Infrastructure](RailDisplay.Infrastructure/RailDisplay.Infrastructure.csproj) | 设备音频、实际 WAV 播放；将来实现 DLL 连接 | 当前桌面项目仅在启动组装位置使用设备音频具体类；通信继续等待协议确认 |

## 版本

版本统一在根目录 [Directory.Build.props](Directory.Build.props) 定义，格式为“版本状态-大版本.中版本.小版本”。修改 `VersionSuffix` 调整状态，修改 `VersionPrefix` 调整三段数字，各项目共同继承；`InformationalVersion` 自动组合，启动日志直接读取该元数据，不再使用纯数字程序集版本。不追加 Git 提交号。

.NET 程序集和文件版本仍使用 SDK 生成的数字格式，NuGet 版本使用标准的“数字-状态”顺序；这些构建元数据不改变对外版本格式。修改后执行解决方案构建，检查生成的 `AssemblyInformationalVersion` 与启动日志一致。

## 日志

使用 `Microsoft.Extensions.Logging` 的 `ILogger<T>`，由 Serilog 写入本地文件。新增依赖用于现有控制台输出不具备的统一级别、结构化上下文、文件轮转及保留数量控制；不引入 Host、服务容器或日志服务器。Core 保持无日志框架依赖，Debug 插件只引用日志抽象，不引用 Infrastructure。

日志目录由 `Environment.SpecialFolder.LocalApplicationData` 确定，子目录为 `RailDisplay/Logs`；Windows 通常是 `%LOCALAPPDATA%\RailDisplay\Logs`，macOS 通常是 `~/Library/Application Support/RailDisplay/Logs`，实际路径以启动日志中的 `LogDirectory` 为准。文件名形如 `raildisplay-20261017.log`，按天及每份 5 MiB 轮转，最多保留最近 14 份文件（不是 14 天），多进程允许共享写入，默认不附带进程 ID。每条包含带时区的时间、级别、完整来源名、消息及异常堆栈。使用 Serilog 标准文本模板，不在末尾追加属性集合（因此不会输出空 `{}`）；需要在文本中查看的上下文写进消息模板。Debug 构建默认 DEBUG 并同时输出控制台，Release 默认 INFO；Avalonia 框架诊断额外限制为 WARN 及以上，避免 Layout 等内部刷新日志刷屏；仍遵守更高的全局门槛及 OFF。应用主动发出的 HotReload 完成通知保留 INFO，不受框架限制。启动前可用环境变量 `RAILDISPLAY_LOG_LEVEL` 覆盖：按 Log4j 顺序支持 `ALL`、`TRACE`、`DEBUG`、`INFO`、`WARN`、`ERROR`、`FATAL`、`OFF`，大小写不敏感。设置某级别记录该级别及更严重的消息；ALL 开启全部，OFF 关闭全部。内部 INFO 对应 .NET Information，WARN 对应 Warning，FATAL 对应 Critical。未设置或空白使用构建默认值，未知名称明确拒绝启动。

| 位置及入口 | 职责、后续修改与关联检查 |
| --- | --- |
| [ApplicationLogging.CreateFactory](RailDisplay.Infrastructure/Logging/ApplicationLogging.cs) | 唯一输出配置；调整日志目录、级别、文件大小、保留数量和输出模板改这里。每个工厂拥有自己的 Serilog 实例，不使用全局 Logger；同步 [LoggingChecks](checks/Logging/LoggingChecks.cs) 及本节 |
| [Program.Main](RailDisplay/Program.cs)、[App](RailDisplay/App.axaml.cs) | 启动前初始化，记录启动/退出及未处理异常，向音频、Debug 插件和原生菜单注入 Logger；桌面退出后释放工厂并刷新文件。Serilog 自身写入故障输出标准错误，初始化目录失败明确退出，不静默切换目录 |
| [AvaloniaLogSink](RailDisplay/Logging/AvaloniaLogSink.cs)、[HotReloadHandler](RailDisplay/HotReloadHandler.cs) | 将框架消息与热重载诊断送到同一日志，保留区域、来源及消息参数；调整框架过滤改 `IsEnabled`，调整级别映射改 `ToLogLevel`，同步日志自检 |
| [DebugPluginLoader](RailDisplay/Debug/DebugPluginLoader.cs)、[DebugPlugin](RailDisplay.Debug/DebugPlugin.cs)、[CtcsDebugSimulation](RailDisplay.Debug/CTCS/CtcsDebugSimulation.cs) | 固定插件入口传入日志工厂，记录初始化、启停、设置窗口及模拟异常。改变入口参数必须同步加载委托与插件 `Create`，运行 `--debug-ui`、`--shutdown` 检查生命周期 |
| [NativeDebugMenu](RailDisplay/Debug/NativeDebugMenu.cs)、[Win32DebugMenu](RailDisplay/Debug/Win32DebugMenu.cs) | 启动入口传入 Logger，Win32 菜单回滚及释放失败记录错误码，保留原有异常行为；修改后在 Windows 检查原生菜单 |
| [DeviceAudioController](RailDisplay.Infrastructure/Controllers/Audio/DeviceAudioController.cs)、[Ctcs2_200HAudioController](RailDisplay.Infrastructure/Controllers/Audio/CTCS/Ctcs2_200HAudioController.cs) | 注入设备 Logger，记录资源加载及播放故障，保留现有异常传播和失败请求行为；新增设备沿用基类接线，运行普通自检的可控音频检查 |

后续需要记录日志的外层模块通过构造函数接收 `ILogger<T>`，由 App 创建对应 Logger；调用使用固定消息模板，例如 `logger.LogError(error, "音频资源加载失败：{SoundName}", name)`。不新增转发用日志接口，不在控件创建日志工厂，不逐帧写速度或重复告警。无参 App 和可选 Logger 参数仅方便设计器与独立检查，实际桌面启动使用已注入的实例。

例如 macOS / 终端设置 `RAILDISPLAY_LOG_LEVEL=WARN dotnet run --project RailDisplay/RailDisplay.csproj`，PowerShell 先设置 `$env:RAILDISPLAY_LOG_LEVEL="WARN"` 再启动应用。变量由进程启动时读取，修改后需要重启；从 IDE 启动时设置到相应运行配置的环境变量。日志标签同样采用 Log4j 名称（TRACE / DEBUG / INFO / WARN / ERROR / FATAL）；OFF 不创建日志文件。

[日志自检](checks/Logging/LoggingChecks.cs) 纳入普通控制台入口，覆盖构建默认级别、Log4j 名称、全部门槛及 OFF、5 MiB 轮转与 14 份保留、中文落盘、异常堆栈、Avalonia 参数、框架低级别刷屏过滤、热重载通知、完整来源名、末尾属性块不输出、正文花括号保留、运行期间刷新、重开追加、文件句柄释放及非法目录不覆盖现有文件，不写入真实用户日志目录。操作系统强制终止、原生崩溃及磁盘故障不能保证最后一条日志可用；未观察任务异常仅在运行时触发相应事件后记录，不替代调用方观察任务。

`CtcsController` 对外保留原有接口，内部组合两个独立逻辑对象：运行逻辑保存本会话唯一的运行快照、制动锁存和预测器，设备逻辑保存本会话唯一的交互导航与历史。运行数据更新不会重建设备逻辑，按键与导航不会重建运行逻辑；模式根在控制器中结合最新 `Data.ControlMode` 读取。运行消息的确认会发布新运行快照，因此消息确认状态归运行逻辑；设备按键可以经控制器调用确认入口。后续增加运行计算改 `CtcsCore`，增加交互行为改 `CtcsDevice`，涉及两侧的动作在控制器接线。三者均位于 Core，没有新增项目、接口层或外部依赖。

## CTCS 按键标识与入口

[CtcsButton](RailDisplay.Core/Models/CTCS/CtcsButton.cs) 枚举定义 19 个实体按键：8 个功能键、10 个数字键和 1 个警惕键。名称按用户提供的键面内容记录；枚举数值仅用于程序内部，不是通信协议编号。

| 标识 | 键面名称 |
| --- | --- |
| `F1`～`F8` | F1～F8，功能键 |
| `Digit0` | 0 |
| `Digit1` | 调车/1 |
| `Digit2` | 目视/2 ABC |
| `Digit3` | 启动/3 DEF |
| `Digit4` | 缓解/4 GHI |
| `Digit5` | 上行/5 JKL |
| `Digit6` | 下行/6 MNO |
| `Digit7` | 确定/7 PQRS |
| `Digit8` | 8TLV |
| `Digit9` | WXYZ |
| `Vigilance` | 警惕/字母 |

[CtcsController.KeyHandle](RailDisplay.Core/Controllers/CTCS/CtcsController.cs) 是控制器统一按键入口，通过 [ICtcsController](RailDisplay.Core/Controllers/CTCS/ICtcsController.cs) 提供，返回是否已处理。它经设备逻辑调用共用 [KeyHandler](RailDisplay.Core/Controllers/Interaction/KeyHandler.cs)：初始化时复制 `按键 → Func<CtcsController, bool>` 绑定表，拒绝非法标识、重复键及空处理器；运行时拒绝非法按键，无绑定返回 `false`，处理器异常向调用方传播。修改原始绑定表不会影响已初始化的处理器。

绑定委托收到本次控制器，读取最新的 `Data.ControlMode` 和 `CurrentInteraction.Step`，决定是否处理以及进入哪个状态。处理器调用控制器的导航入口即可，不自己管理返回栈；同一份只读绑定表可以用于多个控制器，各实例的导航状态独立。处理器应在提交操作前完成本次业务校验，分发器不替业务操作回滚多次调用。

[App](RailDisplay/App.axaml.cs) 把 `FunctionKeyMenu.NavigationBindings` 交给 Core 的 `CtcsNavigationKeys.Create`，再将处理器注入控制器；无参控制器仍使用空表，供独立业务检查使用。运行配置现已接入已有功能菜单的进入步骤、“返回”和“取消”；后两者均通过 `BackItem` 显式绑定 `Back()`，返回上一层，默认不保留输入，不提交业务数据。只切换交互步骤，不改变实际运行模式、等级、音量或制动状态。确定、删除、向上/向下、列车编组选择、音量/亮度调整、警惕等尚无动作规则，保持未绑定。后续规则写在 Core 对应系统目录，由 App 选择并接线，不在界面或组装中编写业务判断。取消菜单的调整同步 `CtcsDisplayChecks` 的全部取消路径检查，以及 `CtcsKeyUiChecks` 的 Debug 取消与 DMI 刷新检查。

`KeyHandler` 可在构造时接收纯查询条件 `canHandle(key, context)`；`CanHandle` 先确认有绑定，再检查最新模式与步骤，不执行处理器。`KeyHandle` 使用同一条件，禁用的键不会触发动作。控制器和 ViewModel 暴露 `CanHandle`，DMI 和 Debug 以此同步启用状态，不能通过执行一次动作来猜测键是否可用。当前导航可用性只按菜单步骤与返回历史判断，各运行模式共用配置，模式专属准入条件仍待用户提供。

[CtcsInteractionState](RailDisplay.Core/Models/CTCS/CtcsInteractionState.cs) 保存业务交互步骤 `Step` 和未确定的输入 `Input`，不包含页面名称、控件或绘制坐标。`Step == null` 表示当前运行模式的默认内容；开机数据默认为 `Standby`，现有模式文字显示“待机”，不再另设 `Idle` 步骤。`CurrentInteractionRoot` 从最新 `Data.ControlMode` 派生，模式不保存在返回栈中。

[CtcsInteractionTree](RailDisplay.Core/Models/CTCS/CtcsInteractionTree.cs) 定义现有功能菜单对应的 22 个步骤，以及关机和全部运行模式根。各模式引用同一份只读 `FunctionSteps`；关机根无子步骤，仅定义结构，尚无电源转换规则。树描述功能归属，不表示各模式已允许进入所有步骤，也不替代实际访问历史。展示菜单的 `TargetStep` 关联这些步骤，不由 Core 读取桌面菜单来决定操作。

共用 [InteractionNavigator](RailDisplay.Core/Controllers/Interaction/InteractionNavigator.cs) 只提供两个导航操作：

- `Next(target)`：将当前完整状态压栈，再进入显式提供的目标。CTCS 控制器拒绝空目标、未定义步骤、根步骤及空引用输入；目标的初始输入由处理器提供，不按枚举顺序推算下一步。
- `Back(count: 1, keepInput: false)`：默认回退一层，清空返回目标步骤的未确定输入；`keepInput: true` 时恢复该目标原先保存的输入，不把离开页的输入带到目标页。按用户确认，次数 ≤0 时按一次处理；超过历史深度时返回最早可返回状态，空栈返回 `false`。不提供设根、回根或单独清除历史接口。

控制器的 `CurrentInteraction` 和 `History` 经设备逻辑读取导航器，只有一个状态来源。`History` 为最早到最近排列的只读快照，末项是上一步，不含当前状态；前进和返回发布新集合，旧集合不随之后操作变化。共用导航器要求状态本身不可变，包含集合的设备状态须自行复制或只读化；输入清理委托只产生新状态，不修改历史快照。外部 `Update` 不重置步骤、输入或历史，每个控制器实例独立持有导航状态，返回后仍采用最新运行模式。

[CtcsViewModel.KeyHandle](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs) 将按键交给 Core，已处理时通知交互、模式根、历史及 `FunctionKeys` 刷新。`FunctionKeys` 是固定 F1～F8 的只读显示快照，每项含实体键、当前文字与可用状态；文字来自当前步骤菜单，可用性来自 Core。DMI 保留原 62×60 的八格位置，F1～F8 字体加粗、共享分隔线为 2px；只有无内容的格子通过 `CtcsFunctionKeyState.IsEmpty` 使用现有 `BlackGrayBrush` 深灰背景，有内容的格子保持原背景，与动作是否实现无关。判断空白改该显示模型，颜色、边框及绑定改 `CTCS2_200H.axaml`，同步显示和图形检查。整屏 `IsHitTestVisible=false`、`Focusable=false`，功能格和文字滚动箭头均不接点击或触摸事件；输入由 Debug 接入。D6 在指定非根交互步骤显示页面标题；根状态下 D 区计划刻度仅 FS/PS 显示，其他输入内容尚未接入。

[FunctionKeyMenu](RailDisplay/Models/CTCS/FunctionKeyMenu.cs) 已共用 `CtcsButton`，描述 F1～F8 菜单层级和目标步骤。调整实体按键改枚举并同步本表；调整步骤改状态模型、共享树及菜单关联；调整导航或分发同步 [InteractionChecks](checks/Interaction/InteractionChecks.cs)，由 Core 和普通自检入口共同执行；具体业务规则同步 [CtcsCoreChecks](checks/CTCS/CtcsCoreChecks.cs)，菜单和通知变化同步 [CtcsDisplayChecks](checks/CTCS/CtcsDisplayChecks.cs)。检查中的按键绑定仅为可控测试样例，不是正式按键规则。

[CtcsKeyUiChecks](checks/Rendering/CtcsKeyUiChecks.cs) 由 Debug 构建的 `--debug-ui` 和 `--pixels` 入口执行，验证原生/像素两种显示在 320×240、640×480、1280×960 下的 DMI 不参与指针命中、鼠标及触摸事件不改变状态、仅空白功能格使用深灰背景，以及动态加载 Debug 的 19 个枚举绑定与 DMI 文字刷新。修改显示绑定、输入边界、插件入口或刷新订阅时同步此检查；需要可用图形环境。

## 本地 Debug 手柄

本地 Debug 已移入独立的 `RailDisplay.Debug` DLL。Debug 构建会生成并复制 DLL 和调试符号到主程序输出目录，主程序通过“Debug → Simulation”、F12 或“Debug → Debug Setting”首次打开时才加载；不引用插件类型。DLL 不存在时主程序仍能启动，不显示 Debug 菜单。Release 不复制或加载插件，也不包含 Debug 菜单入口。

关闭调试窗口立即停止模拟，保留速度、手柄位置和现有业务状态；再次打开后继续，不重复生成样例数据，也不补算暂停时间。关闭主窗口会释放插件、声音并退出整个程序。停用会停止任务和释放窗口；DLL 保留在进程中，不做程序集热卸载或运行中替换。

Windows 通过 Win32 `HMENU` 在系统标题栏下显示传统原生菜单；macOS 通过 `NativeMenu` 显示顶部系统菜单。菜单不进入 CTCS 的 640×480 画面，Windows 首次挂载菜单时补足外框高度。macOS 挂载后保持根菜单实例不变，释放时只移除自己的 Debug 菜单项，避免退出时更新了不匹配的菜单。

Debug 菜单使用英文，只保留 `Simulation` 和 `Debug Setting`，不再提供强制重绘菜单项。`Debug Setting` 可编辑目标速度、允许速度（km/h）和目标距离（m）；点击 `Apply` 后从最新快照统一提交，经过 Core 校验。目标速度和目标距离留空表示未提供；目标速度存在时仍须满足 Core 的关联校验。关闭设置窗口不影响正在运行的模拟。SBI、EBI、TSM 暂保留现值，后续依据用户提供的规则自动计算，不在此处虚构公式。

模拟窗口下方第一排为 `F1–F8`，第二排为 `1–9、0、警惕`，19 键均通过 `PressButtonCommand` 交付 `CtcsButton`，调用主程序提供的同一个 `KeyHandle`；未配置的键禁用。Debug 窗口内键盘 F1～F8、主键盘和小键盘数字也映射到对应实体键，已有手柄快捷键保留。数字输入和警惕业务尚未定义，目前禁用；不虚构警惕的键盘快捷键。

固定插件入口接收按键处理与可用性查询两个窄回调，并返回刷新命令可用性的动作。宿主加载后只订阅一次 `FunctionKeys` 变更，Debug 输入后刷新 DMI 文字及 Debug 命令可用性，外部状态更新也触发刷新；释放时解除订阅。插件继续只引用 Core、日志抽象和 UI，不引用桌面项目。
左侧功率从下到上为 0、P1～P7，右侧制动从上到下为 0、B1～B7、EB，均可用鼠标拖动。默认均为 0，方向向前。
窗口有焦点时，A 增加功率、D 减少功率，W 向前、S 向后，`;` 减少制动、`'` 增加制动。点击中间箭头切换方向。
功率非零时箭头显示红框并禁止换向；有制动时有效功率为 0，功率手柄位置保留，制动回到 0 后恢复有效功率。
本地速度模拟已接入 CTCS 核心输入，速度以 km/h 表示，上限 400；双零位保持速度，制动最低减至 0，EB 退档立即改用当前档位，向后牵引同样显示速度绝对值。只更新速度，不将 B1～B7 映射为 CTCS 五档制动状态，也不发送游戏操作。

各功率档基础加速度为 P1～P7：0.08、0.16、0.24、0.32、0.40、0.48、0.56 m/s²；各制动档基础减速度为 B1～B7：0.10、0.20、0.30、0.40、0.55、0.70、0.85 m/s²，EB 为 1.20 m/s²。使用用户提供的目标值，不从允许范围随机采样。

| 速度 km/h（下限包含，上限不包含） | 牵引 Kp | 常用制动 Kb | EB Keb |
| --- | --- | --- | --- |
| 0～40 | 1.00 | 1.00 | 1.00 |
| 40～80 | 0.90 | 1.00 | 1.00 |
| 80～120 | 0.75 | 0.98 | 1.00 |
| 120～160 | 0.55 | 0.95 | 1.00 |
| 160～200 | 0.35 | 0.90 | 0.95 |
| 200～250 | 0.20 | 0.85 | 0.90 |
| 250～300 | 0.15 | 0.80 | 0.85 |
| 300～350 | 0.10 | 0.75 | 0.85 |
| 350～400（包含 400） | 0.05 | 0.70 | 0.80 |

模拟按显式经过时间推进，跨速度段时分段积分；减速到边界后进入下一低速段。插件每约 50ms 调度一次，使用单调时钟实际经过时间；关闭手柄窗口或退出应用时取消并等待任务。热重载保留当前速度。当前没有游戏数据源；以后接入时需互斥切换数据源，并停止本地推进。

| 位置与入口 | 职责及后续调整 |
| --- | --- |
| [DebugViewModel](RailDisplay.Debug/ViewModels/DebugViewModel.cs) 的 `Power`、`Brake`、`HandleKey`、`ToggleDirectionCommand` | 输入范围校验、有效功率、换向及键位；修改规则同步更新窗口提示和 DebugChecks |
| [DebugWindow.axaml](RailDisplay.Debug/Views/DebugWindow.axaml)、[后台代码](RailDisplay.Debug/Views/DebugWindow.axaml.cs) | Win10 式浅色直角布局：两侧手柄、中央换向、大字档位和底部有效功率；不显示标题、顶部说明或键位提示，快捷键保留。尺寸、颜色及外观改 XAML，事件转交在后台代码，不加入速度算法 |
| [MainWindow.axaml](RailDisplay/Views/MainWindow.axaml) | 仅承载 CTCS 屏幕，不放自绘菜单；调整窗口和屏幕布局在这里 |
| [NativeDebugMenu](RailDisplay/Debug/NativeDebugMenu.cs)、[Win32DebugMenu](RailDisplay/Debug/Win32DebugMenu.cs) | 原生 Debug 菜单和窗口关闭时的释放；文字、macOS 菜单改前者，Windows 菜单句柄和消息分发改后者；动作由 App 注入，两平台须同步菜单项与 DebugUiChecks |
| [TrainMotionSimulation](RailDisplay.Core/Controllers/Simulation/TrainMotionSimulation.cs) 的 `Acceleration`、`Advance` | 唯一速度计算来源、档位和速度系数、分段积分、范围校验；调整目标值或分段改这里，同步 TrainMotionChecks 和本文系数表 |
| [CtcsDebugSimulation](RailDisplay.Debug/CTCS/CtcsDebugSimulation.cs) 的 `Advance`、`RunAsync` | 读取本地手柄、按实际经过时间推进，调用注入的读取、提交和预警推进入口；不引用桌面 ViewModel。调度调整改这里，不在窗口计算 |
| [DebugSettingsViewModel](RailDisplay.Debug/ViewModels/DebugSettingsViewModel.cs)、[DebugSettingsWindow.axaml](RailDisplay.Debug/Views/DebugSettingsWindow.axaml) | 三项设置的编辑、解析、错误反馈及原子提交；新增输入同步 Core 校验、窗口绑定和 DebugChecks。布局在 XAML，修改提交行为改 `Apply`，始终保留最新速度及其余状态 |
| [DebugPlugin](RailDisplay.Debug/DebugPlugin.cs) 的 `Create`、`ShowAsync`、`ShowSettings`、`StopAsync` | 插件入口、单窗口、模拟启停与资源释放；`Create` 接收数据、预警推进、按键处理和可用性查询回调及日志工厂，返回打开动作、按键刷新动作和释放对象。改签名同步宿主加载委托，改按键同步 DebugChecks；运行 `--debug-ui` 和 `--shutdown` 检查生命周期 |
| [DebugPluginLoader](RailDisplay/Debug/DebugPluginLoader.cs)、[项目配置](RailDisplay/RailDisplay.csproj) | 只加载应用目录内固定的 `RailDisplay.Debug.dll`；构建引用不参与桌面编译，生成后复制插件。入口签名变更须同时更新插件 `Create`、加载器与生命周期检查；新增实际依赖时同步检查发布文件 |
| [App](RailDisplay/App.axaml.cs) 的 `ShowDebugWindow`、`ShutdownAsync` | 接线、原生菜单与 F12、关闭主窗口退出程序；退出前等待插件和声音释放。菜单栏应用名在 [App.axaml](RailDisplay/App.axaml) 的 `Name` 修改 |
| [DebugChecks](checks/DebugChecks.cs)、[检查入口](checks/Program.cs) | 正常档位、上下限、非法输入不改变状态、制动覆盖功率及鼠标/键盘共用换向；普通自检自动执行；[TrainMotionChecks](checks/TrainMotionChecks.cs) 由 Core 和桌面入口复用，覆盖全部系数、边界、跨段步长一致性和非法输入。布局与焦点另需实际窗口检查 |

## 界面热重载

Debug 构建使用 HotAvalonia 3.1.5 监听 `.axaml` 修改，使用与 Avalonia 相同版本的 `Avalonia.Markup.Xaml.Loader` 12.1.4 加载界面。原来的 `MetadataUpdateHandler` 只响应 .NET 代码更新，不能加载 XAML；因此新增这两个仅用于 Debug 的依赖，不自行编写监听器或 XAML 编译器。Release 不引用它们。

首次接入后需要重新构建、启动一次应用；之后在 Rider 正常运行或调试并保存 `.axaml` 即可触发 XAML 热重载。C# 方法体改动仍需要 IDE 的 .NET Hot Reload 或 `dotnet watch --project RailDisplay/RailDisplay.csproj`；不受 .NET 热重载支持的结构修改需要重启。此配置未增加图片、音频等二进制资源的热替换能力。

| 位置与入口 | 职责及后续调整 |
| --- | --- |
| [RailDisplay.csproj](RailDisplay/RailDisplay.csproj) 的 Debug 依赖组 | 热重载库及运行时 XAML 加载器；升级 Avalonia 时同步 Loader 版本，并实际验证保存 XAML 后的更新 |
| [HotReloadHandler](RailDisplay/HotReloadHandler.cs) 的 `UpdateApplication`、`ForceRedraw` | C# 热更新后重绘所有窗口，保留原 ViewModel 和业务快照；不再提供手动重绘菜单项 |
| [PixelDisplay](RailDisplay/Controls/PixelDisplay.cs) 的 `ForceRedraw` | 释放旧像素帧并立即生成新帧；修改此路径同步验证原生/像素模式切换及不同窗口尺寸 |
| [DebugUiChecks](checks/Rendering/DebugUiChecks.cs) | 验证原生菜单动作、重复重绘、布局变化、像素分辨率和业务状态保留；Windows 还检查实际 HMENU 和 WM_COMMAND 分发 |

单独验证菜单、重绘及插件启停：`dotnet run --project checks/CtcsChecks.csproj -- --debug-ui`（Debug 构建，需要图形环境）；`--pixels` 也会先执行这些检查。整程序退出检查单独运行 `dotnet run --project checks/CtcsChecks.csproj -- --shutdown`，实际打开主窗口和插件后关闭主窗口，验证全部窗口关闭且正常退出。实际热重载另需在运行时修改并保存一处 XAML 文字或尺寸，确认显示更新；修改后还原，并检查速度、手柄、菜单仍可使用。自动调用菜单动作不能代替原生菜单鼠标点击、键盘导航和多 DPI 的人工验证。

## 设备声音系统

每个屏型使用一个音频控制器，当前为 [Ctcs2_200HAudioController](RailDisplay.Infrastructure/Controllers/Audio/CTCS/Ctcs2_200HAudioController.cs)。在该类的 `Sounds` 列表中配置名称、资源路径、播放方式和重复间隔；列表越靠前优先级越高，每条声音一个优先级。真实音频尚未提供，列表保持为空，应用不会自行发声。

名称可以是任意非空字符串，不限于 `S0` 或 `A1`；设备内唯一、大小写敏感，不同设备允许重名。资源路径相对于应用目录，必须是目录内的 WAV。将实际文件放入 `RailDisplay/Assets/Audio/CTCS2_200H/` 后，对应路径填写 `Assets/Audio/CTCS2_200H/文件名.wav`，项目会复制到构建和发布目录。首版读取 PCM / IEEE float WAV，不接系统压缩音频解码器。

| 模块及入口 | 职责 | 后续调整与关联验证 |
| --- | --- | --- |
| [SoundDefinition](RailDisplay.Core/Models/Audio/SoundDefinition.cs)、[IDeviceAudioController](RailDisplay.Core/Controllers/Audio/IDeviceAudioController.cs) | 配置及设备声音接口；`Once`、`Loop`、`Interval` 三种方式 | 新增字段或操作同步更新调度、播放实现和音频自检；`RepeatInterval` 为正数 `TimeSpan`，仅间隔重复使用 |
| [AudioScheduler](RailDisplay.Core/Controllers/Audio/AudioScheduler.cs) | 单设备优先级、请求去重、循环/间隔计时及结束结果 | 规则调整改这里；时间由调用者显式传入，核心检查不需要窗口或声卡 |
| [DeviceAudioController](RailDisplay.Infrastructure/Controllers/Audio/DeviceAudioController.cs) | 串行操作、异步播放协调、设备音量和释放 | 更改调度推进或生命周期改这里，同步可控播放替身检查；软件推进约每 10ms 一次，不依赖 UI 刷新 |
| [NAudioOutput](RailDisplay.Infrastructure/Audio/NAudioOutput.cs)、[IAudioOutput](RailDisplay.Infrastructure/Audio/IAudioOutput.cs) | WAV 加载、平台输出、完成/失败通知和文件资源释放 | 新增平台改播放适配；修改后运行普通检查及该平台 `--audio` 真实播放检查 |
| [Ctcs2_200HAudioController](RailDisplay.Infrastructure/Controllers/Audio/CTCS/Ctcs2_200HAudioController.cs) | CTCS2-200H 专用音频配置 | 添加/替换音频或调整顺序改 `Sounds`；新设备有实际代码后继承同一基类，不复制调度规则 |
| [App](RailDisplay/App.axaml.cs)、[MainViewModel](RailDisplay/ViewModels/MainViewModel.cs) | 创建实例、传递 Core 接口和退出释放 | `MainViewModel.CtcsAudio` 是本设备声音入口；不在控件中创建输出，不通过热重载重新配置声音 |

调用 `PlayAsync(name)` 的返回任务持续到整个请求结束，结果为 `Completed`、`Stopped` 或 `Interrupted`；循环/间隔请求不会在开始发声时完成。调用者必须观察任务，以接收播放失败。`StopAsync(name)` 取消该名称的播放、排队和重复计时，`StopAllAsync()` 清空本设备；`SetVolumeAsync(float)` 设置 0～1 的有限数音量，默认 1，静音不暂停进度。音量只按设备提供，不单独调节每条音频。

设备内只播放一个声音，高优先级立即打断，低优先级排队；不同设备可以同时播放。循环每轮结束后重新竞争；间隔重复从处理播放完成通知时开始计时，等待期间让出播放位置。没有同级优先级，高优先级循环可能持续使低优先级等待。名称查询平均 O(1)，普通排队/取出 O(log n)，取消指定排队请求 O(n)。每个名称最多一个有效请求，取消会实际移除队列条目，不积累失效条目。

两项用户暂定规则在核心中保留 TODO / `shortcut:`：有效同名请求忽略重复触发并共享结束结果；被高优先级打断的请求彻底结束，包括循环及间隔重复，需重新调用才再播放。确认不同规则后修改 `AudioScheduler` 并同步音频检查。

实际播放使用 NAudio 3.1.0 的 `NAudio.Wasapi`（Windows）与 `NAudio.Alsa`（Linux），不引入 MIDI 或完整媒体栈。Windows 优先；Linux 需要系统提供 ALSA / libasound。macOS 暂不支持实际音频，`IsSupported` 返回 false，调用播放会明确抛出 `PlatformNotSupportedException`，启动不创建声卡设备，界面仍可运行。输出失败会结束所有有效请求并传播异常，不自动重试；资源加载失败只拒绝本次请求，保留当前有效播放。

当前仅提供接口，不连接 CTCS 状态触发，也不增加试听或设置界面。具体声音业务含义和触发规则仍由维护者提供。

## CSG 显示条件

CSG（包括允许速度端头）仅在完全监控 FS 模式显示，CSM／TSM 均适用；其他模式全部隐藏。显示条件唯一入口为 [CtcsViewModel](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs) 的 `ShowCsg`，通过现有页面绑定传给 `SpeedScale.ShowCsg`。后续调整模式条件改此属性并保留 `Refresh` 的通知，同步 [CtcsDisplayChecks](checks/CTCS/CtcsDisplayChecks.cs) 的全部模式及区段往返检查，执行普通自检；实际页面绑定由像素检查验证。

## A1 制动预警

按引用对话最终方案显示四档正方形：`Tint≥8` 为 5px，`4<Tint<8` 为 27px，`0<Tint≤4` 为 **41px**（用户确认四舍五入），`Tint=0` 为 54px；达到干预速度为红色最大图标。CSM 在 Tint>8 秒隐藏、等于 8 秒显示；TSM 在 Tint≥8 秒显示最小图标。CSM 正常灰色、TSM 正常黄色，超过允许速度且未达到 Vint 为橙色。RSM 低于释放速度黄色，达到释放速度红色最大图标。仅 FS 或有连续控制曲线的引导模式显示。

输入由 [CtcsData](RailDisplay.Core/Models/CTCS/CtcsData.cs) 同批提供：`BrakeWarningTime` 单位秒，非负数，null 表示数据不足，正无穷表示本轮预测不会冲线；速度字段 `SbiSpeed`、`EbiSpeed`、`ReleaseSpeed` 单位 km/h，均非负有限数，null 表示未提供；SBI 不得大于 EBI。`MonitoringSection` 可显式指定 CSM/TSM/RSM，RSM 必须提供释放速度；null 沿用临时距离区段判定。`HasContinuousControlCurve` 为引导模式的曲线标记。`BrakeWarningInterventionSpeed` 按用户要求暂时固定取当前 `EbiSpeed`，用于 A1 的 Tint 预测和颜色判断，缺失 EBI 时不回退 SBI。速度表仍使用 `InterventionSpeed`（低于 SBI 取 SBI，其余取 EBI，等值点暂归 EBI）；RSM 在 Tint≥8 秒隐藏是方案中的暂定边界，代码保留 `shortcut:`，正式规则确认后替换。

本地预测由 Core 的 `CtcsController.AdvanceBrakeWarning(elapsed)` 显式推进：进入 FS 时记录首个速度，随后至少间隔 1 秒记录一次实际观测，只保留当前及前 4 次速度/时间点（最多 5 个）；各段速度差除以实际时间差后，仅对非零加速度取算术平均（最多 4 段）；零值不计入总和与分母，窗口内所有区间均为零时平均值取 0，正负非零值都参与且可以相互抵消，不使用近零阈值，内部统一使用 m/s 和 m/s²。调度延迟不补造观测；FS 内切换监视区保留历史，离开 FS 清空，重入后重新采样。仅有进入 FS 时的首个速度或缺失当前 Vint 时 Tint 为 null；第 1 秒有两个速度点即可开始预测，不等待窗口凑满；已经达到 Vint 时可直接得知 Tint=0。数据充足且低于 Vint 时，平均加速度大于 0 则 Tint=(Vint-v)/a，否则为正无穷。该算法按用户确认的模拟假设保持本次 Vint 和平均加速度，每次推进读取最新速度与 EBI，不推算前方干预曲线，不视为 PDF 已确认的正式 ATP 预测算法。

本地模拟每次推进速度后调用同一个预测入口，即使双零位速度不变也持续推进；显示层读取同一核心快照，不自行倒计时。正无穷在 TSM 显示最小图标，在 CSM 隐藏；Tint 缺失或当前区段所需速度缺失时留空。引导模式不运行本地 FS 预测，但仍可按已有显示规则接收外部 Tint。未来游戏接入时由会话协调入口先提交同批输入，再使用单调时钟的实际经过时间推进预测，并在新会话使用新控制器，禁止沿用上一主机采样。速度表的 SBI 告警优先级独立，A1 超过 SBI 但未到 EBI 时仍可显示橙色；A1 的 Vint 暂取 EBI，速度表继续沿用原有 Vint 选择规则。

| 位置与入口 | 职责及后续调整 |
| --- | --- |
| [CtcsData](RailDisplay.Core/Models/CTCS/CtcsData.cs)、[CtcsController.Update](RailDisplay.Core/Controllers/CTCS/CtcsController.cs) | 输入、Vint 与整批校验；改字段或边界同步核心检查，保持非法输入不改变旧快照 |
| [CtcsBrakeWarningPredictor](RailDisplay.Core/Controllers/CTCS/CtcsBrakeWarningPredictor.cs)、[ICtcsController](RailDisplay.Core/Controllers/CTCS/ICtcsController.cs) 的 `AdvanceBrakeWarning` | 唯一 FS 当前及前 4 次速度的滚动采样、平均加速度及 Tint 预测来源；调整采样或公式改预测器，模式生命周期改 `CtcsCore`；同步 [BrakeWarningPredictionChecks](checks/CTCS/BrakeWarningPredictionChecks.cs)，由 Core 和普通入口执行 |
| [CtcsViewModel.AdvanceBrakeWarning](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs)、[CtcsDebugSimulation.Advance](RailDisplay.Debug/CTCS/CtcsDebugSimulation.cs) | 将速度推进和经过时间交给 Core，再通知绑定；调整本地调度改 Debug 模拟，保持匀速时仍推进；同步 [DebugChecks](checks/DebugChecks.cs) 接入检查 |
| [BrakeWarningDisplay.Calculate](RailDisplay/Models/CTCS/BrakeWarningDisplay.cs) | A1 尺寸、颜色、模式条件；改规则同步 BrakeWarningChecks；居中位置取整到设计像素，奇数边长两侧留白最多差 1px |
| [CtcsViewModel.BrakeWarning](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs)、[CTCS2_200H.axaml](RailDisplay/Views/CTCS2_200H.axaml) 的 A1 | 更新通知、正方形位置和颜色绑定，沿用 CTCS 资源及整屏缩放；改布局在页面中调整 |
| [BrakeWarningChecks](checks/CTCS/BrakeWarningChecks.cs)、[CtcsCoreChecks](checks/CTCS/CtcsCoreChecks.cs)、[PixelDisplayChecks.CheckBrakeWarningView](checks/Rendering/PixelDisplayChecks.cs) | 时间、速度、模式、区段边界与往返、非法输入，以及实际页面颜色/尺寸和原生/像素模式缩放；执行普通自检、Core 自检及 `--pixels` |

## A2 目标距离

A2 仅在完全监控 FS 模式的 TSM 区显示，数据来自 `CtcsData.TargetDistance`（米），不另存一份目标距离。白色光带宽 15px、最大高 172px，自下而上表示目标距离，达到 1000m 后保持满高，0m 时光带为空而刻度仍显示。正上方数字为 Arial 16px；小于 1000m 按 1m、达到 1000m 按 10m 向下取整，始终显示米且不补前导零，超出五位显示能力时保持最大可显示值 99990。显示格式不修改核心原值；其他模式、CSM 或无目标数据时，光带、刻度和数字一起留空，FS＋TSM 中的速度告警状态不影响显示。

布局入口为 [CTCS2_200H.axaml](RailDisplay/Views/CTCS2_200H.axaml) 的 `A2`：`TargetDistanceRuler` 宽 53px、高 172px、相对 A2 上 32px；数字上 0px、宽 53px、右对齐到光带右边缘。绘制由 [TargetDistanceScale](RailDisplay/Controls/CTCS/TargetDistanceScale.cs) 负责：光带左 38px、宽 15px；刻度右端为 30px，与光带间隔 8px，长刻度为 22×2px，短刻度为 12×1px，均为白色，不在刻度旁显示数字标签。调整整体位置或数字字体改页面；刻度、光带几何及坐标映射改控件；显示条件与数字精度改 [CtcsViewModel](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs) 的 `ShowTargetDistance`、`TargetDistanceText`，并保持 `Refresh` 中的绑定通知。

图4已提供，并按当前分段线性坐标：0～100m 占下方 2.3/3；100～1000m 占上方 0.7/3。1000m 在顶端，0m 在底端；保留 0、100、1000m 端点，另绘制 70、80、90、95m 和 200、400、600、800m 八根中间刻度，共 11 根；0、1000m 为长刻度，其余为短刻度。刻度位置和光带高度使用控件中同一个 `DistanceHeight` 映射，50m 高约 65.93px，100m 高约 131.87px，500m 高约 149.70px。A3 尚未绘制。修改映射或显示逻辑同步 [CtcsScreenChecks](checks/CTCS/CtcsScreenChecks.cs)，修改绘制同步 [PixelDisplayChecks](checks/Rendering/PixelDisplayChecks.cs) 的 `CheckTargetDistanceView`，执行普通自检和 `--pixels` 并检查实际窗口缩放；控件使用 53×172 设计坐标，整体缩放继续由 `PixelDisplay` 负责。

## D6 交互页标题

以下非根交互步骤在 D6 居中显示白色 16px 标题，并将背景设为 `BlackGrayBrush`：司机号、车次号、列车数据输入；模式功能选择、调车模式确认、目视行车确认；载频方式选择、上/下行载频确认；CTCS 等级选择及分别显示目标等级的 CTCS 0/2 级转换确认；其他选项、音量、亮度、软件版本、启动及缓解确认。映射入口为 [CtcsViewModel.D6PageTitle](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs)，同步 `Refresh` 通知；页面背景和居中文字布局位于 [CTCS2_200H.axaml](RailDisplay/Views/CTCS2_200H.axaml) 的 D6。未列出的交互步骤保持空白和原背景；非根状态隐藏 D 区计划刻度和 D7 底色。修改映射时同步 [CtcsDisplayChecks](checks/CTCS/CtcsDisplayChecks.cs) 与 [CtcsKeyUiChecks](checks/Rendering/CtcsKeyUiChecks.cs)；实际 UI 检查需图形环境。

## D1 距离标尺

D 区距离与速度计划刻度及 D7 底色仅在交互根状态下的完全监控 FS 或部分监控 PS 模式显示；条件由 [CtcsViewModel.ShowDistanceScale](RailDisplay/ViewModels/CTCS/CtcsViewModel.cs) 提供，页面将其绑定到 `D7.IsVisible` 和 `DistanceRuler.IsVisible`。修改显示模式或根状态条件时同步更新 [PixelDisplayChecks.CheckCtcsView](checks/Rendering/PixelDisplayChecks.cs) 与 [CtcsKeyUiChecks](checks/Rendering/CtcsKeyUiChecks.cs)，并运行 `--pixels`。

D7 背景使用 CTCS 资源中的 `MRSPBackgroundBrush`（`#0A1846`）；调整该区域绑定改 `CTCS2_200H.axaml`，调整共用颜色改 `CTCSColors.axaml`，并同步实际页面像素检查。

D1 距离标尺由 [DistanceScale](RailDisplay/Controls/CTCS/DistanceScale.cs) 绘制，字体为 Arial 11px；0 位于左端，1k 位于 D 区宽度的 1/3，后续倍增刻度等距排列，最大值位于右端。两端文字向内对齐以避免裁切。
设置 `CtcsViewModel.MaximumDisplayDistance`（单位米）为 `8000` 或 `16000` 切换量程，初始为 `8000`；当前没有点击切换或自动量程规则。该属性属于展示状态，不修改核心业务数据。调整绘制改控件，调整绑定或布局改 `CTCS2_200H.axaml`；同步更新屏型及像素检查。其他 D 子区尚未绘制。

各距离刻度的竖向参考线使用 `BlackGrayBrush`、1px 线宽，从 D2 底部延伸到 D7 顶端（D 区 y=55～280），不进入 D8 或 D6。页面将标尺控件放在 y=55，宽 243、高 245；控件内部为竖线单独保留右侧 1px：最右竖线占 D 区 x=241 的像素，x=242 与右边框 x=243 之间形成 1px 空白。横线不受此内边距影响，绘制至 x=242，紧贴右边框内侧。底部 20px 仅绘制 D1 距离文字，不绘制白色短刻度。调整竖线内边距改控件的 `distanceWidth`，调整整体范围改页面尺寸，调整颜色改 `GridBrush` 绑定，并同步像素边界检查。

D7 横向速度参考线也由同一个 `DistanceScale` 组件绘制，0 位于 D7 底边，向上每 50 km/h 一条，当前 300 顶线距 D7 顶端 35px，七条在此范围内等距排列。颜色与线宽沿用距离参考线，横线不留右侧空白。各线左端上方显示对应速度数字，沿用 D1 的字体与文字颜色，文字距左端 2px，按可见字形底部贴齐线条上沿，避免字体行高留白造成间隙。调整顶线位置改 `Render` 中的 `speedGridTop`，调整速度范围、间隔、文字位置或 D7 的 150px 高度也改此处，并同步页面 D7 布局及像素检查；B1 速度表保持现有组件。

## CTCS2/3 的软能力判断

- `CtcsLevel` 包含 0、2、3。Core 只校验是否为已定义等级，不判断窗口类名，也不根据屏型拒绝合法的 CTCS3 输入。
- `CtcsScreenProfile.CTCS2_200H` 当前默认 `SupportsLevel3: false`，保留该屏型原有的切换范围。需要允许 CTCS3 时修改配置，或在组装位置使用 `CtcsScreenProfile.CTCS2_200H with { SupportsLevel3 = true }`。
- 交互层读取 `CanSwitchToLevel3` 显示可用性，实际本地切换调用 `TrySwitchLevel(CtcsLevel.Level3)`；返回 `false` 时不修改状态。
- `Update` 接收到合法的 Level3 数据时仍可展示，即使该屏型未开放本地切换。软能力约束和数据有效性校验是两件事。
- 当前完成的是共用入口、等级表示和切换能力，不代表 CTCS3 专属行为已实现。具体差异规则提供后再放入共用控制器；新增菜单入口的键位与交互按实际需求实现。

## 验证

E17 当前显示电脑本地时间，共三行：宋体 12px 的“日期时间”标题、Arial 12px 的 `HH:mm:ss` 时间（24 小时制，时:分:秒）、Arial 12px 的 `yy-MM-dd` 日期。页面计时器每秒将同一份时间交给 `CtcsViewModel.UpdateDisplayTime`，页面脱离显示树时停止；更新不修改核心业务快照。调整布局与字体改 `CTCS2_200H.axaml`，调整格式改 ViewModel，调整刷新改页面后台代码，并同步屏型检查。未来接入游戏时间时，替换本地计时器入口，统一在 UI 线程调用 `UpdateDisplayTime`，不要让本地时间与游戏时间同时写入。

制动状态 `CtcsBrakeLevel.NoBrake = -1` 表示无制动，图标为空白；`ReleaseAllowed = 0` 表示允许缓解，两者语义不同。`CtcsData.BrakeLevel` 保留传入值，Core 的 `CtcsController.BrakeLevel` 合并传入状态与速度触发的锁存状态，取较强者；C9 经 `CtcsViewModel.BrakeLevel` 绑定该结果。除 CTCS-0、机信和隔离模式外，严格超过 SBI 锁存 `ServiceBrake`，严格超过 EBI 锁存 `EmergencyBrake`，EB 优先；等于阈值不触发对应升级，缺失阈值不按零处理。降速、阈值变化或进入排除模式均不清除锁存，排除模式不新增或升级制动；隔离模式同时关闭速度超限告警与本地 Tint 预测。暂不接入缓解操作，锁存仅随控制器重建清空，待用户确认缓解规则后替换。该结果仅驱动图标，不改变速度、手柄、牵引或本地制动模拟。调整触发与锁存改 `CtcsCore.ApplySpeedBrake`；调整显示接入同步接口、ViewModel 和 `CTCS2_200H.axaml`；调整图形改 `BrakeIndicator`。同步核心边界/模式/锁存检查、Debug 模拟隔离检查与 C9 页面绑定及图标像素检查。

```sh
dotnet build RailDisplay.sln
dotnet run --project checks/Core/CoreChecks.csproj
dotnet run --project checks/CtcsChecks.csproj
dotnet run --project checks/CtcsChecks.csproj -- --pixels
dotnet run --project checks/CtcsChecks.csproj -- --audio
```

两个检查项目都已加入解决方案。构建只编译，不执行检查；现有检查是控制台程序，不能用 `dotnet test` 代替。`--pixels` 需要图形运行环境，且只执行渲染检查。核心检查无需 Avalonia 或真实游戏。

普通检查包含 [音频核心检查](checks/Audio/AudioCoreChecks.cs) 和 [音频适配检查](checks/Audio/AudioOutputChecks.cs)，后者使用可控替身与明确标记的临时 WAV，不依赖声卡。`--audio` 单独执行低音量测试 WAV 的真实播放、多设备同时播放、音量和停止检查，需要 Windows 或可用 ALSA 的 Linux；macOS 会明确报告未执行。完成通知通过不等于听感验证，也不代表其他系统已验证。

核心检查覆盖等级 0/2/3、边界值、非法输入不改变状态、集合所有权和消息确认；桌面检查保留原有灯带、颜色、滚动、闪烁、菜单关系，并验证软能力开关；渲染检查加载实际 View，检查绑定及 320×240、640×480、1280×960 的绘制。

### GitHub Actions

[Release CI 工作流](.github/workflows/ci.yml) 在推送、Pull Request 和手动触发时运行，使用 .NET 10，在 `windows-latest` 和 `macos-latest` 上仅构建 Release，共两组任务。文件名使用小写 `ci.yml`，工作流显示为 `Release CI`，任务标识为 `build-and-check`，任务显示为 `Build and check (Windows)`、`Build and check (macOS)`；步骤统一使用英文动词短语，便于从日志定位失败阶段。

每组依次执行检出源码、安装 SDK、按 Release 配置还原依赖、构建解决方案、Core 自检、普通自检。构建使用 `--no-restore`，检查使用 `--no-build`，确保后续阶段验证本次构建结果。任一步失败即报告该组失败，但不取消另一平台的检查；每组限时 15 分钟，同一分支或 PR 的新运行会取消尚未完成的旧运行。工作流仅授予 `contents: read`，检出后不保留凭据，所用 Action 固定完整提交 SHA，并以注释记录版本。

CI 不执行需要图形环境或声卡的 `--pixels`、`--debug-ui`、`--shutdown`、`--audio`，也不代替实际窗口、原生菜单和听感验证。工作流只构建与检查，不发布安装包或创建 Release。

后续调整触发方式改工作流的 `on`，调整平台及显示名称改 `strategy.matrix.include`，升级 .NET 目标版本时同步 `dotnet-version` 与各项目的 `TargetFramework`；增加检查改 `steps` 并保持 Release 配置，升级 Action 时核对官方发布并同时更新 SHA 和版本注释，上述变化均同步本节。修改后在本地执行以下命令，再查看 GitHub Actions 中两组任务的结果。Action 用法参考官方 [checkout](https://github.com/actions/checkout) 和 [setup-dotnet](https://github.com/actions/setup-dotnet) 文档。

```sh
dotnet restore RailDisplay.sln -p:Configuration=Release
dotnet build RailDisplay.sln --configuration Release --no-restore
dotnet run --project checks/Core/CoreChecks.csproj --configuration Release --no-build
dotnet run --project checks/CtcsChecks.csproj --configuration Release --no-build
```

开发约束和交付要求见 [AGENTS.md](AGENTS.md)。
