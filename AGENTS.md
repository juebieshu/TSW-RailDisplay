# RailDisplay 开发规范

本文件适用于项目根目录及其子目录。开发前阅读本文件和本次涉及的代码；规范约束后续改动，不表示现有代码已经全部符合目标架构。

## 1. 已确认的产品方向

- RailDisplay 对接游戏中的 DLL，并在客户端内模拟系统运行。业务范围包括根据游戏速度、线路数据等计算监督曲线、允许速度、制动状态，不能将项目仅视作数据展示器。
- 当前屏型为 `CTCS2_200H`；后续计划支持 LKJ、CTCS3、CTCS2-200C、CIR。计划不等于当前任务，未进入实现范围的功能不预建空模块。
- 桌面端以 Windows 为主要开发、交付和验证目标；macOS 仅作为附带平台维持兼容，不要求与 Windows 功能对称，也不作为架构设计的基准。用户已取消 Linux 支持，已有 ALSA 代码暂留，不继续新增 Linux 适配。支持窗口缩放，当前优先鼠标操作；触摸为可能的后续需求。
- 实际音频当前优先 Windows，使用 NAudio 3.1.0；Linux 使用 ALSA 适配，macOS 音频按用户决定暂缓，不影响桌面启动。
- 游戏 DLL 在局域网广播 `raildisplay` 消息；客户端与 DLL 握手后，DLL 提供局域网 IP，用于区分安装 DLL 的游戏主机。
- Debug 功能位于 RailDisplay 内，使用两个简单滑块：牵引 `P1—P7`、制动 `B1—B7＋EB`。滑块仅驱动本地模拟和显示，不向游戏发送控制指令。
- 业务规则由用户提供。协议细节、算法、阈值和状态转移不得根据名称、截图、经验或现有临时实现自行补全。

## 2. AI 协作约定

- 子 Agent 默认显式指定 `gpt-6.1-sol`；简单任务可使用已确认不高于该档位的模型。禁止子 Agent 使用 Astra 系列，包括 `gpt-6-astra`，不得因任务困难、失败或模型不可用自动切换到 Astra。
- 不使用未指定模型的子 Agent 创建方式，以免继承主 Agent 的模型。工具要求独立上下文时，显式传递任务范围、必要文件和本规范。限制同样适用于子 Agent 再委派的任务。
- 可以并行委派独立检查、分析和开发；不得让多个 Agent 同时修改同一部分文件，主 Agent 负责核对和整合结果。
- 说明、讨论和文档默认使用中文。需要用户决定业务含义或有明显取舍的架构边界时，优先使用系统提问工具，给出推荐方案及影响；已确认的事项不重复询问。
- 对缺失规则只暂停依赖该规则的实现，继续可独立完成的工作。需要假数据时明确标注来源与用途，不把模拟假设写成已确认规则。

## 3. 目标架构与依赖方向

独立类库和接口边界是要求，不能仅靠同一程序集中的文件夹名称代替。采用以下结构。Core 已承接现有 CTCS 业务，Infrastructure 类库已建立并引用 Core，但通信实现仍待协议明确。

| 项目 | 职责 | 依赖限制 |
| --- | --- | --- |
| `RailDisplay.Core` | 业务模型、系统状态、监督与制动等计算、业务流程、接入所需的接口 | 不依赖桌面应用、通信实现、Avalonia、MVVM 框架或游戏运行时 |
| `RailDisplay.Infrastructure` | 设备音频控制器、实际音频播放，以及 DLL 通信、发现与握手、协议解析、连接管理等外部 I/O | 依赖 Core 的契约；不得依赖桌面应用 |
| `RailDisplay` | Avalonia 启动入口、ViewModel／显示控制器、页面、控件、资源及 Debug 交互 | 展示代码依赖 Core；仅启动组装位置使用 Infrastructure 的具体实现 |
| `RailDisplay.Debug` | 可选本地 Debug DLL：手柄窗口、假数据、模拟调度及启停 | 只引用 Core、日志抽象与必要的 UI 依赖；通过启动入口传入的窄回调交付数据，不引用桌面项目或 Infrastructure |
| `checks` | 控制台自检入口及检查代码 | 按检查对象引用项目；纯业务检查不启动窗口或连接真实游戏 |

