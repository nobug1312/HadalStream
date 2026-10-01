using System.Text.Json;
using HadalStream.Application.Abstractions;
using HadalStream.Application.Conversions;
using HadalStream.Application.Downloads;
using HadalStream.Application.Links;
using HadalStream.Application.Settings;
using HadalStream.Domain.Common;
using HadalStream.Domain.Settings;
using HadalStream.Infrastructure.Persistence;
using MediatR;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace HadalStream.Desktop;

// Web bridge: the UI posts {id, method, params}; the host answers {id, result} or {id, error} and pushes {event, data}.
internal sealed partial class MainWindow
{
    private sealed record Request(int Id, string Method, JsonElement Params);

    private sealed record Reply(object? Result, string? Error)
    {
        public static Reply Ok(object? result) => new(result, null);
    }

    private async void OnWebMessage(object? _, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrusted(e.Source)) return;
        int? id = null;
        try
        {
            var request = JsonSerializer.Deserialize<Request>(e.WebMessageAsJson, JsonFile.Options)!;
            id = request.Id;
            // Dropped files arrive as WebView2 file objects carrying real paths; page script can't forge them.
            var dropped = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToArray() ?? [];
            var reply = await HandleAsync(request.Method, request.Params, dropped);
            Post(reply.Error is { } error ? new { id, error } : new { id, result = reply.Result });
        }
        catch (Exception ex)
        {
            // Handler failures are already logged by the MediatR pipeline; the rest is malformed input.
            Post(new { id, error = ex.Message });
        }
    }

    private async Task<Reply> HandleAsync(string method, JsonElement args, string[] dropped) => method switch
    {
        "resolve" => await Send(new ResolveLinks(Text(args, "input"))),
        "jobs.list" => await Send(new ListDownloads()),
        "jobs.add" => await Send(new AddDownloads(args.Deserialize<NewDownload[]>(JsonFile.Options) ?? [])),
        "jobs.pause" => await Send(new PauseDownload(Text(args, "id"))),
        "jobs.resume" => await Send(new ResumeDownload(Text(args, "id"))),
        "jobs.cancel" => await Send(new CancelDownload(Text(args, "id"))),
        "jobs.remove" => await Send(new RemoveDownload(Text(args, "id"))),
        "jobs.open" => await Send(new OpenDownload(Text(args, "id"))),
        "jobs.reveal" => await Send(new RevealDownload(Text(args, "id"))),
        "jobs.revealLog" => await Send(new RevealDownloadLog(Text(args, "id"))),
        "settings.get" => await Send(new GetSettings()),
        "settings.save" => await SaveSettingsAsync(args),
        "settings.pickFolder" => PickFolder(),
        "convert.list" => await Send(new ListConversions()),
        "convert.pick" => PickVideos() is { } files ? await Send(new AddConversions(files)) : Reply.Ok(Array.Empty<object>()),
        "convert.drop" => await Send(new AddConversions(dropped)),
        "convert.cancel" => await Send(new CancelConversion(Text(args, "id"))),
        "convert.retry" => await Send(new RetryConversion(Text(args, "id"))),
        "convert.remove" => await Send(new RemoveConversion(Text(args, "id"))),
        "convert.open" => await Send(new OpenConversion(Text(args, "id"))),
        "convert.reveal" => await Send(new RevealConversion(Text(args, "id"))),
        "app.info" => Reply.Ok(new { version = typeof(Program).Assembly.GetName().Version?.ToString(3) }),
        _ => new Reply(null, "Unknown command."),
    };

    private async Task<Reply> Send(IRequest<Result> request) => new(null, (await mediator.Send(request)).Error?.Message);

    private async Task<Reply> Send<T>(IRequest<Result<T>> request)
    {
        var result = await mediator.Send(request);
        return new(result.IsSuccess ? (object?)result.Value : null, result.Error?.Message);
    }

    private async Task<Reply> SaveSettingsAsync(JsonElement args)
    {
        var reply = await Send(new SaveSettings(args.Deserialize<AppSettings>(JsonFile.Options) ?? throw new JsonException("Invalid settings.")));
        ApplyTheme();
        return reply;
    }

    private Reply PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose download folder", InitialDirectory = settings.Current.DownloadDir };
        return Reply.Ok(dialog.ShowDialog(this) == true ? dialog.FolderName : null);
    }

    private string[]? PickVideos()
    {
        var dialog = new OpenFileDialog { Title = "Choose videos", Filter = "Videos (*.flv;*.mov;*.mkv)|*.flv;*.mov;*.mkv", Multiselect = true };
        return dialog.ShowDialog(this) == true ? dialog.FileNames : null;
    }

    private static string Text(JsonElement args, string name) => args.GetProperty(name).GetString() ?? "";

    private void Post(object message)
    {
        if (closed) return;
        web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonFile.Options));
    }
}
