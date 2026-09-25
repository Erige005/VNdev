using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace VNdev.App.Ai;

/// <summary>
/// Gọi mọi dịch vụ nói chuẩn "chat completions" của OpenAI: OpenAI, Gemini,
/// DeepSeek, OpenRouter, Grok, Ollama… Một lớp cho tất cả vì họ cố tình dùng
/// chung định dạng để người dùng đổi qua lại chỉ bằng địa chỉ và khoá.
/// </summary>
public sealed class OpenAiCompatProvider : IChatProvider
{
    // Một HttpClient dùng chung cả app — tạo mới mỗi lần gọi làm cạn cổng mạng
    // khi người dùng chat liên tục.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // Giữ nguyên chữ có dấu thay vì escape thành mã unicode — escape làm request
    // dài gấp mấy lần, tốn token và chậm hơn mà không được gì.
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly AiProviderConfig _config;
    private readonly string? _key;

    public OpenAiCompatProvider(AiProviderConfig config, string? key)
    {
        _config = config;
        _key = key;
    }

    private string BaseUrl => _config.BaseUrl.TrimEnd('/');

    public async Task<List<string>> ListModels(CancellationToken ct)
    {
        using var res = await Send(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models"), HttpCompletionOption.ResponseContentRead, ct);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        return (body?["data"] as JsonArray ?? new JsonArray())
            .Select(m => m?["id"]?.GetValue<string>())
            .OfType<string>()
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async IAsyncEnumerable<StreamEvent> Stream(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.Model))
        {
            throw new AiException($"Chưa chọn model cho \"{_config.Name}\". Vào Cài đặt → AI Providers, bấm Tải danh sách model rồi chọn một.");
        }

        var body = new JsonObject
        {
            ["model"] = _config.Model.Trim(),
            ["stream"] = true,
            ["messages"] = BuildMessages(request),
        };
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.Schema.DeepClone(),
                },
            }).ToArray());
        }

        var json = body.ToJsonString(Relaxed);
        using var res = await Send(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }, HttpCompletionOption.ResponseHeadersRead, ct);
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var text = new StringBuilder();
        var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
        // Trường riêng của từng hãng đi kèm lời gọi công cụ (Gemini để chữ ký
        // suy nghĩ ở "extra_content") — phải gửi trả nguyên vẹn ở lượt sau.
        var extras = new Dictionary<int, JsonObject>();
        var finish = "stop";
        var thinkingShown = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            JsonNode? chunk;
            try { chunk = JsonNode.Parse(data); }
            catch (JsonException) { continue; }

            if (chunk?["error"] is JsonNode err)
            {
                throw new AiException($"{_config.Name} báo lỗi: {err["message"]?.GetValue<string>() ?? err.ToJsonString()}");
            }

            var choice = chunk?["choices"]?[0];
            if (choice is null) continue;
            if (choice["finish_reason"]?.GetValue<string>() is { } fr) finish = fr;

            var delta = choice["delta"];
            if (delta is null) continue;

            // DeepSeek, Qwen… gửi phần suy luận riêng — không hiện ra, chỉ báo "đang nghĩ".
            if (!thinkingShown && delta["reasoning_content"] is JsonValue)
            {
                thinkingShown = true;
                yield return new ThinkingStarted();
            }

            if (delta["content"] is JsonValue c && c.GetValue<string>() is { Length: > 0 } piece)
            {
                text.Append(piece);
                yield return new TextDelta(piece);
            }

            if (delta["tool_calls"] is JsonArray toolDeltas)
            {
                foreach (var td in toolDeltas)
                {
                    var index = td?["index"]?.GetValue<int>() ?? 0;
                    if (!calls.TryGetValue(index, out var call)) call = ("", "", new StringBuilder());
                    if (td?["id"]?.GetValue<string>() is { Length: > 0 } id) call.Id = id;
                    if (td?["function"]?["name"]?.GetValue<string>() is { Length: > 0 } name) call.Name = name;
                    if (td?["function"]?["arguments"]?.GetValue<string>() is { } args) call.Args.Append(args);
                    calls[index] = call;
                    if (td is JsonObject tdObj)
                    {
                        foreach (var (k, v) in tdObj)
                        {
                            if (k is "index" or "id" or "type" or "function" || v is null) continue;
                            if (!extras.TryGetValue(index, out var ex)) extras[index] = ex = new JsonObject();
                            ex[k] = v.DeepClone();
                        }
                    }
                }
            }
        }

        var turn = new ChatTurn { Role = ChatRole.Assistant, Text = text.ToString(), NativeProviderKind = NativeKind };
        var nativeCalls = new JsonArray();
        foreach (var (index, call) in calls)
        {
            JsonObject args;
            try { args = JsonNode.Parse(call.Args.Length == 0 ? "{}" : call.Args.ToString()) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { args = new JsonObject { ["__invalid_json"] = call.Args.ToString() }; }
            var id = call.Id.Length > 0 ? call.Id : $"call_{Guid.NewGuid():N}"[..20];
            turn.ToolCalls.Add(new ToolCall(id, call.Name, args));

            var raw = new JsonObject
            {
                ["id"] = id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Args.Length == 0 ? "{}" : call.Args.ToString() },
            };
            if (extras.TryGetValue(index, out var extra)) foreach (var (k, v) in extra) raw[k] = v?.DeepClone();
            nativeCalls.Add(raw);
        }
        var native = new JsonObject { ["role"] = "assistant", ["content"] = turn.Text };
        if (nativeCalls.Count > 0) native["tool_calls"] = nativeCalls;
        turn.Native = native;

        var stop = finish switch
        {
            "tool_calls" => "tool_use",
            "length" => "max_tokens",
            "content_filter" => "refusal",
            _ => turn.ToolCalls.Count > 0 ? "tool_use" : "end_turn",
        };
        yield return new TurnFinished(turn, stop);
    }

    /// <summary>Lượt gốc chỉ gửi lại đúng dịch vụ đã sinh ra nó — trường riêng của hãng này hãng khác không hiểu.</summary>
    private string NativeKind => $"openai:{BaseUrl}";

    private JsonArray BuildMessages(ChatRequest request)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.System } };
        foreach (var turn in request.Turns)
        {
            if (turn.Role == ChatRole.Assistant && turn.Native is JsonObject nativeMsg && turn.NativeProviderKind == NativeKind)
            {
                messages.Add(nativeMsg.DeepClone());
            }
            else if (turn.Role == ChatRole.Assistant)
            {
                var msg = new JsonObject { ["role"] = "assistant", ["content"] = turn.Text };
                if (turn.ToolCalls.Count > 0)
                {
                    msg["tool_calls"] = new JsonArray(turn.ToolCalls.Select(c => (JsonNode)new JsonObject
                    {
                        ["id"] = c.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.ToJsonString(Relaxed) },
                    }).ToArray());
                }
                messages.Add(msg);
            }
            else
            {
                foreach (var result in turn.ToolResults)
                {
                    messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = result.CallId, ["content"] = result.Content });
                }
                if (turn.Text.Length > 0) messages.Add(new JsonObject { ["role"] = "user", ["content"] = turn.Text });
            }
        }
        return messages;
    }

    private void Authorize(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
    }

    /// <summary>
    /// Gửi yêu cầu, tự thử lại khi dịch vụ quá tải tạm thời.
    /// </summary>
    /// <remarks>
    /// Lỗi 503/429/5xx thường tự hết sau vài giây (Gemini hay báo "high demand"
    /// lúc đông người). Thử lại tối đa 3 lần, chờ lâu dần, trước khi làm phiền
    /// người dùng. Nhận hàm dựng request thay vì request có sẵn vì .NET không
    /// cho gửi lại cùng một HttpRequestMessage.
    /// </remarks>
    private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> build, HttpCompletionOption option, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            using var req = build();
            Authorize(req);
            HttpResponseMessage res;
            try
            {
                res = await Http.SendAsync(req, option, ct);
            }
            catch (HttpRequestException ex)
            {
                var hint = BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                    ? " Ollama đã chạy chưa?"
                    : " Kiểm tra mạng và địa chỉ API.";
                throw new AiException($"Không kết nối được tới {_config.Name}.{hint}", ex);
            }

            if (res.IsSuccessStatusCode) return res;

            var status = res.StatusCode;
            var transient = status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (transient && attempt < maxAttempts)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * 1.5);
                if (wait > TimeSpan.FromSeconds(20)) wait = TimeSpan.FromSeconds(20);
                res.Dispose();
                await Task.Delay(wait, ct);
                continue;
            }

            var detail = await res.Content.ReadAsStringAsync(ct);
            // Có dịch vụ trả lỗi dạng object, có dịch vụ (Gemini) bọc trong mảng.
            try
            {
                var node = JsonNode.Parse(detail);
                if (node is JsonArray arr && arr.Count > 0) node = arr[0];
                detail = node?["error"]?["message"]?.GetValue<string>() ?? detail;
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
            res.Dispose();

            throw status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AiException($"Khoá API của \"{_config.Name}\" không đúng hoặc không có quyền. Kiểm tra lại ở Cài đặt → AI Providers."),
                HttpStatusCode.TooManyRequests => new AiException($"{_config.Name} báo quá nhiều yêu cầu hoặc hết hạn mức của tài khoản. Đã thử lại {maxAttempts - 1} lần. Đợi một lát, hoặc đổi sang model khác ở đầu khung chat."),
                HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError =>
                    new AiException($"Máy chủ {_config.Name} đang quá tải (lỗi {(int)status}) — đã thử lại {maxAttempts - 1} lần vẫn chưa được. Đây là lỗi tạm thời phía {_config.Name}: thử lại sau ít phút, hoặc đổi sang model khác ở đầu khung chat."),
                HttpStatusCode.NotFound => new AiException($"{_config.Name} không có model hoặc địa chỉ này ({detail})."),
                _ => new AiException($"{_config.Name} báo lỗi {(int)status}: {Shorten(detail)}"),
            };
        }
    }

    private static string Shorten(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