编译依赖：`Infrastructure → Core`，`RailDisplay → Core`。桌面项目允许引用 Infrastructure 以完成启动组装，但其类型不得进入 ViewModel、显示控制器或控件。Core 不得反向引用外层项目，项目引用不得成环。

`RailDisplay.Debug` 项目独立输出为 `Debug.dll`。桌面应用在任意构建配置下扫描应用目录，只识别该固定 DLL；缺失时不显示 Debug 菜单且正常启动，Windows 的 `Always on Top`、`Hide Title Bar` 顶层菜单项仍显示；存在时先加载插件程序集读取 Debug 菜单文字，首次操作才创建插件并初始化样例数据。Debug 构建以 `ReferenceOutputAssembly=false` 构建并复制插件；非 Debug 发布清理复用输出目录中残留的插件、符号及热重载文件，Release 可加载发布后由用户放入目录的 `Debug.dll`。桌面代码不引用插件类型，不扩展为通用插件框架。停用释放窗口和模拟任务，不承诺程序集热卸载。

- 外部数据源、操作发送等需要替换或隔离的能力，通过 Core 所需的窄接口接入；接口定义业务所需能力，不暴露 Socket、Avalonia 对象或协议解析器。
- 依赖从构造函数传入，在应用启动入口统一组装。禁止在控件或 ViewModel 内创建通信客户端，禁止通过全局可变单例、静态服务容器或查找窗口获取业务依赖。
- 外层模块日志使用构造函数传入的 `Microsoft.Extensions.Logging.ILogger<T>`，启动入口创建 Infrastructure 的 `ApplicationLogging` 工厂，Serilog 负责本地文件输出；Core 不因日志接入增加框架依赖。Debug 插件仅引用日志抽象，由固定入口接收工厂；不得依赖 Serilog 或 Infrastructure。禁止全局静态 Logger；Avalonia 自身日志入口统一桥接，框架诊断至少 WARN；HotReload 完成通知是应用日志，保留 INFO，二者都遵守更高的全局门槛及 OFF。用户数据目录下 `RailDisplay/Logs` 按天及 5 MiB 轮转，保留最近 14 份；日志级别对齐 Log4j，Release 默认 INFO、Debug 默认 DEBUG，启动环境变量 `RAILDISPLAY_LOG_LEVEL` 可覆盖，支持 ALL/TRACE/DEBUG/INFO/WARN/ERROR/FATAL/OFF。调整默认值同步 README 与日志检查。退出时先释放插件和音频，再释放日志工厂；高频刷新不逐帧记录。
- 不要求每个内部类都有接口；纯计算函数和明确的内部实现可以直接使用。不为分层增加只有转发作用的服务、工厂或额外类库。
- DLL 运行在游戏中。桌面客户端不加载或依赖游戏程序集；通信协议需要双方共享代码时，再按实际兼容性提取最小协议契约，不要求 DLL 引用整个模拟核心。
- 声音契约、配置模型及纯调度在 Core；每屏型一个 Infrastructure 音频控制器，在类内配置有序音频列表，共用 `DeviceAudioController` 基类。具体输出适配通过 `IAudioOutput` 隔离，启动入口统一创建，ViewModel 仅使用 Core 的 `IDeviceAudioController`。
- 新增实际业务算法进入 Core，新增外部通信进入 Infrastructure。首次涉及相应职责时完成必要的类库建立和调用接入，不把新职责继续堆入桌面项目。
- 迁移同步更新项目引用、命名空间、绑定、调用方和自检。保留原有校验与错误处理，不留下两份同时生效的规则实现。

## 4. 职责拆分与多系统扩展

