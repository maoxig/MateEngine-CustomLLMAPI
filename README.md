# MateEngine Custom LLM API Mod

## Introduction

This mod allows you to replace MateEngine's local LLM with remote LLM providers (such as ChatGPT, DeepSeek, Claude, etc.) via their APIs. Once enabled, the mod starts a proxy locally (default port 13333, customizable), forwarding requests from Character to the remote LLM API.

![alt text](resources/intro.png)

The reason for developing this mod is that running a local large model (even quantized) is challenging for most users. As a desktop pet game, constantly running a local large model is excessive (and usually unnecessary). In contrast, remote LLM API providers often offer better performance and higher availability, without occupying memory or VRAM. Many APIs are now very affordable, and some are even free. Therefore, this mod aims to provide MateEngine users with a convenient way to use remote LLM APIs.

## Features

- Switch between local and remote LLMs (due to LLMUnity limitations, if already connected—input box shows "talk to me"—you need to restart the game to switch)
- Supports any LLM service compatible with the OpenAI Chat Completions API specification, as well as some other compatible services
- Fully compatible with MateEngine's original mechanisms (character memory, prompts, chat history, etc.), as it only forwards requests to the remote LLM API
- Save multiple LLM API configurations, support one-click switching and automatic fallback
- Real-time error messages in the UI for troubleshooting
- Settings are saved in `C:\Users\{YourUsername}\AppData\LocalLow\Shinymoon\MateEngineX\LLMProxySettings.json`, editable manually

## Installation

Similar to CustomDancePlayer installation steps, see [https://github.com/maoxig/MateEngine-CustomDancePlayer?tab=readme-ov-file#installation-steps](https://github.com/maoxig/MateEngine-CustomDancePlayer?tab=readme-ov-file#installation-steps). The only difference is the DLL name.

## User Guide

1. Press `J` to open/close the configuration panel. After configuring, click Save to enable.
2. Enter the API Endpoint (e.g., `https://api.openai.com/v1/chat/completions` or `https://api.deepseek.com/v1/chat/completions`). Refer to the relevant API documentation to ensure the path is correct.
3. Enter the API key and configure other parameters (such as model name). As testing is still limited, not all LLM providers may be supported yet—future updates will improve compatibility.
4. Start MateEngine's chat feature. If the proxy server starts successfully, the input box will change from loading to "Talk to me." Note: This does not guarantee successful requests; only when the proxy server successfully requests the remote LLM API will you receive a reply.

## Future Plans

- Support for streaming responses (currently disabled, pending bug fixes)
- More convenient character prompt configuration
- More customizable parameters (LLM invocation related)
- More flexible API configuration to support more LLM providers
- Suggestions and PRs are welcome

## Security & Disclaimer

Since this involves port listening and API keys, please ensure you trust this mod. Any consequences from using this mod are the user's responsibility. The author only maintains the mod itself and is not responsible for LLM API-related issues.

## Contribution Guide

The author has only tested on DeepSeek. Developers familiar with various LLM APIs are welcome to help maintain. The specific API adaptation code is in `LLMAPIProxy.cs`. To test:
- Clone this repo to Visual Studio
- Change project references to the DLLs in your local MateEngine installation under `MateEngineX_Data/Managed`
- Modify code, compile the DLL, and place it in MateEngine's `MateEngineX_Data/Managed` directory
- Start MateEngine to test
- If you need to modify UI-related code, use MateEngine's Unity project, drag the provided DLL and prefab into the project for editing, and export the prefab as .me

## Other Notes

This mod's implementation is straightforward, but since MateEngine is based on Mono and has trimmed many APIs, some functions require manual low-level implementation. If you have better ideas, feel free to suggest.

## License

This mod uses the MIT License and complies with Mate Engine's official license. Personal non-commercial use, modification, and distribution are allowed, but commercial use is prohibited. Using this mod means you agree to Mate Engine's official terms. The developer is not responsible for any game errors or issues caused by this mod.

License details: MIT License, Mate Engine License  
Prohibited: Integrating this mod into commercial software without permission, or redistributing after removing copyright information.