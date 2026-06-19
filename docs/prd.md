# Windows 苹果证书与 IPA 上传桌面工具需求文档

## 1. 文档信息

- 文档类型：产品需求文档 PRD
- 交付格式：Markdown
- 目标平台：Windows 桌面端
- 推荐技术栈：C# / .NET 8 + WPF 或 WinUI 3
- MVP 定位：本地优先的自用 / 内部工具
- 主要用户：非技术或半技术的个人开发者、小团队运营人员、内部应用上架人员

## 2. 背景与问题

当前用户在 Windows 上完成 iOS 证书与 IPA 上传，通常需要手工执行 OpenSSL 命令、登录 Apple Developer 后台创建证书、下载 `.cer`、合成 `.p12`、下载 `.mobileprovision`，再用第三方工具上传 IPA。

现有流程的主要痛点：

- 命令行步骤多，私钥、CSR、CER、PEM、P12 概念容易混淆。
- `ios.key` 一旦丢失，无法重新生成同一套 P12，风险高。
- P12、描述文件、Bundle ID、证书类型不匹配时，失败原因不直观。
- AppUploader、XUploader 等第三方工具可以解决部分问题，但用户需要把账号、应用专用密码、IPA 等敏感信息交给不透明工具。
- Windows 用户没有 Mac/Xcode 环境，上传和排错路径更分散。

本产品目标是把原手工教程产品化，做成一个 Windows 桌面工具，用引导式流程降低制证和上传门槛，同时把私钥、证书和凭据尽量留在本地。

## 3. 产品目标

MVP 目标：

- 在 Windows 上完成私钥、CSR、P12 的半自动制证闭环。
- 管理本地证书资产和描述文件，减少误删、混用和遗失。
- 校验已签名 IPA 的关键上架条件，并上传到 App Store Connect / TestFlight。
- 不收集 Apple ID 主密码，不做云端托管，不做云端代传。
- 提供比手工教程和第三方上传工具更清晰的错误提示、安全边界和操作记录。

非目标：

- MVP 不做 Windows 本地签名或 IPA 重签名。
- MVP 不自动创建 Apple 证书、Bundle ID、设备、描述文件。
- MVP 不做 SaaS 用户系统、收费系统、云端账号库、云端证书托管。
- MVP 不模拟 Mac、不伪装设备、不逆向苹果私有协议。

## 4. 目标用户

### 4.1 主要用户

非技术或半技术的个人 / 小团队 Windows 用户：

- 已拥有付费 Apple Developer 账号。
- 能按照指引进入 Apple Developer / App Store Connect 后台操作。
- 不想手写 OpenSSL 命令。
- 不想深入理解证书链、描述文件和 IPA 上传失败原因。
- 需要一个本地工具把流程串起来。

### 4.2 暂不优先的用户

- 高频多账号发行团队。
- 大规模批量上传团队。
- 需要云端统一管理证书、账号、权限和审计的组织。
- 需要自动重签、批量改包、自动化打包流水线的团队。

## 5. 竞品与市场分析

| 工具 / 方案 | 主要能力 | 优势 | 不足 | 本产品差异化 |
| --- | --- | --- | --- | --- |
| AppUploader | Windows/macOS/Linux 下的 IPA 上传、证书、描述文件、设备管理等 | 功能覆盖广，面向无 Mac 用户，流程成熟 | 工具较重，账号与凭据处理不够透明，用户对安全边界难判断 | 聚焦本地安全、引导式制证、上传前诊断、透明日志 |
| XUploader | Windows IPA 上传工具，常作为 AppUploader 替代选择 | 上手简单，面向上传场景 | 公开资料有限，功能边界和安全策略不清晰 | 明确只用官方/低风险机制，减少黑盒上传 |
| Apple Transporter / App Store Connect API | Apple 官方上传与管理能力 | 官方、合规、长期稳定性更好 | Windows/Linux 上传 App 可能需要额外元数据；API Key 配置门槛较高 | 用桌面 UI 包装官方能力，降低配置和排错成本 |
| ios-uploader 等开源 CLI | 跨平台命令行 IPA 上传 | 可自动化、开源、适合技术用户 | 非官方背书，苹果接口变化时可能失效，非技术用户门槛高 | 提供图形化流程、资产校验、错误解释和安全提示 |