- 先按职责分目录，再按系统分类，例如 `Models/CTCS`、`Controllers/CTCS`、`ViewModels/CTCS`、`Controls/CTCS`；后续 LKJ、CIR 在相应职责目录下使用同级系统目录，有实际代码时再建立。
- Views 是例外：屏型文件直接放在 `RailDisplay/Views/`，例如 `Views/CTCS2_200H.axaml`，不增加 CTCS 或型号子目录。
- CTCS2/3 共用 `Core/Controllers/CTCS/CtcsController`，确有等级差异时根据 `CurrentLevel` 处理，不复制整套控制器。当前只迁移已有规则，不能声称已完成 CTCS3 专属业务算法。
- `CtcsController` 为统一调度入口，内部独立持有 `CtcsDevice` 和 `CtcsCore`。设备逻辑持有按键处理器、交互导航和历史，不持有运行数据；运行逻辑持有唯一 `CtcsData`、监视区、速度告警、制动锁存、预警预测及运行消息状态，不依赖设备逻辑。控制器不再复制两侧状态；运行模式与交互根的组合在控制器完成。新增运行规则进入运行逻辑或它按实际职责拆出的计算类，设备交互进入设备逻辑，跨职责操作由控制器调度，不通过 partial 文件共享全部内部状态。
- Core 接受已定义的 CTCS0/2/3 等级，不根据屏型硬性拒绝 CTCS3。界面切换由 `CtcsScreenProfile.SupportsLevel3` 和 `CtcsViewModel.TrySwitchLevel` 控制；能力开关不得取消输入有效性校验，也不拦截收到的合法等级数据。
- 同一业务规则只有一个实现来源。不同屏幕展示同一结果时复用计算；规则不同的系统不得为了复用而强行套用 CTCS 模型或相互调用页面。
- 不建立包揽所有设备、连接、业务状态和 UI 状态的万能 Controller、Manager 或 Utils。类名应能说明它处理的具体职责。
- 出现以下情况时必须在本次相关范围内拆分：承担互不相关的职责；相同规则分散实现；修改一个功能反复牵连无关模块；测试业务必须启动界面或接入游戏。
- 按职责和依赖拆分，不按文件行数机械拆分。不提前建立插件框架、通用规则引擎、深层继承体系或为所有未来屏型服务的抽象。
- 只有实际语义相同的部分才能共享。数据结构外形相似、按钮长得相似，不足以证明应该共用业务逻辑。
- ViewModel／显示控制器负责绑定状态与用户操作协调；业务核心负责系统状态和计算。滚动位置、选中项、颜色、闪烁相位、格式化文本等属于展示层。
- 核心返回有业务含义的状态，展示层映射为文字、颜色和图形。不得在核心返回画刷、控件、页面名称或屏幕坐标。

## 5. 计算、数据与状态

- 实现业务前明确输入、输出、单位、适用系统／模式、边界条件及用户提供的规则依据。缺少影响计算正确性的内容时先提问，不自行设计监督或制动算法。
- 区分游戏原始数据、核心计算结果、用户操作请求和 UI 临时状态。每项权威状态有明确归属，不让多个层各维护一份可独立修改的副本。
- 计算尽量采用显式输入与输出。时间、步长、线路数据等计算条件显式传入，使相同输入和状态能复现相同结果；业务推进不得依赖界面刷新次数、窗口是否可见或闪烁计时器。
- 有状态的系统模拟集中处理状态转移，不将同一状态机拆散到绑定转换器、绘制函数和网络回调中。
- 数据字段或契约文档明确单位、范围及缺失值含义。单位转换在明确边界完成；未知、未收到和无效值不得直接当作零。
- 校验外部输入和调试输入的范围、有限数、枚举、必填字段及关联约束。校验全部成功后提交同一批次状态，失败不得留下半更新的数据。
- 发布的数据快照不能被接收方随意修改。`record`、`init` 或 `IReadOnlyList` 不代表其内部集合自动不可变；在实际边界明确所有权或进行必要复制。
- 速度触发的制动仅供 C9 图标：Core `CtcsController.BrakeLevel` 合并输入与速度锁存状态，严格超过 SBI 为常用制动、严格超过 EBI 为 EB，EB 优先；CTCS-0、机信和隔离模式不新增或升级速度制动，已有锁存保留。隔离模式关闭速度超限告警与本地 Tint 预测。降速不自动解除，暂不接入缓解，锁存随控制器重建清空；后续缓解按用户规则接入。不得影响本地速度模拟或 Debug 手柄。
- 原始输入和派生结果必须能关联到同一主机会话与更新批次，不能把上一主机的速度与下一主机的线路拼在一起。
- Core 的 `CtcsCore` 中按距离边界判断 TSM 的逻辑是已标注的临时模拟，由 `CtcsController.Update` 调度，不是已实现的监督曲线。后续按用户规则替换，并保留相关边界检查。
- A1 制动预警使用同批 `BrakeWarningTime`（秒）、SBI/EBI/释放速度（km/h）、显式监视区和连续控制曲线标记。用户确认的本地 Tint 预测由 Core `AdvanceBrakeWarning(elapsed)` 显式推进：进入 FS 即持续至少每 1 秒采样，保留当前及前 4 次速度和实际时间，仅对已有相邻区间的非零加速度取平均（最多 4 段），全部为零时取 0；正负非零值均参与，不设置近零阈值；第 1 秒已有两次速度即可开始预测，不等待历史凑满；FS 内监视区切换不重置，退出 FS 清空。A1 的 Vint 按用户要求暂取当前 EBI（缺失时不回退 SBI），Tint 预测与图标颜色共用 `BrakeWarningInterventionSpeed`，速度表沿用原有选择规则；保持本次平均加速度和最新 EBI 预测冲线时间；已达阈值为 0，数据不足为 null，低于阈值且平均加速度≤0 为正无穷（本轮预测不冲线）。仅 Tint 允许这一正无穷语义，速度等输入仍必须有限。调度迟到不伪造采样，界面不自行倒计时；这是用户指定模拟，不是正式监督算法。四档设计尺寸为 5、27、41、54，第三档按用户确认四舍五入。显示逻辑在桌面 `BrakeWarningDisplay`，输入校验在 Core；SBI 等值点归 EBI、RSM 在 Tint≥8 秒隐藏是引用方案中的暂定边界，保留 `shortcut:` 待正式规则替换。
- 临时方案必须写明已知限制及替换条件，使用 `shortcut: <限制>，<何时替换>` 注释；不得用临时方案绕过输入校验或隐瞒缺失规则。
- 音频名称为设备内唯一、大小写敏感的任意非空字符串；优先级按各设备配置列表从上到下递减，不设置同级。同设备只播一个声音，高优先级立即打断，低优先级排队，不同设备独立播放。
- 音频支持单次、每轮重新参与调度的持续循环、播完后等待指定间隔的重复；等待间隔让出播放位置。有效同名请求暂时去重，被打断请求（包括重复声音）暂时彻底结束，两项保留 TODO / `shortcut:`，用户重新确认后再替换。
- 仅每设备总音量（默认 1、范围 0～1、必须有限），运行时调整立即生效；静音不暂停进度。首版资源为相对于应用目录的 PCM / IEEE float WAV，真实音频未提供，CTCS2-200H 配置为空；不虚构音频名称、优先级或业务触发。

