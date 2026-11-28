using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Calc.Protocol;

// One JSON object per line, each way: requests from the Client on the worker's stdin, replies on its stdout. The "type"
// field says which. Later sessions add fields, and name this file in their Files.

/// <summary>A request to the worker. Every one carries an id, which its reply echoes.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EvalRequest), "eval")]
[JsonDerivedType(typeof(DigitsRequest), "digits")]
[JsonDerivedType(typeof(ReformatRequest), "reformat")]
[JsonDerivedType(typeof(FetchRequest), "fetch")]
[JsonDerivedType(typeof(FunctionsRequest), "functions")]
[JsonDerivedType(typeof(StopRequest), "stop")]
[JsonDerivedType(typeof(PingRequest), "ping")]
public abstract record Request;

/// <summary>
/// Evaluate <paramref name="Source"/> in <paramref name="Context"/>. <paramref name="Ans"/> is sent only when the source
/// mentions it. Replies <see cref="ResultReply"/> or <see cref="ErrorReply"/>.
/// </summary>
public sealed record EvalRequest(long Id, string Source, EvalContext Context, AnsValue? Ans = null, TextStyle? Options = null) : Request;

/// <summary>More digits of a value than <c>main</c> holds (C7). Replies <see cref="DigitsReply"/>.</summary>
public sealed record DigitsRequest(long Id, string Value, string Format) : Request;

/// <summary>A value's text again in another notation (C6). Replies <see cref="ResultReply"/>.</summary>
public sealed record ReformatRequest(long Id, string Value, NotationStyle Notation, int Digits) : Request;

/// <summary>A held value's lossless text, whatever its size. Replies <see cref="ValueReply"/>.</summary>
public sealed record FetchRequest(long Id, long HeldId) : Request;

/// <summary>The function table. Replies <see cref="FunctionsReply"/>.</summary>
public sealed record FunctionsRequest(long Id) : Request;

/// <summary>
/// Stop the evaluation with this id, between nodes: it replies <see cref="ErrorReply"/> with kind Stopped. One still
/// queued replies so without starting. Stop itself has no reply; an id that isn't queued or running is ignored.
/// </summary>
public sealed record StopRequest(long Id) : Request;

/// <summary>Are you there? Answered at once, even during a long evaluation. Replies <see cref="PongReply"/>.</summary>
public sealed record PingRequest(long Id = 0) : Request;

/// <summary>An <c>Ans</c>: its lossless text, or the id of a value the worker holds.</summary>
public sealed record AnsValue(string? Lossless = null, long? Held = null);

public enum NotationStyle { Auto, Scientific, Engineering, Fixed, Aligned }

/// <summary>How a result's text is laid out: Engine's TextOptions, on the wire.</summary>
public sealed record TextStyle(NotationStyle Notation = NotationStyle.Auto, string? Separator = null, bool GroupFraction = false);

/// <summary>A reply from the worker.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResultReply), "result")]
[JsonDerivedType(typeof(DigitsReply), "digits")]
[JsonDerivedType(typeof(ValueReply), "value")]
[JsonDerivedType(typeof(ErrorReply), "error")]
[JsonDerivedType(typeof(ProgressReply), "progress")]
[JsonDerivedType(typeof(FunctionsReply), "functions")]
[JsonDerivedType(typeof(PongReply), "pong")]
public abstract record Reply;

/// <summary>
/// A result: <c>ResultText</c>'s fields; the flags raised, the milliseconds taken and any warnings; the value itself as
/// <paramref name="Lossless"/> text, or, past <see cref="Wire.LosslessLimit"/>, <paramref name="Held"/> by the worker under
/// this reply's id; and the understood form when implicit multiplication was involved.
/// </summary>
/// <remarks>A null is left out of the line, so the nullable fields come last, with defaults, which makes them optional.</remarks>
public sealed record ResultReply(long Id, string Short, string Main, string Kind, string Caption, int Digits, bool Complete,
                                 IReadOnlyList<string> Flags, double Ms, IReadOnlyList<string> Warnings,
                                 string? Lossless = null, long? Held = null, string? Understood = null) : Reply;

