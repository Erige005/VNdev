using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace VNdev.App.Ai;

/// <summary>Một lần AI muốn gọi công cụ, ví dụ đọc một cảnh.</summary>
public sealed record ToolCall(string Id, string Name, JsonObject Arguments);

public sealed record ToolResult(string CallId, string Content, bool IsError);

/// <summary>Một dịch vụ AI có sẵn trong danh sách — xem <see cref="Providers.Presets"/>.</summary>
public sealed record ProviderPreset(string Id, string Name, string Vendor, AiProviderKind Kind, string BaseUrl, bool NeedsKey, string KeyUrl, string Blurb);

/// <summary>Một công cụ AI được dùng: tên, mô tả, JSON Schema tham số.</summary>
public sealed record ToolSpec(string Name, string Description, JsonObject Schema);

public enum ChatRole { User, Assistant }

/// <summary>
/// Một lượt trong cuộc trò chuyện, ở dạng trung lập — mỗi provider tự dịch
/// sang định dạng riêng của nó lúc gửi đi.
/// </summary>
/// <remarks>
/// <see cref="Native"/> giữ nguyên nội dung gốc provider trả về (với Claude là
/// cả khối "thinking" kèm chữ ký). Claude bắt buộc gửi lại đúng khối đó ở lượt
/// sau khi đang dùng công cụ, dịch qua dạng trung lập rồi dịch ngược là mất.
/// Đổi sang provider khác giữa chừng thì phần gốc bị bỏ qua, dùng phần trung lập.
/// </remarks>
public sealed class ChatTurn
{
    public ChatRole Role { get; init; }
    public string Text { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; } = new();
    public List<ToolResult> ToolResults { get; } = new();

    public object? Native { get; set; }
    public string? NativeProviderKind { get; set; }
}

public sealed class ChatRequest
{
    public required string System { get; init; }
    public required IReadOnlyList<ChatTurn> Turns { get; init; }
    public required IReadOnlyList<ToolSpec> Tools { get; init; }
}

/// <summary>Những gì provider báo về trong lúc trả lời, theo thứ tự thời gian.</summary>
public abstract record StreamEvent;
public sealed record TextDelta(string Text) : StreamEvent;
public sealed record ThinkingStarted : StreamEvent;
public sealed record TurnFinished(ChatTurn Turn, string StopReason) : StreamEvent;

/// <summary>Lỗi người dùng tự sửa được (sai khoá, hết hạn mức, mất mạng) — báo bằng lời dễ hiểu.</summary>
public sealed class AiException : Exception
{
    public AiException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IChatProvider
{
    IAsyncEnumerable<StreamEvent> Stream(ChatRequest request, CancellationToken ct);

    /// <summary>Danh sách model provider đang có, để người dùng chọn thay vì gõ tay.</summary>
    System.Threading.Tasks.Task<List<string>> ListModels(CancellationToken ct);
}

public static class Providers
{
    public static IChatProvider Create(AiProviderConfig config)
    {
        var key = config.NeedsKey ? CredentialStore.Get(config.Id) : null;
        if (config.NeedsKey && string.IsNullOrEmpty(key))
        {
            throw new AiException($"Chưa có API key cho \"{config.Name}\". Vào Cài đặt → AI Providers để dán khoá.");
        }
        return config.Kind == AiProviderKind.Anthropic
            ? new AnthropicProvider(config, key!)
            : new OpenAiCompatProvider(config, key);
    }

    /// <summary>
    /// Danh sách dịch vụ có sẵn — người dùng chỉ chọn và dán khoá, không phải
    /// biết địa chỉ API. Id cố định để khoá trong Credential Manager gắn đúng
    /// dịch vụ qua các lần cập nhật app.
    /// </summary>
    /// <remarks>
    /// Chỉ Claude có model gợi ý sẵn. Dịch vụ khác đổi tên model liên tục, nên
    /// app hỏi thẳng dịch vụ danh sách model tài khoản đó dùng được ngay khi
    /// khoá được lưu, thay vì ghi cứng một danh sách sẽ sớm lỗi thời.
    /// </remarks>
    public static readonly ProviderPreset[] Presets =
    {
        new("anthropic", "Claude", "Anthropic", AiProviderKind.Anthropic, "https://api.anthropic.com", true,
            "https://console.anthropic.com/settings/keys", "Viết văn tiếng Việt tự nhiên, giữ giọng nhân vật tốt."),
        new("openai", "ChatGPT", "OpenAI", AiProviderKind.OpenAiCompatible, "https://api.openai.com/v1", true,
            "https://platform.openai.com/api-keys", "Các model GPT của OpenAI."),
        new("gemini", "Gemini", "Google", AiProviderKind.OpenAiCompatible, "https://generativelanguage.googleapis.com/v1beta/openai", true,
            "https://aistudio.google.com/apikey", "Có gói miễn phí, đọc được ngữ cảnh rất dài."),
        new("deepseek", "DeepSeek", "DeepSeek", AiProviderKind.OpenAiCompatible, "https://api.deepseek.com/v1", true,
            "https://platform.deepseek.com/api_keys", "Giá rẻ."),
        new("openrouter", "OpenRouter", "OpenRouter", AiProviderKind.OpenAiCompatible, "https://openrouter.ai/api/v1", true,
            "https://openrouter.ai/keys", "Một khoá dùng được hàng trăm model của nhiều hãng."),
        new("xai", "Grok", "xAI", AiProviderKind.OpenAiCompatible, "https://api.x.ai/v1", true,
            "https://console.x.ai", "Các model Grok của xAI."),
        new("ollama", "Ollama", "chạy trên máy", AiProviderKind.OpenAiCompatible, "http://localhost:11434/v1", false,
            "https://ollama.com/download", "Miễn phí, chạy ngay trên máy bạn, không cần khoá."),
    };

    public static ProviderPreset? PresetFor(AiProviderConfig config)
        => Presets.FirstOrDefault(p => p.Id == config.Id
            || (p.Kind == config.Kind && string.Equals(p.BaseUrl, config.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Lọc bỏ model không dùng để chat (tạo ảnh, đọc giọng, nhúng vector…) khỏi
    /// danh sách dịch vụ trả về, để người dùng không chọn nhầm.
    /// </summary>
    public static List<string> ChatModelsOnly(IEnumerable<string> models)
    {
        string[] skip = { "embed", "tts", "whisper", "dall-e", "moderation", "audio", "image", "transcribe", "realtime", "babbage", "davinci", "rerank", "imagen", "veo", "aqa" };
        return models.Where(m => !skip.Any(k => m.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Model Claude gợi ý sẵn; bấm "Tải danh sách" để lấy đầy đủ từ tài khoản.</summary>
    public static readonly string[] ClaudeModels = { "claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5", "claude-fable-5-1" };
}
