using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LLMUnity
{
    public class ActionQueue
    {
        private class Node
        {
            public Action action;
            public Node next;
            public Node(Action a) { action = a; next = null; }
        }

        private Node head;
        private Node tail;
        private int count;

        public int Count => count;

        public void Enqueue(Action action)
        {
            if (action == null) return;
            Node newNode = new Node(action);
            lock (this)
            {
                if (head == null)
                {
                    head = tail = newNode;
                }
                else
                {
                    tail.next = newNode;
                    tail = newNode;
                }
                count++;
            }
        }

        public Action Dequeue()
        {
            lock (this)
            {
                if (head == null) return null;
                var action = head.action;
                head = head.next;
                if (head == null) tail = null;
                count--;
                return action;
            }
        }
    }

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

        private ActionQueue actions = new ActionQueue();

        public static void CreateIfNeeded()
        {
            if (_instance != null) return;
            var go = new GameObject("MainThreadDispatcher");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThreadDispatcher>();
        }

        public void Enqueue(Action action)
        {
            actions.Enqueue(action);
        }

        void Update()
        {
            while (actions.Count > 0)
            {
                var action = actions.Dequeue();
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MainThreadDispatcher] Action execution error: {e.Message}");
                }
            }
        }

        public Task<string> RunRequestOnMainThread(string url, string method, string requestBody, string authHeader)
        {
            var tcs = new TaskCompletionSource<string>();
            StartCoroutine(RunRequestCoroutine(url, method, requestBody ?? "", authHeader ?? "", tcs));
            return tcs.Task;
        }

        private IEnumerator RunRequestCoroutine(string url, string method, string requestBody, string authHeader, TaskCompletionSource<string> tcs)
        {
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(url, method);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
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
                Debug.LogError($"[LLM Proxy] UnityWebRequest creation error: {e.Message}");
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

            request.Dispose();
        }
    }
}