## 6. DLL 通信与主机会话

- 已确定的流程为广播发现、客户端与 DLL 握手、接收用于区分游戏主机的局域网 IP、接收数据。具体传输方式、端口、消息格式、协议版本和时序尚未确定，实现前确认相应协议。
- IP 表示游戏主机在当前局域网中的地址；不得未经约定把它当作永久设备标识、客户端标识或游戏内设备编号。地址变化、多网卡和同机多个游戏实例的区分方式待协议明确。
- 发现、握手、连接状态、解析和重连由通信部分管理，不能分散在各个屏型中。每个会话的数据与生命周期隔离；切换主机或重连时禁止接纳旧会话迟到的数据。
- 协议消息在接入边界解析与校验，再映射为核心输入。控件和业务算法不得直接读取网络字节、JSON 字段或协议命令字符串。
- 实现通信时定义消息大小限制、格式校验、版本兼容行为和错误反馈。局域网来源的数据仍需校验；坏包不能破坏最后一组有效状态或导致接收循环悄悄退出。
- 断线或数据超时必须可被业务协调层和展示层识别，不得持续把旧数据表现为实时数据。超时数值、断线后的模拟行为及重连策略依据协议和用户规则确定。
- 网络 I/O 异步、可取消；不得阻塞 UI 线程，不得使用无上限重试或无上限积压队列。重连、窗口关闭和应用退出时正确取消并释放资源。
- 如果后续增加发往游戏的操作，区分请求、发送状态、确认结果和游戏实际状态；重试与重复执行的处理必须依据协议。当前 Debug 滑块不属于游戏控制接口。
- 在用户明确协议与实现任务前，不自行确定 UDP／TCP／WebSocket、包结构、确认机制或新增通信依赖。

## 7. 本地 Debug 模拟

