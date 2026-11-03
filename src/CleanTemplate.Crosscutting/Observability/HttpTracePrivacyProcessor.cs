using System.Diagnostics;
using OpenTelemetry;

namespace CleanTemplate.Crosscutting.Observability;

internal sealed class HttpTracePrivacyProcessor : BaseProcessor<Activity>
{
    private readonly ActivityListener _exceptionListener;

    public HttpTracePrivacyProcessor()
    {
        // .NET 10 HttpClient calls Activity.AddException independently of RecordException.
        _exceptionListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ExceptionRecorder = (Activity _, Exception _, ref TagList tags) =>
            {
                tags.Clear();
                tags.Add("exception.message", null);
                tags.Add("exception.stacktrace", null);
                tags.Add("exception.type", null);
            }
        };
        ActivitySource.AddActivityListener(_exceptionListener);
    }

    public override void OnEnd(Activity activity)
    {
        if (activity.Kind is not (ActivityKind.Server or ActivityKind.Client)) return;
        if (activity.GetTagItem("http.request.method") is null && activity.GetTagItem("http.method") is null) return;

        string method = activity.GetTagItem("http.request.method") as string
            ?? activity.GetTagItem("http.method") as string ?? "_OTHER";
        method = method is "GET" or "HEAD" or "POST" or "PUT" or "DELETE" or "CONNECT" or "OPTIONS" or "TRACE" or "PATCH" ? method : "_OTHER";
        string? route = activity.Kind == ActivityKind.Server ? activity.GetTagItem("http.route") as string : null;
        object? status = activity.GetTagItem("http.response.status_code") ?? activity.GetTagItem("http.status_code");

        foreach (var tag in activity.TagObjects.ToArray()) activity.SetTag(tag.Key, null);
        activity.SetTag("http.request.method", method);
        if (status is int code && code is >= 100 and <= 599)
        {
            activity.SetTag("http.response.status_code", code);
            if (activity.Status == ActivityStatusCode.Error) activity.SetTag("error.type", code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (route is not null) activity.SetTag("http.route", route);
        activity.DisplayName = activity.Kind == ActivityKind.Server ? $"{method} {route ?? "unmatched"}" : method;
        activity.SetStatus(activity.Status, null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _exceptionListener.Dispose();
        base.Dispose(disposing);
    }
}
