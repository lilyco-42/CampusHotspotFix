# CampusHotspotFix — 开发进度说明

## 当前完成的部分(可以立即在VS里打开验证)

- 项目骨架(csproj、app.manifest管理员权限声明)
- `Utils/CommandRunner.cs` —— 命令行调用封装,已处理中文Windows的GBK编码问题
- `Utils/AdminChecker.cs` —— 管理员权限检查与提权
- `Models/` —— 诊断结果、修复结果、网卡信息的数据结构,编号严格对应PRD的P1-P10
- `Services/NetworkAdapterService.cs` —— 网卡查询 + 承载网络驱动支持检测(P1前置判断)
- `Services/HostedNetworkService.cs` —— 虚拟热点创建/停止/状态查询(P1修复核心逻辑)
- `Forms/MainForm.cs` —— **最小可编译骨架**,只接了上面两个Service的两个查询按钮,用来现在就能跑起来验证,不是最终UI
- `Resources/faq.json` —— P9拨号报错代码对照表(占位数据,建议用你评论区实际收集到的错误码替换/补充)

## 如何验证(在你自己的Windows机器上)

1. 用VS 2022打开 `CampusHotspotFix.csproj`
2. 还原NuGet包(VS一般会自动做,或手动"还原NuGet包")
3. F5运行——会弹UAC提权窗口,点"是"
4. 点"检测网卡是否支持承载网络"按钮,看输出结果是否符合你机器的实际情况(和手动跑`netsh wlan show drivers`命令对照一下)
5. 点"列出所有网络接口"按钮,确认能不能在列表里看到你的PPPoE拨号连接(类型应该显示"Ppp")

**如果第4步的中文关键字匹配不上:** 大概率是你的Windows系统语言不是简体中文,或者是某个Windows版本的netsh输出格式有变化,把`_outputBox`里显示的原始输出发给我,我再调整`NetworkAdapterService.IsHostedNetworkSupported()`里的正则匹配规则。

## 下一步开发顺序(严格按《技术实现工作流程.md》第4节)

1. **IcsShareService(COM互操作)** —— 建议先单独写一个Console测试项目验证`INetSharingManager`调用通不通,确认没问题再合并进主项目,不要直接在WinForms里调试COM问题
2. **PowerManagementService** —— 相对独立,可以和上面并行做
3. **TaskSchedulerService** —— 依赖前两个都跑通后才有意义测试"重启后是否自动生效"
4. **DisconnectMonitorService** —— 需要长时间挂机跑才能验证判断逻辑,建议放最后,而且要留出几小时真实运行时间
5. **DiagnosticService + ReportService** —— 汇总层,等所有Service返回结构确定后再写
6. **完整MainForm三步向导UI** —— 把现在的骨架换成"诊断→修复→报告"的完整流程

## 已知限制/待确认事项

- `CommandRunner`里的GBK编码假设:如果目标用户机器不是简体中文系统,这个假设会失效,需要做系统语言检测并切换编码
- `NetworkAdapterService.IsHostedNetworkAdapterName()`的关键字匹配没有在真机上验证过,不同Windows版本的虚拟适配器描述文本可能有出入,请在测试阶段重点关注这个方法是否命中
- 静默修复模式(`Program.cs`里的`RunSilentFix()`)目前只是空的TODO骨架,等其他Service完成后再补
