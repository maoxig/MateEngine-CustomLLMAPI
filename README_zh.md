# MateEngine Custom LLM API Mod

## 简介

本 Mod 允许你将 MateEngine 的本地 LLM 替换为远程 LLM 提供商（如 ChatGPT、DeepSeek、Claude 等）的接口。启用后，Mod 会在本地启动一个代理（默认端口为 13333，可自定义），将来自 Character 的请求转发至远程 LLM API。

![alt text](resources/intro.png)

开发这个mod的原因是，运行一个本地大模型（即使经过量化）对大多数用户来说是一个挑战，作为一个桌宠游戏，如果需要时刻负载一个本地大模型未免有些过于奢侈（而且大多数时候并不需要）。相比之下远程 LLM API提供商通常能提供更好的性能和更高的可用性，并且也不会占用内存、显存。而且现如今很多API的价格已经非常低廉，甚至有些是免费的。因此本 Mod 旨在为 MateEngine 用户提供一个便捷的方式来使用远程 LLM API。

## 功能特性

- 支持在本地和远程 LLM 之间切换（由于LLMUnity的限制，目前如果已经连接成功（即输入框变为 talk to me），需要重启游戏才能切换）
- 支持任意符合 OpenAI Chat Completions API 规范的 LLM 服务，也能支持一些其他的符合规范的 LLM 服务
- 完全兼容 MateEngine 原有机制（角色记忆、Prompt、聊天记录等），因为它仅仅是转发请求到远程LLM接口
- 可保存多套 LLM API 配置，支持一键切换及故障自动回退
- UI 上实时显示错误提示，便于排查问题
- 设置选项保存在 `C:\Users\{你的用户名}\AppData\LocalLow\Shinymoon\MateEngineX\LLMProxySettings.json`，可手动编辑

## 安装方法

类似 CustomDancePlayer的安装方式，见[https://github.com/maoxig/MateEngine-CustomDancePlayer?tab=readme-ov-file#installation-steps](https://github.com/maoxig/MateEngine-CustomDancePlayer?tab=readme-ov-file#installation-steps). 唯一的不同是dll的名字有所不同。


## 使用指南

1. 按 `J` 键打开/关闭配置面板，配置完成后点击保存以启用。
2. 填写 API Endpoint（如 `https://api.openai.com/v1/chat/completions` 或 `https://api.deepseek.com/v1/chat/completions`），请参考对应相关的 API 文档，确保路径正确
3. 输入 API 密钥，并配置其他参数（如模型名称等）。由于目前还未充分测试，因此不一定能够适配所有 LLM 服务商，将在之后的更新中逐步支持。
4. 启动 MateEngine 聊天功能，若代理服务器正常启动，输入框会从 loading 状态变为 Talk to me。注意，这并不意味着一定能请求成功，只有代理服务器能成功请求远程 LLM API 时，才能收到回复

## 未来计划

- 支持流式响应（目前暂未启用，待修复相关 bug）
- 更便捷的角色 Prompt 配置
- 更多自定义参数（LLM 调用相关）
- 更灵活的 API 配置方式，适配更多 LLM 服务商
- 欢迎提出建议和PR

## 安全与责任声明

由于涉及端口监听和 API 密钥，请确保你信任本 Mod。任何因使用本 Mod 导致的后果由用户自行承担，作者仅负责 Mod 本身维护，不对 LLM API 相关问题负责。


## 贡献说明

由于作者仅在 DeepSeek 上测试过，欢迎熟悉各类 LLM API 的开发者参与维护。 具体的API适配代码位于 `LLMAPIProxy.cs`。如果要测试：
- 克隆本仓库到Visual Studio
- 修改项目的引用，改为你本地的 MateEngine 安装路径下的 `MateEngineX_Data/Managed` 目录里的DLL
- 修改代码，编译生成 DLL，放入 MateEngine 的 `MateEngineX_Data/Managed` 目录里
- 启动 MateEngine 测试
- 如果你需要修改 UI 相关代码，你需要使用 MateEngine 的Unity工程项目，然后拖拽提供的DLL和prefab到项目里编辑，并且导出prefab为.me



## 其他说明

本 Mod 实现思路简单，但因 MateEngine 基于 Mono 且裁剪了大量 API，部分功能需手动实现底层方法。如果你有更好的实现建议，欢迎提出

## 许可协议

本 Mod 采用 MIT 许可协议，并遵循 Mate Engine 官方许可协议。允许个人非商业用途、修改和分发，但禁止用于商业盈利。使用本 Mod 即表示您同意遵守 Mate Engine 官方条款。开发者不对因本 Mod 导致的游戏错误或问题承担责任。

许可详情：MIT 许可协议，Mate Engine 许可协议  
禁止行为：未经许可将本 Mod 集成到商业软件中，或在移除版权信息后进行再分发。
