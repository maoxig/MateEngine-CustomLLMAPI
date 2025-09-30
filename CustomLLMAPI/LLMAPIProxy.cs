using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static LLMUnity.LLMAPIProxy;

namespace LLMUnity
{


    public class LLMAPIProxy : MonoBehaviour
    {
        [Header("Proxy Settings")]
        public int proxyPort = 13333;
        public List<LLMProxySettings.LLMProxySettingsData.APIConfigData> configs = new List<LLMProxySettings.LLMProxySettingsData.APIConfigData>();
        public int currentConfigIndex = 0;

        // Internal state
        private LLMProxySettings settingsRef;
        private Socket listenerSocket;
        public bool isRunning = false;
        private Thread acceptThread;
        private const int maxRetries = 3;
        private const int maxPortFallbacks = 10;

        public enum APIProvider { OpenAI, Anthropic, Custom }

        #region DTOs
        public class TemplateResponse { public string template; }
        public class ErrorResponse { public string error; }
        public class CompletionResponse { public string content; public bool stop; }

        public class PropsResponse
        {
            public GenerationSettings default_generation_settings;
            public int total_slots;
            public string model;
            public string system_prompt;
        }
        public class GenerationSettings { public int n_predict; public double temperature; public double top_p; }

        public class SlotResponse { public int slot; public bool in_use; public string current_task; public string user; public string system_prompt; }

        public class TokenizeResponse { public int[] tokens; }

        public class DetokenizeResponse { public string text; }

        public class StreamDataItem { public string content; public bool stop; }
        public class StreamResult { public StreamDataItem[] data; }
        public class APIMessage { public string role; public string content; }
        public class OpenAIRequest { public string model; public APIMessage[] messages; public double? temperature; public double? top_p; public int? max_tokens; public object stop; public bool stream; }
        public class AnthropicRequest { public string model; public APIMessage[] messages; public int max_tokens; public double? temperature; }
        #endregion

        void Awake()
        {
            MainThreadDispatcher.CreateIfNeeded();
            settingsRef = FindAnyObjectByType<LLMProxySettings>();
            if (settingsRef == null)
            {
                Debug.LogWarning("[LLMAPIProxy] No LLMProxySettings found in scene.");
            }
        }

        public async Task StartProxyServer()
        {
            if (listenerSocket != null && isRunning)
            {
                Debug.LogWarning("[LLM Proxy] Server already running.");
                return;
            }

            try
            {
                listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                int originalPort = proxyPort;
                bool bound = false;
                for (int i = 0; i < maxPortFallbacks; i++)
                {
                    try
                    {
                        listenerSocket.Bind(new IPEndPoint(IPAddress.Loopback, proxyPort));
                        bound = true;
                        break;
                    }
                    catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        proxyPort++;
                    }
                }

                if (!bound)
                {
                    throw new Exception($"Failed to bind after {maxPortFallbacks} attempts starting from port {originalPort}.");
                }

                if (proxyPort != originalPort)
                {
                    Debug.LogWarning($"[LLM Proxy] Port {originalPort} in use or conflicted. Falling back to port {proxyPort}.");
                    MainThreadDispatcher.Instance.Enqueue(() =>
                    {
                        if (settingsRef != null && settingsRef.portInput != null)
                        {
                            settingsRef.data.proxyPort = proxyPort;
                            settingsRef.portInput.text = proxyPort.ToString();
                        }
                    });
                }

                listenerSocket.Listen(50);
                isRunning = true;

                Debug.Log($"[LLM Proxy] Server started on port {proxyPort}");

                acceptThread = new Thread(HandleRequestsThread) { IsBackground = true, Name = "LLMProxy-AcceptThread" };
                acceptThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM Proxy] Failed to start server: {e.Message}");
                throw;
            }