- 手柄、假数据、菜单文字和调度位于独立 `RailDisplay.Debug` 项目；主程序只保留原生菜单宿主、加载和启动接线。检测到 `Debug.dll` 时加载程序集读取菜单文字，首次打开 Debug 才创建插件并初始化样例数据。关闭调试窗口即停止模拟并保留最后状态和手柄，再次打开继续；不得补算暂停时间或重复初始化样例。关闭主窗口释放插件、音频并退出整个程序。
- Debug 滑块为离散档位：`P1—P7` 和 `B1—B7＋EB`，仅在 RailDisplay 内驱动本地模拟及显示。
- Debug 菜单使用英文根项 `Debug`，其下为 `Simulation`、`Debug Setting`，不提供强制重绘菜单项。Windows 右键菜单始终将灰显的 `Always on Top`、`Hide Title Bar` 作为顶层项显示，暂不接入逻辑；存在 `Debug.dll` 时额外显示 `Debug` 根项。macOS 仅在插件存在时显示 Debug 菜单，不显示这两个 Windows 专属菜单项。设置窗口编辑目标速度、允许速度（km/h）、目标距离（m），经 Core 校验后从最新快照统一提交；SBI、EBI、TSM 保留现值，自动计算规则待用户提供。手柄窗口下方 19 个实体键已通过插件窄回调接入同一个 `KeyHandle`，命令可用性来自 `CanHandle`；交互状态更新后同步刷新，释放插件时解除宿主订阅。窗口内 F1—F8、主键盘及小键盘数字映射实体键，手柄原快捷键保留，不虚构警惕快捷键。
- Core 的 `CtcsButton` 枚举定义 19 个实体按键，注释记录用户提供的键面名称，枚举数值不是通信协议编号。`CtcsController.KeyHandle` 委托共用 `KeyHandler<TKey,TContext>` 分发；初始化时复制并校验绑定表，拒绝非法键、重复绑定及空处理器。`CanHandle` 是纯查询，与执行使用同一个可用条件，读取最新模式和步骤，不通过试执行判断启用状态；禁用键不触发处理器。处理器异常向调用方传播，业务处理须先校验后提交。
- 已确认接入既有功能菜单导航：`FunctionKeyMenu` 的 `TargetStep` 和显式 `IsBack` 生成只读 `CtcsNavigationBinding` 配置，App 交给 Core 的 `CtcsNavigationKeys.Create` 创建处理器并注入控制器；Core 不读取桌面菜单或标签。“返回”和“取消”均显式配置为 `Back()`，返回上一层，沿用默认不保留输入，不提交业务数据；不通过显示文字推断动作。确定、删除、数字输入、编组选择、音量/亮度调整、警惕及实际模式、启动、缓解等提交规则继续待确认，相应动作禁用。各模式暂共用同一菜单，模式准入条件仍待用户提供；无参控制器仍为空表。
- DMI 的 `FunctionKeys` 为 F1—F8 的只读显示快照，按当前步骤映射文字，并从 Core 查询可用性；缺项文字为空，未定义动作在 Debug 禁用。DMI 八个功能格保留原设计坐标；仅无内容的格子通过 `IsEmpty` 使用现有深灰色 `DisabledButtonBrush`，有内容的格子保持原背景，不按动作可用性改变颜色。DMI 整屏只显示，不接受鼠标、触摸或其他点击输入，文字滚动箭头也只显示；按键由 Debug 调用 ViewModel 的统一入口，处理成功后刷新 DMI。
- 当前交互和历史由设备逻辑中的共用 `InteractionNavigator<TState>` 单独持有，设备状态须为不可变快照。仅保留 `Next(target)` 和 `Back(count: 1, keepInput: false)`：前进保存当前步骤与输入；返回默认清空目标步骤未确定的输入，显式保留时恢复目标原输入。用户确认次数 ≤0 按一次处理，超过深度返回最早状态，空栈返回未处理；不提供设根、回根及单独清历史接口。`History` 从最早到最近排列，末项为上一步且不含当前状态，每次导航发布新只读快照。
- `CtcsInteractionState.Step == null` 表示当前模式默认内容，输入默认空；启动模式为 `Standby`，显示待机，不另设 `Idle`。`CurrentInteractionRoot` 从最新 `ControlMode` 派生，各模式引用同一份 22 步骤的只读功能树；关机仅保留根结构，电源转换规则尚未接入。外部数据更新保留步骤、输入和历史，返回不会恢复旧模式。`CtcsViewModel.KeyHandle` 处理成功后刷新交互、模式根和历史，DMI 在展示层映射内容；后续输入页面仅替换 D 区，其他监控区继续显示，当前尚未改布局或实现数字/字母输入及确认规则。
- 调试输入经过同样的输入校验与核心业务计算，不绕过规则直接给控件写显示结果；假数据生成与业务算法分开。
- 本地调试与游戏数据源明确切换、展示当前来源，不能同时竞争写入同一业务会话。调试路径不得调用向游戏发送操作的接口。
- 滑块值不等于 `CtcsBrakeLevel` 显示状态，禁止直接强转或按枚举顺序映射；`NoBrake = -1` 表示无制动并显示为空白，`ReleaseAllowed = 0` 表示允许缓解，其余 1～4 保持原值。牵引／制动输入如何产生速度、监督和制动结果由用户规则决定。
- 已确认手柄输入规则：功率 0／P1—P7，制动 0／B1—B7／EB，默认双零位、方向向前；功率非零时显示红框并禁止换向，制动非零时有效功率为 0，保留功率手柄位置。A／D 增减功率，W／S 选择向前／向后，分号／单引号减增制动，点击箭头换向。本地速度模拟采用已确认的目标加减速度及分段系数（详见 README 的本地 Debug 表），最高 400 km/h，双零位保持速度，制动最低到 0，EB 退档立即解除本地紧急制动，方向不改变显示速度的正负。速度计算位于 Core，显式传入时间，跨段积分；不将手柄档位映射为 CTCS 五档制动状态。监督算法及真实系统 EB 解除规则仍待确认。
- 调试数据与热重载逻辑不得在实时连接中偷偷重置业务状态，或者切换主机、覆盖当前会话。

