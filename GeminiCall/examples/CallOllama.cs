using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Ollama 호환 API 호출 유틸리티.
/// MonoBehaviour 없이 사용 가능. 코루틴 실행은 호출측에서 StartCoroutine으로 감싸야 함.
/// </summary>
public static class CallOllama
{
    public const string DefaultUrl = "http://localhost:20006";

    /// <summary>
    /// /api/chat 호출. 코루틴으로 사용.
    /// </summary>
    /// <param name="serverUrl">서버 주소 (예: "http://localhost:20006")</param>
    /// <param name="model">모델명 (예: "gpt-4.1-nano")</param>
    /// <param name="messages">메시지 배열</param>
    /// <param name="onComplete">완료 콜백 (응답 텍스트, 실패 시 null)</param>
    /// <param name="temperature">temperature (null이면 생략)</param>
    /// <param name="topP">top_p (null이면 생략)</param>
    /// <param name="timeoutSec">타임아웃 초</param>
    public static IEnumerator Chat(
        string serverUrl,
        string model,
        Message[] messages,
        Action<string> onComplete,
        float? temperature = null,
        float? topP = null,
        int timeoutSec = 60)
    {
        var request = new ChatRequest
        {
            model = model,
            messages = messages,
            stream = false,
        };

        if (temperature.HasValue || topP.HasValue)
        {
            request.options = new Options();
            if (temperature.HasValue) request.options.temperature = temperature.Value;
            if (topP.HasValue) request.options.top_p = topP.Value;
        }

        string json = JsonUtility.ToJson(request);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using var web = new UnityWebRequest($"{serverUrl}/api/chat", "POST");
        web.uploadHandler = new UploadHandlerRaw(bodyRaw);
        web.downloadHandler = new DownloadHandlerBuffer();
        web.SetRequestHeader("Content-Type", "application/json");
        web.timeout = timeoutSec;

        yield return web.SendWebRequest();

        if (web.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[CallOllama] {web.error}\n{web.downloadHandler.text}");
            onComplete?.Invoke(null);
            yield break;
        }

        var response = JsonUtility.FromJson<ChatResponse>(web.downloadHandler.text);
        onComplete?.Invoke(response.message.content);
    }

    /// <summary>
    /// 단일 user 메시지로 간편 호출.
    /// </summary>
    public static IEnumerator Chat(
        string serverUrl,
        string model,
        string userMessage,
        Action<string> onComplete,
        float? temperature = null,
        float? topP = null,
        int timeoutSec = 60)
    {
        var messages = new[] { new Message { role = "user", content = userMessage } };
        yield return Chat(serverUrl, model, messages, onComplete, temperature, topP, timeoutSec);
    }

    /// <summary>
    /// MonoBehaviour owner를 받아 직접 코루틴 실행. StartCoroutine 불필요.
    /// </summary>
    public static Coroutine CorChat(
        MonoBehaviour owner,
        string serverUrl,
        string model,
        Message[] messages,
        Action<string> onComplete,
        float? temperature = null,
        float? topP = null,
        int timeoutSec = 60)
    {
        return owner.StartCoroutine(Chat(serverUrl, model, messages, onComplete, temperature, topP, timeoutSec));
    }

    /// <summary>
    /// 단일 user 메시지 + MonoBehaviour owner 간편 호출.
    /// </summary>
    public static Coroutine CorChat(
        MonoBehaviour owner,
        string serverUrl,
        string model,
        string userMessage,
        Action<string> onComplete,
        float? temperature = null,
        float? topP = null,
        int timeoutSec = 60)
    {
        return owner.StartCoroutine(Chat(serverUrl, model, userMessage, onComplete, temperature, topP, timeoutSec));
    }

    // --- JSON ---

    [Serializable]
    public class ChatRequest
    {
        public string model;
        public Message[] messages;
        public bool stream;
        public Options options;
    }

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class Options
    {
        public float temperature;
        public float top_p;
    }

    [Serializable]
    public class ChatResponse
    {
        public string model;
        public string created_at;
        public Message message;
        public bool done;
    }
}
