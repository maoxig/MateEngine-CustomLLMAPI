using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;


/// <summary>
/// MainThreadDispatcher
/// - 确保 UnityWebRequest 在主线程创建/执行
/// - 暴露 Task<string> RunRequestOnMainThread(...) 接口供后台线程 await
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    private static MainThreadDispatcher _instance;
    public static MainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null) CreateIfNeeded();
            return _instance;
        }
    }

    public static void CreateIfNeeded()
    {
        if (_instance != null) return;
        var go = new GameObject("MainThreadDispatcher");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MainThreadDispatcher>();
    }

    /// <summary>
    /// 在主线程上执行 UnityWebRequest 并返回响应文本（总是创建并运行 UWR 在协程中）
    /// </summary>
    public Task<string> RunRequestOnMainThread(string url, string method, string requestBody, string authHeader)
    {
        var tcs = new TaskCompletionSource<string>();
        StartCoroutine(RunRequestCoroutine(url, method, requestBody ?? "", authHeader ?? "", tcs));
        return tcs.Task;
    }

    private System.Collections.IEnumerator RunRequestCoroutine(string url, string method, string requestBody, string authHeader, TaskCompletionSource<string> tcs)
    {
        UnityWebRequest request = null;
        try
        {
            request = new UnityWebRequest(url, method);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody ?? "");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(authHeader))
            {
                request.SetRequestHeader("Authorization", authHeader);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LLM Proxy] UnityWebRequest coroutine error: {e.Message}");
            var errResp = new Dictionary<string, string> { { "error", $"API request failed: {e.Message}" } };
            tcs.SetResult(JsonConvert.SerializeObject(errResp));
            yield break;
        }

        var operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)

        {
            Debug.LogError($"[LLM Proxy] UnityWebRequest error: {request.error}\nResponse: {request.downloadHandler?.text}");
            var errResp = new Dictionary<string, string> { { "error", $"API request failed: {request.error}" } };
            tcs.SetResult(JsonConvert.SerializeObject(errResp));
        }
        else
        {
            tcs.SetResult(request.downloadHandler.text);
        }

        if (request != null)
        {
            request.Dispose();
        }
    }
}
