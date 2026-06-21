# P12Bridge Desktop

[English](README.md)

P12Bridge 是一款面向 Windows 的 iOS 证书、描述文件和 IPA 上传工作台。

MVP 采用本地优先方案：帮助 Windows 用户在本机生成私钥与 CSR，导入 Apple 签发的证书，导出 P12 文件，校验描述文件和已签名 IPA，并上传 IPA 构建。应用不收集 Apple ID 主密码，也不把签名资产托管到云端。

## 产品范围

- Windows 桌面应用。
- 推荐技术栈：C# / .NET 8，WPF 或 WinUI 3。
- 本地证书和签名资产管理。
- 半自动 P12 生成：
  - 本机生成私钥和 CSR；
  - 用户在 Apple Developer 上传 CSR；
  - 用户导入下载的 `.cer`；
  - 应用在本机导出 `.p12`。
- 描述文件导入与校验。
- 已签名 IPA 检查与上传。
- MVP 不包含本地 IPA 签名或重签名。
- 不收集 Apple ID 主密码。
- 不提供云端证书托管或云端上传中转。

## 文档

- [产品需求](docs/prd.md)
- [技术设计](docs/design.md)
- [实施计划](docs/implement.md)
- [手动验证清单](docs/manual-verification.md)

## 仓库状态

桌面端 MVP 已实现本地证书流程、描述文件导入、IPA 检查、上传前检查、设置、资产管理和操作历史。

自动化本地验证已覆盖主要的非 Apple 服务链路。剩余验证工作：

1. 使用真实 Apple 签发的证书和描述文件完成手动端到端流程。
2. 使用有效凭据验证真实已签名 IPA 上传。
3. 仅按手动验证清单保留脱敏证据。

## 安全原则

- 私钥保留在本机。
- 不保存 Apple ID 主密码。
- 仅在用户明确同意时保存可选凭据。
- 日志中脱敏密钥和密码。
- 优先使用 Apple 官方 API 和文档化上传机制。
