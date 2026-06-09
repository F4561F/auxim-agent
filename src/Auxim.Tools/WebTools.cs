using System.Net.Http.Headers;
using Auxim.Core.Tools;

namespace Auxim.Tools;

public static class WebTools
{
    public static void Register(ToolRegistry registry)
    {
        registry.Register(new ToolDefinition(
            "web.fetch",
            "web",
            "Fetches a URL and returns text content, capped to a maximum number of characters.",
            async (args, cancellationToken) =>
            {
                var url = FileTools.Required(args, "url");
                var maxChars = 12000;
                if (args.TryGetValue("maxChars", out var rawMax)
                    && int.TryParse(rawMax?.ToString(), out var parsedMax)
                    && parsedMax > 0)
                {
                    maxChars = Math.Min(parsedMax, 50000);
                }

                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(20),
                };
                http.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Auxim", "0.1"));

                var text = await http.GetStringAsync(url, cancellationToken);
                return text.Length <= maxChars ? text : text[..maxChars];
            })
        {
            ParametersSchema = FileTools.ObjectSchema(
                [
                    ("url", "string", "URL to fetch."),
                    ("maxChars", "integer", "Maximum characters to return, capped at 50000."),
                ],
                ["url"]),
        });
    }
}
