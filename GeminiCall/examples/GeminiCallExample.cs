using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// GeminiCall 서버의 Ollama 호환 /api/chat 엔드포인트 호출 예제.
/// 빈 GameObject에 붙여서 사용.
/// </summary>
public class GeminiCallExample : MonoBehaviour
{
    [Header("Server")]
    [SerializeField] private string serverUrl = "http://localhost:20006";

    [Header("Model")]
    [SerializeField] private string model = "gpt-4.1-nano";

    [Header("Prompt")]
    [TextArea(3, 10)]
    [SerializeField] private string prompt = "Unity에서 오브젝트를 회전시키는 방법을 알려줘.";

    void Start()
    {
        StartCoroutine(SendChat(prompt, response =>
        {
            Debug.Log($"응답: {response}");
        }));
    }

    public IEnumerator SendChat(string userMessage, Action<string> onComplete)
    {
        var requestBody = new ChatRequest
        {
            model = model,
            messages = new Message[]
            {
                new Message { role = "user", content = userMessage }
            },
            stream = false,
            options = new Options
            {
                temperature = 0.5f,
                top_p = 0.9f
            }
        };

        string json = JsonUtility.ToJson(requestBody);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using var request = new UnityWebRequest($"{serverUrl}/api/chat", "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 60;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error: {request.error}\n{request.downloadHandler.text}");
            onComplete?.Invoke(null);
            yield break;
        }

        var response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
        onComplete?.Invoke(response.message.content);
    }

    // --- JSON 직렬화용 클래스 ---

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