## 8. 界面、线程与资源

- View 与自绘控件负责布局、绘制、命中测试及纯视觉交互；允许这些职责的后台代码，禁止在其中计算监督曲线、决定制动规则、收发网络或读写业务文件。
- 指针事件转换为明确的操作交给 ViewModel／业务入口。未来鼠标与触摸应能调用同一操作；当前不提前实现未要求的触摸功能。
- 延续当前屏幕设计坐标与统一缩放方式；窗口缩放不得改变业务数值、命中区域含义或显示比例。区分设计坐标、输出分辨率和系统 DPI，不将当前 `640×480` 强加给未来所有屏型。
- 共用颜色和字体资源集中管理，屏型专属尺寸留在屏型内。复用已有 `PixelDisplay`、`SpeedScale`、`BrakeIndicator` 等组件前确认职责适合，不复制一套相同渲染逻辑。
- 界面状态在 UI 线程批量提交；核心与通信不直接操作 Dispatcher、窗口或 ObservableObject。跨线程交付放在桌面适配位置，保持一次展示使用同一批数据。
- 可以合并高频显示刷新，但不能因此丢弃业务计算所需的输入或离散事件。先区分可替换的最新显示快照与必须处理的业务事件。
- 计时器、事件订阅、后台任务和位图有明确所有者，在对应生命周期结束时停止、取消、解除订阅或释放。切换页面不得重复订阅；反复缩放不得持续泄漏位图。
- 桌面实现优先满足 Windows 的产品和交互需求；跨平台能力适合时继续复用，Windows 专有实现集中隔离。macOS 适配作为附带兼容层维护，不为追求平台对称而改变 Windows 行为或架构。
- 原生菜单使用真正的平台菜单：macOS 使用 NativeMenu，在顶部系统菜单栏显示可用的 Debug 菜单；Windows 使用 Win32 HMENU，在窗口非标题栏区域右键时弹出，始终包含灰显的 `Always on Top`、`Hide Title Bar` 顶层项，并在扫描到 `Debug.dll` 时增加 Debug 根项。Windows 标题栏保持系统原样，不以 Avalonia 自绘 Menu 替代。通用 HMENU 实现位于 `RailDisplay/Platform/Windows/Win32Menu.cs`，跨平台应用菜单组装位于 `RailDisplay/Platform/NativeApplicationMenu.cs`；关闭窗口时释放菜单与订阅。
- macOS 原生根菜单挂载后保持同一实例，释放 Debug 时只移除自身菜单项，避免 Avalonia 原生导出器的菜单实例不匹配异常。
- XAML 热重载由 Debug 专用 HotAvalonia 与同版本 Avalonia.Markup.Xaml.Loader 负责；MetadataUpdateHandler 和 `ForceRedraw` 只重新布局、绘制和生成像素帧，不重置业务数据或重建声音、模拟实例。