public sealed record DigitsReply(long Id, string Text) : Reply;

/// <summary>A held value's lossless text, answering <see cref="FetchRequest"/>: exempt from the size limit.</summary>
public sealed record ValueReply(long Id, string Lossless) : Reply;

/// <summary>
/// Something went wrong. <paramref name="Kind"/> is one of Calc.Syntax's ErrorKind names, or one of the worker's own:
/// <see cref="ErrorKinds.BadRequest"/> for a line it can't use, and <see cref="ErrorKinds.ValueGone"/> for a held value it
/// no longer has, whose line the Client replays. <paramref name="From"/> and <paramref name="To"/> are the span in the
/// source, when there is one. <paramref name="Id"/> is null only for a line whose id couldn't be read.
/// </summary>
public sealed record ErrorReply([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Id, string Kind, string Message,
                                int? From = null, int? To = null) : Reply;

/// <summary>How far a long evaluation has got (B12, C5): <paramref name="Done"/> from 0 to 1, and the seconds expected in all.</summary>
public sealed record ProgressReply(long Id, double Done, double? Estimate = null) : Reply;

public sealed record FunctionsReply(long Id, IReadOnlyList<FunctionInfo> List) : Reply;

/// <summary>A function's name, how many arguments it takes, and the session it waits on, if any.</summary>
public sealed record FunctionInfo(string Name, int MinArguments, int MaxArguments, string? WaitingFor = null);

/// <summary>
/// The worker's answer to <see cref="PingRequest"/>: the wire version, and the memory it may use, which is the heap cap
/// the Client set (B10), or all there is.
/// </summary>
public sealed record PongReply(long Id, string Version, long? Memory = null) : Reply;

/// <summary>The worker's own error kinds, and the Client's, beside Calc.Syntax's.</summary>
public static class ErrorKinds
{
    /// <summary>The worker couldn't use the line: it isn't a request, or its context or Ans can't be used.</summary>
    public const string BadRequest = "BadRequest";

    /// <summary>The worker no longer holds that value: replay its line.</summary>
    public const string ValueGone = "ValueGone";

    /// <summary>The Client's: the worker ended by itself, or couldn't start. The next request starts another.</summary>
    public const string WorkerDied = "WorkerDied";

    /// <summary>The Client's: too little memory is free to start a worker safely (B10's floor).</summary>
    public const string MemoryShort = "MemoryShort";
}

/// <summary>Messages to and from their one-line JSON text.</summary>
public static class Messages
{
    // The source-generated settings, with relaxed escaping so ' · √ and π stay readable: the text never goes into HTML.
    private static readonly JsonSerializerOptions Options =
        new(WireJson.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonTypeInfo<Request> Requests = (JsonTypeInfo<Request>)Options.GetTypeInfo(typeof(Request));
    private static readonly JsonTypeInfo<Reply> Replies = (JsonTypeInfo<Reply>)Options.GetTypeInfo(typeof(Reply));

    public static string Write(Request request) => JsonSerializer.Serialize(request, Requests);

    public static string Write(Reply reply) => JsonSerializer.Serialize(reply, Replies);

    /// <exception cref="JsonException">It isn't a request.</exception>
    public static Request ReadRequest(string line) =>
        JsonSerializer.Deserialize(line, Requests) ?? throw new JsonException("A request can't be null.");

    /// <exception cref="JsonException">It isn't a reply.</exception>
    public static Reply ReadReply(string line) =>
        JsonSerializer.Deserialize(line, Replies) ?? throw new JsonException("A reply can't be null.");
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    AllowOutOfOrderMetadataProperties = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Request))]
[JsonSerializable(typeof(Reply))]
internal sealed partial class WireJson : JsonSerializerContext;