结论：

市场已有“能上传”的工具，但缺少一个面向 Windows 普通用户、强调本地私钥安全、制证流程可解释、上传前能诊断问题的轻量桌面工具。MVP 应先解决“少出错、能看懂、凭据不乱交”的问题，而不是追求大而全。

## 6. MVP 范围

### 6.1 账号连接

需求：

- 不要求、不保存、不传输 Apple ID 主密码。
- 支持配置 App Store Connect API Key，用于后续 Apple 官方 API 能力扩展。
- IPA 上传场景允许使用 Apple ID + 应用专用密码。
- UI 必须明确说明“应用专用密码不是 Apple ID 登录密码”。
- 凭据默认仅本地保存，优先使用 Windows Credential Manager 或等价安全存储。

验收标准：

- 用户无法在软件中输入 Apple ID 主密码作为登录凭据。
- 应用专用密码输入页有清晰说明和生成入口指引。
- 本地保存凭据前必须有明确用户授权。

### 6.2 证书 / P12 半自动生成

MVP 采用半自动制证闭环：

1. 用户选择证书用途：开发调试或发布上传。
2. 软件本地生成私钥和 CSR。
3. 软件保存并提示备份私钥。
4. 用户按软件指引进入 Apple Developer 后台上传 CSR。
5. 用户下载 `.cer` 并导入软件。
6. 软件本地转换证书并导出 `.p12`。
7. 用户设置 P12 密码，软件提示妥善保存。

需求：

- 支持生成 RSA 私钥与 CSR。
- 支持导入 Apple 下载的 `.cer`。
- 支持导出 `.p12`。
- 支持显示证书类型、创建时间、过期时间、关联私钥状态。
- 对私钥缺失、证书格式错误、P12 密码为空等情况给出明确提示。
- 每套证书资产应以项目目录形式管理，避免文件散落。

验收标准：

- 用户无需手写 OpenSSL 命令即可生成 CSR 和 P12。
- 删除或移动私钥后，软件能提示该证书资产不可继续导出同一套 P12。
- `.p12` 导出失败时，错误信息能指向具体原因。

### 6.3 描述文件管理

MVP 不自动创建 `.mobileprovision`，只做导入和校验。

需求：

- 支持导入 `.mobileprovision`。
- 解析并展示 Bundle ID、Team ID、Profile 类型、过期时间、设备数量、证书摘要。
- 校验描述文件与本地证书资产是否可能匹配。
- 区分 Development、Ad Hoc、App Store 等类型。
- 对过期、类型不匹配、Bundle ID 不一致等问题给出阻断或警告。

验收标准：

- 用户导入描述文件后能看到关键字段。
- 当描述文件类型与上传场景不匹配时，软件明确提示。
- 当描述文件已过期时，软件阻止继续作为有效资产使用。

### 6.4 IPA 校验

MVP 只处理已签名 IPA，不做签名或重签名。

需求：

- 支持选择 `.ipa` 文件。
- 展示 IPA 包名、版本号、Build 号、Bundle ID、文件大小。
- 检查是否包含基础签名与描述文件信息。
- 尝试校验 IPA 内嵌描述文件与用户导入资产的 Bundle ID / Team ID / 类型是否一致。
- 上传前给出检查清单：Distribution 证书、App Store 类型 Profile、Bundle ID 匹配、版本信息可读、文件存在且可访问。

验收标准：

- 用户上传前能看到 IPA 的核心信息。
- 常见问题能提前暴露，而不是等上传失败后才发现。
- 软件明确说明“不支持本地重签名，需使用外部打包工具生成已签名 IPA”。

### 6.5 IPA 上传

需求：

- 支持上传已签名 IPA 到 App Store Connect / TestFlight。
- 支持 Apple ID + 应用专用密码上传路径。
- 预留 App Store Connect API Key / JWT 上传或查询能力。
- 上传过程展示进度、速度、阶段状态和日志。
- 上传完成后提示用户到 App Store Connect 构建版本页面核验。
- 上传失败时提取关键错误并给出可执行建议。

注意：