## 9. 验证要求

沿用现有控制台自检，不为规范本身切换测试框架。新业务逻辑、解析、状态转移及缺陷修复必须有小而有效的检查；纯文本和无逻辑变化不新增形式化测试。

从项目根目录执行：

```sh
dotnet build RailDisplay.sln
dotnet run --project checks/CtcsChecks.csproj
```

仅检查业务核心、无需桌面或图形依赖时：

```sh
dotnet run --project checks/Core/CoreChecks.csproj
```

像素渲染、制动图标及相关缩放行为变化时，另外运行：

```sh
dotnet run --project checks/CtcsChecks.csproj -- --pixels
```

- 两个自检项目均已列入解决方案；构建会编译它们，但不会执行检查。`dotnet test` 不替代上述控制台命令；`--pixels` 单独运行渲染检查，不同时运行普通检查。
- 业务检查至少覆盖实际相关的正常情况、临界值、非法输入和状态往返；非法输入应验证未留下半更新状态。修复缺陷时补能复现问题的检查。
- `checks/Core` 只引用 Core，复用 `checks/CTCS/CtcsCoreChecks.cs`；`checks/CTCS` 的显示及能力检查、`checks/Rendering` 的渲染检查由桌面自检入口执行。保持业务检查不依赖 Avalonia 或真实游戏。
- 通信实现时检查实际协议的坏包、断线、重连及会话切换，使用可控输入，不让日常自检依赖真实游戏或局域网设备。
- 像素检查需要可用的 Avalonia 图形运行环境。布局和交互变化还需实际显示检查，包括本次相关的窗口大小、缩放与点击位置；自动像素检查不替代所有人工验证。
- Windows 是主要验收平台，交付说明实际执行的检查、运行平台、结果和未验证部分。macOS 作为附带平台单独记录兼容性验证结果；当前平台通过不代表其他平台也已验证。
- 原生菜单、设置窗口及热重载重绘可单独执行 `dotnet run --project checks/CtcsChecks.csproj -- --debug-ui`，也由 Debug 构建的 `--pixels` 入口执行。需要图形环境；XAML 保存后更新、实际菜单点击和键盘导航仍需运行应用验证。
- `--debug-ui` 同时检查插件关闭后停止更新、重开保留状态、单窗口与重复释放。`dotnet run --project checks/CtcsChecks.csproj -- --shutdown` 单独以真实桌面生命周期检查主窗口关闭后释放插件并退出整个程序，需要 Debug 构建和图形环境。
- 音频核心检查位于 `checks/Audio/AudioCoreChecks.cs`，由 Core 与普通检查入口复用；播放适配检查使用可控替身，不依赖声卡。实际音频另执行 `dotnet run --project checks/CtcsChecks.csproj -- --audio`，仅 Windows / 可用 ALSA 的 Linux 支持，macOS 报告未执行。该参数单独运行真实播放检查，不代替普通自检或听感验证。

## 10. 改动范围与交付

- 修改前搜索所有相关调用、绑定、测试、配置和资源，明确本次变动到达哪些位置；修根因，不在每个调用处复制补丁。
- 完成本次涉及职责的整理，无关问题记录后另做。不借小功能或规范文档任务批量重构、改名、升级依赖或调整界面风格。
- 优先复用现有实现，其次标准库和已安装依赖。新增依赖必须说明实际需求及现有能力不足之处，不为几行代码引入框架。
- CI 自动触发时 Windows 使用 Release，手动触发时 Windows 使用 Debug；macOS 始终使用 Debug。发布为依赖已安装 .NET 运行时的单文件应用包，文件名为 `RailDisplay-{InformationalVersion}-{RID}.zip`，不捆绑 .NET 运行时。
- 保持项目现有 C# 命名、可空性和格式约定；命名表达领域含义，注释说明代码本身无法表达的原因、规则依据或已知限制。
- 不吞异常、不默默回退到看似有效的业务值。错误在能决定处理方式的位置处理，日志提供定位所需上下文，避免逐帧或逐包无节制输出。
- 完成时简要说明改了什么、为什么、如何验证、剩余限制。更新架构或协议时同步更新本文件中受影响的约束与命令。
- 每次修改后，必须向用户逐项说明本次新增或修改的各个部分：该部分负责什么、位于哪些文件及关键入口、后续调整其行为应修改哪里和如何修改，并说明需要同步更新的关联部分及验证方式。说明按功能或模块组织，使用用户能理解的语言，并提供可定位的文件链接。