            await Task.CompletedTask;
        }

        private void HandleRequestsThread()
        {
            Debug.Log("[LLM Proxy] Accept thread running.");
            while (isRunning)
            {
                try
                {
                    Socket client = listenerSocket.Accept();
                    Debug.Log($"[LLM Proxy] Accepted connection from {client.RemoteEndPoint}");
                    ThreadPool.QueueUserWorkItem(obj => ProcessSocketRequest((Socket)obj), client);
                }
                catch (SocketException se)
                {
                    if (isRunning)
                        Debug.LogError($"[LLM Proxy] Accept socket error: {se.Message}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LLM Proxy] Accept thread error: {e.Message}");
                }
            }
            Debug.Log("[LLM Proxy] Accept thread exiting.");
        }

        private void ProcessSocketRequest(Socket clientSocket)
        {
            try
            {
                using (clientSocket)
                {
                    string requestString = ReadSocketRequestBlocking(clientSocket);
                    if (string.IsNullOrEmpty(requestString))
                    {
                        Debug.LogWarning("[LLM Proxy] Empty request or read timeout.");
                        return;
                    }

                    Debug.Log($"[LLM Proxy] Received request (trunc): {requestString.Substring(0, Math.Min(200, requestString.Length))}");

                    var (method, path, headers, body) = ParseHttpRequest(requestString);

                    string responseBody;
                    int statusCode = 200;

                    if (path != null && path.ToLower().Contains("completion"))
                    {
                        try
                        {
                            var task = HandleCompletion(body);
                            responseBody = task.GetAwaiter().GetResult();
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[LLM Proxy] Forward exception: {e.Message}");
                            statusCode = 500;
                            responseBody = JsonConvert.SerializeObject(new ErrorResponse { error = e.Message });
                        }
                    }
                    else if (path != null && path.ToLower().Contains("template"))
                    {
                        responseBody = HandleTemplate();
                    }
                    else if (path != null && path.ToLower().Contains("tokenize"))
                    {
                        responseBody = HandleTokenize(body);
                    }
                    else if (path != null && path.ToLower().Contains("detokenize"))
                    {
                        responseBody = HandleDetokenize(body);
                    }
                    else if (path != null && path.ToLower().Contains("slot"))
                    {
                        responseBody = HandleSlot(body);
                    }
                    else if (path != null && path.ToLower().Contains("props") || path.ToLower().Contains("health"))
                    {
                        responseBody = HandleProps();
                    }
                    else
                    {
                        statusCode = 404;
                        responseBody = JsonConvert.SerializeObject(new ErrorResponse { error = $"Endpoint not found: {path}" });
                    }

                    string response = $"HTTP/1.1 {statusCode} {(statusCode == 200 ? "OK" : "Not Found")}\r\n" +
                                      "Content-Type: application/json\r\n" +
                                      $"Content-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\n" +
                                      "Connection: close\r\n\r\n" +
                                      responseBody;

                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);

                    try
                    {
                        int sent = 0;
                        while (sent < responseBytes.Length)
                        {
                            int s = clientSocket.Send(responseBytes, sent, responseBytes.Length - sent, SocketFlags.None);
                            if (s <= 0) break;
                            sent += s;
                        }
                        Debug.Log($"[LLM Proxy] Response sent ({sent} bytes) to {clientSocket.RemoteEndPoint}");
                    }
                    catch (SocketException se)
                    {
                        Debug.LogError($"[LLM Proxy] Send socket error: {se.Message}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[LLM Proxy] Send error: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM Proxy] ProcessSocketRequest error: {e.Message}");
            }
        }

        private string HandleProps()
        {
            string currentModel = configs.Count > 0 ? configs[currentConfigIndex].model : "unknown";
            return JsonConvert.SerializeObject(new PropsResponse
            {
                default_generation_settings = new GenerationSettings { n_predict = 128, temperature = 0.7f, top_p = 0.9 },
                total_slots = 1,
                model = currentModel,
                system_prompt = ""
            });
        }

        private string HandleTemplate()
        {
            string currentTemplate = configs.Count > 0 ? configs[currentConfigIndex].chatTemplate : "chatml";
            return JsonConvert.SerializeObject(new TemplateResponse { template = currentTemplate });
        }

        private async Task<string> HandleCompletion(string requestBody)
        {
            try
            {
                var apiResponse = await ForwardToAPI(requestBody);
                return apiResponse;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM Proxy] Completion error: {e.Message}");
                var cr = new CompletionResponse { content = $"Error: {e.Message}", stop = true };
                return JsonConvert.SerializeObject(cr);
            }
        }

        private string HandleSlot(string body)
        {
            return JsonConvert.SerializeObject(new SlotResponse { slot = 0, in_use = false, current_task = "", user = "", system_prompt = "" });
        }

        private string HandleDetokenize(string body)
        {
            return JsonConvert.SerializeObject(new DetokenizeResponse { text = "" });
        }

        private string HandleTokenize(string body)
        {
            return JsonConvert.SerializeObject(new TokenizeResponse { tokens = new int[0] });
        }

        private string ReadSocketRequestBlocking(Socket socket)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int total = 0;

                    while (true)
                    {
                        int read = socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                        if (read <= 0) break;
                        ms.Write(buffer, 0, read);
                        total += read;

                        if (ms.Length > 20 * 1024 * 1024)
                        {
                            Debug.LogError("[LLM Proxy] Request too large.");
                            return null;
                        }

                        string s = Encoding.UTF8.GetString(ms.ToArray());
                        int headerEnd = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (headerEnd >= 0)
                        {
                            string headerPart = s.Substring(0, headerEnd);
                            int contentLength = 0;
                            string[] headerLines = headerPart.Split(new[] { "\r\n" }, StringSplitOptions.None);
                            for (int i = 1; i < headerLines.Length; i++)
                            {
                                var h = headerLines[i];
                                int idx = h.IndexOf(':');
                                if (idx > 0)
                                {
                                    var key = h.Substring(0, idx).Trim();
                                    var val = h.Substring(idx + 1).Trim();
                                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int.TryParse(val, out contentLength);
                                    }
                                }
                            }

                            int bodyAlready = total - (headerEnd + 4);
                            int need = contentLength - bodyAlready;
                            while (need > 0)
                            {
                                int r = socket.Receive(buffer, 0, Math.Min(buffer.Length, need), SocketFlags.None);
                                if (r <= 0) break;
                                ms.Write(buffer, 0, r);
                                total += r;
                                need -= r;
                            }

                            break;
                        }
                    }

                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch (SocketException se)
            {
                Debug.LogError($"[LLM Proxy] Read socket exception: {se.Message}");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM Proxy] Read error: {e.Message}");
                return null;
            }
        }

        private (string method, string path, Dictionary<string, string> headers, string body) ParseHttpRequest(string request)
        {
            if (string.IsNullOrEmpty(request)) return (null, null, null, null);

            var lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return (null, null, null, null);

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return (null, null, null, null);
            string method = requestLine[0];
            string path = requestLine[1];

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int bodyStartIndex = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                {
                    bodyStartIndex = i + 1;
                    break;
                }
                var headerParts = lines[i].Split(new[] { ": " }, 2, StringSplitOptions.None);
                if (headerParts.Length == 2)
                {
                    headers[headerParts[0]] = headerParts[1];
                }
            }

            string body = "";
            if (bodyStartIndex > 0 && bodyStartIndex < lines.Length)
            {
                body = string.Join("\r\n", lines, bodyStartIndex, lines.Length - bodyStartIndex);
            }

            return (method, path, headers, body);
        }

        private async Task<string> ForwardToAPI(string llamaCppRequest)
        {
            if (configs.Count == 0)
            {
                throw new Exception("No API configurations available.");
            }

            int attempts = 0;
            while (attempts < maxRetries)
            {
                try
                {
                    var currentConfig = configs[currentConfigIndex];
                    var llmRequest = JsonConvert.DeserializeObject<Dictionary<string, object>>(llamaCppRequest ?? "{}");
                    string apiRequestBody = ConvertToAPIFormat(llmRequest, currentConfig);

                    string authHeader = $"Bearer {currentConfig.apiKey}";
                    string responseBody = await MainThreadDispatcher.Instance.RunRequestOnMainThread(currentConfig.apiEndpoint, "POST", apiRequestBody, authHeader);

                    if (string.IsNullOrEmpty(responseBody))
                    {
                        throw new Exception("API returned empty response.");
                    }

                    return ConvertFromAPIFormat(responseBody, llmRequest, currentConfig);
                }
                catch (Exception e)
                {
                    attempts++;
                    if (attempts >= maxRetries)
                    {
                        throw new Exception($"All retries failed after {maxRetries} attempts: {e.Message}");
                    }
                    currentConfigIndex = (currentConfigIndex + 1) % configs.Count;
                    Debug.LogError($"[LLM Proxy] API request failed (attempt {attempts}/{maxRetries}): {e.Message}. Switching to next config.");
                }
            }

            throw new Exception("Unexpected end of retry loop.");
        }

        private string ConvertToAPIFormat(Dictionary<string, object> llmRequest, LLMProxySettings.LLMProxySettingsData.APIConfigData config)
        {
            switch (config.provider)
            {
                case APIProvider.OpenAI:
                case APIProvider.Custom:
                    {
                        var req = new OpenAIRequest
                        {
                            model = config.model,
                            stream = llmRequest != null && llmRequest.ContainsKey("stream") && Convert.ToBoolean(llmRequest["stream"])
                        };

                        if (llmRequest != null && llmRequest.ContainsKey("prompt"))
                            req.messages = new[] { new APIMessage { role = "user", content = llmRequest["prompt"]?.ToString() ?? "" } };

                        if (llmRequest != null && llmRequest.ContainsKey("temperature"))
                        {
                            if (double.TryParse(llmRequest["temperature"].ToString(), out double tmp)) req.temperature = tmp;
                        }

                        if (llmRequest != null && llmRequest.ContainsKey("top_p"))
                        {
                            if (double.TryParse(llmRequest["top_p"].ToString(), out double tp)) req.top_p = tp;
                        }

                        if (llmRequest != null && llmRequest.ContainsKey("n_predict"))
                        {
                            if (int.TryParse(llmRequest["n_predict"].ToString(), out int np) && np > 0) req.max_tokens = np;
                        }

                        if (llmRequest != null && llmRequest.ContainsKey("stop")) req.stop = llmRequest["stop"];

                        return JsonConvert.SerializeObject(req);
                    }

                case APIProvider.Anthropic:
                    {
                        var req = new AnthropicRequest { model = config.model, max_tokens = 1024 };
                        if (llmRequest != null && llmRequest.ContainsKey("prompt"))
                            req.messages = new[] { new APIMessage { role = "user", content = llmRequest["prompt"]?.ToString() ?? "" } };

                        if (llmRequest != null && llmRequest.ContainsKey("n_predict"))
                            if (int.TryParse(llmRequest["n_predict"].ToString(), out int np) && np > 0) req.max_tokens = np;

                        if (llmRequest != null && llmRequest.ContainsKey("temperature"))
                            if (double.TryParse(llmRequest["temperature"].ToString(), out double tmp)) req.temperature = tmp;

                        return JsonConvert.SerializeObject(req);
                    }

                default:
                    return "{}";
            }
        }

        private string ConvertFromAPIFormat(string apiResponse, Dictionary<string, object> originalRequest, LLMProxySettings.LLMProxySettingsData.APIConfigData config)
        {
            try
            {
                if (string.IsNullOrEmpty(apiResponse))
                {
                    var err = new CompletionResponse { content = "Error: Empty response from API", stop = true };
                    return JsonConvert.SerializeObject(err);
                }

                JToken responseToken = JToken.Parse(apiResponse);
                return ConvertNormalResponse(responseToken, originalRequest);
            }
            catch (JsonException e)
            {
                Debug.LogError($"[LLM Proxy] JSON parsing error: {e.Message}");
                var err = new CompletionResponse { content = $"Error: Failed to parse API response - {e.Message}", stop = true };
                return JsonConvert.SerializeObject(err);
            }
        }

        private string ConvertNormalResponse(JToken response, Dictionary<string, object> originalRequest)
        {
            string content = "";

            try
            {
                var choices = response["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    var first = choices[0];
                    var message = first["message"];
                    if (message != null && message["content"] != null) content = message["content"].ToString();
                    else if (first["text"] != null) content = first["text"].ToString();
                    else if (first["delta"] != null && first["delta"]["content"] != null) content = first["delta"]["content"].ToString();
                }

                if (string.IsNullOrEmpty(content))
                {
                    var contentToken = response["content"];
                    if (contentToken != null)
                    {
                        if (contentToken is JArray arr && arr.Count > 0)
                        {
                            var first = arr[0];
                            if (first["text"] != null) content = first["text"].ToString();
                            else content = first.ToString();
                        }
                        else if (contentToken.Type == JTokenType.String) content = contentToken.ToString();
                        else if (contentToken["text"] != null) content = contentToken["text"].ToString();
                    }
                }

                if (string.IsNullOrEmpty(content))
                {
                    if (response["completion"] != null) content = response["completion"].ToString();
                    else if (response["text"] != null) content = response["text"].ToString();
                    else if (response["output"] != null) content = response["output"].ToString();
                }

                if (string.IsNullOrEmpty(content))
                {
                    Debug.LogWarning($"[LLM Proxy] No content found in API response");
                    Debug.LogWarning($"[LLM Proxy] Full response: {response.ToString(Formatting.None)}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM Proxy] Error parsing response content: {e.Message}");
            }

            bool isStream = originalRequest != null && originalRequest.ContainsKey("stream") && Convert.ToBoolean(originalRequest["stream"]);
            if (isStream)
            {
                var result = new StreamResult { data = new[] { new StreamDataItem { content = content, stop = true } } };
                return JsonConvert.SerializeObject(result);
            }
            else
            {
                var result = new CompletionResponse { content = content, stop = true };
                return JsonConvert.SerializeObject(result);
            }
        }

        void OnDestroy()
        {
            StopProxyServer();
        }

        public void StopProxyServer()
        {
            isRunning = false;

            try
            {
                listenerSocket?.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LLM Proxy] Error closing listener: {e.Message}");
            }
            listenerSocket = null;

            try
            {
                if (acceptThread != null && acceptThread.IsAlive)
                {
                    acceptThread.Join(100);
                }
            }
            catch { }

            Debug.Log("[LLM Proxy] Server stopped");
        }
    }
}