- Apple 官方 Transporter 在 Windows/Linux 上传 App 可能依赖额外元数据，例如 `AppStoreInfo.plist`。实现阶段需要验证具体上传链路，优先采用官方支持路径。
- 如果使用非官方开源上传组件，必须在产品中明确稳定性风险，并保留替换方案。

验收标准：

- 用户能选择 IPA 并完成一次上传尝试。
- 上传失败日志可复制。
- 账号凭据不会上传到自有云端服务器。

### 6.6 本地资产与历史记录

需求：

- 本地保存证书项目列表。
- 每个项目包含：私钥、CSR、CER、P12、描述文件、备注、创建时间、过期时间。
- 支持打开项目所在文件夹。
- 支持导出备份包。
- 支持操作历史：生成 CSR、导入 CER、导出 P12、导入 Profile、校验 IPA、上传 IPA。
- 日志默认脱敏，不显示完整密码、完整私钥内容。

验收标准：

- 用户能找回历史生成的资产位置。
- 软件能提示即将过期的证书或描述文件。
- 敏感字段在界面和日志中默认脱敏。

## 7. 安全与合规要求

- 私钥只在本地生成和保存。
- 不把私钥、P12 密码、应用专用密码上传到自有服务器。
- 不保存 Apple ID 主密码。
- 本地凭据保存必须可关闭。
- 日志导出前必须脱敏。
- 软件不得伪装 Mac 设备或绕过 Apple 官方限制。
- 所有涉及 Apple 的自动化能力，优先采用 App Store Connect API、官方 Transporter 或公开可解释机制。

## 8. 产品信息架构

建议主导航：

- 首页 / 工作台：展示最近项目、待办提醒、证书过期提醒。
- 制作证书：生成私钥/CSR、导入 CER、导出 P12。
- 描述文件：导入 Profile、查看和校验。
- IPA 上传：选择 IPA、上传前检查、上传进度、上传结果。
- 资产库：证书项目、文件位置、备份、历史记录。
- 设置：API Key、应用专用密码、OpenSSL/Transporter 路径、日志、隐私。

## 9. MVP 成功指标

- 用户无需命令行即可完成 CSR 生成和 P12 导出。
- 用户能在上传前发现至少 5 类常见错误。
- 用户能理解应用专用密码、Apple ID 主密码、P12 密码的区别。
- 用户能完成一次已签名 IPA 的上传尝试。
- 私钥、P12、密码等敏感数据默认不离开本机。

## 10. 后续版本路线

V1.1：

- 通过 App Store Connect API 自动读取 Bundle ID、证书、设备、Profiles。
- 自动检查 App Store Connect 中是否存在对应 App。
- 构建版本上传后自动查询处理状态。

V1.2：

- 支持自动创建 Bundle ID、证书、描述文件。
- 支持设备 UDID 管理。
- 支持证书过期提醒和批量备份。

V2.0：

- 评估本地签名 / 重签名能力。
- 评估多账号和批量上传。
- 评估商业化授权、自动更新、客服诊断包。

## 11. 风险与待验证项

- Windows/Linux 下使用官方 Transporter 上传 IPA 的具体输入要求需要在实现阶段验证。
- Apple 上传协议和认证方式可能调整，需要保留可替换上传适配层。
- App Store Connect API Key 的配置门槛对非技术用户仍偏高，需要用 UI 引导降低难度。
- `.mobileprovision`、IPA 签名信息的解析需要覆盖真实样本，否则校验结果可能不完整。
- 过度承诺“自动上传成功”会带来支持压力，产品文案应强调“上传前检查和官方链路上传”。

## 12. 资料来源

- AppUploader 官网：https://www.appuploader.net/en/
- AppUploader Windows IPA 上传教程：https://www.appuploader.net/en/blog/239
- AppUploader iOS 证书管理教程：https://www.appuploader.net/tutorial/en/45/45.html
- Apple App Store Connect API：https://developer.apple.com/app-store-connect/api/
- Apple App Store Connect API 文档：https://developer.apple.com/documentation/appstoreconnectapi
- Apple 上传构建帮助：https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds/
- Apple Transporter 用户指南：https://help.apple.com/itc/transporteruserguide/en.lproj/static.html
- ios-uploader 开源项目：https://github.com/simonnilsson/ios-uploader
- XUploader 社区参考：https://zhuanlan.zhihu.com/p/668599193