## 当前结构索引

- `RailDisplay.Core/Models/CTCS/`：CTCS 业务数据、等级、模式、消息、实体按键枚举 `CtcsButton`、交互状态 `CtcsInteractionState` 及共用模式功能树 `CtcsInteractionTree`。
- `RailDisplay.Core/Controllers/Interaction/`：共用 `KeyHandler` 按键分发和 `InteractionNavigator` 前进、返回；不包含设备业务规则或界面布局。
- `RailDisplay.Core/Controllers/CTCS/`：`CtcsController` 和 `ICtcsController` 为统一调度与对外契约；`CtcsDevice` 负责按键、步骤和导航；`CtcsCore` 负责校验、数据快照、区段、告警、制动、预警及运行消息状态，预警计算复用 `CtcsBrakeWarningPredictor`。
- `RailDisplay.Core/Controllers/Audio/`、`Models/Audio/`：声音契约、配置及纯调度。
- `RailDisplay.Infrastructure/Controllers/Audio/`：共用设备音频控制器与 CTCS2-200H 配置；`Audio/` 为实际播放适配，尚无 DLL 通信实现。
- `RailDisplay/Models/CTCS/`：屏型能力、菜单、显示状态和消息行模型。
- `RailDisplay/ViewModels/CTCS/`：CTCS 显示状态、绑定通知、滚动、闪烁和本地等级切换；不保存第二份可独立修改的业务状态。
- `RailDisplay/Views/`：窗口及平铺的屏型页面；`Controls/CTCS/` 为 CTCS 绘制控件，`Controls/PixelDisplay.cs` 为共享渲染组件。
- `RailDisplay/Assets/Themes/CTCS/`：CTCS 视觉资源；`RailDisplay.Debug/CTCS/`：本地模拟样例和推进调度。
- `RailDisplay/App.axaml.cs`：实例组装、初始数据、原生 Debug 菜单动作与退出释放入口；`MainViewModel` 接收 CTCS ViewModel 和设备声音 Core 接口；`HotReloadHandler` 只重绘界面，保留业务数据和声音实例。
- `RailDisplay/Platform/NativeApplicationMenu.cs`、`RailDisplay/Platform/Windows/Win32Menu.cs`：macOS Debug 系统菜单、Windows HMENU 及生命周期；`RailDisplay/Debug/DebugPluginLoader.cs` 按需加载可选 DLL；`checks/Rendering/DebugUiChecks.cs` 检查菜单动作、重绘、状态保留与插件启停，`ApplicationExitChecks.cs` 检查整程序退出。
- `RailDisplay.Debug/DebugPlugin.cs`：单窗口、模拟启停与异步释放；`ViewModels/DebugViewModel.cs`、`Views/DebugWindow.axaml`：手柄输入。应用目录存在 `Debug.dll` 时可通过原生菜单或 F12 首次加载；关闭插件窗口暂停，重开继续；`Core/Controllers/Simulation/TrainMotionSimulation.cs` 计算速度，`RailDisplay.Debug/CTCS/CtcsDebugSimulation.cs` 通过主程序传入的回调提交核心数据。
- `checks/`：普通检查与渲染检查入口、`DebugChecks.cs` 手柄及接入检查、`TrainMotionChecks.cs` 速度模拟检查；`checks/Interaction/InteractionChecks.cs` 检查导航、分发、输入保留与模式更新，和速度检查一样由 Core 入口复用；`checks/Core/`：独立核心检查入口。
- `RailDisplay.Infrastructure/Logging/ApplicationLogging.cs`：本地日志目录、级别、标准输出模板及轮转；`RailDisplay/Logging/AvaloniaLogSink.cs`：框架日志桥接；`Program.Main` 初始化、异常与退出刷新，`App` 注入外层模块；`checks/Logging/LoggingChecks.cs` 由普通入口验证落盘、过滤、上下文、异常及释放。
- `README.md`：各部分用途、后续修改位置、扩展方法及验证命令。

上述索引用于定位，不是授权额外重构的任务清单。
