using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json;

namespace QuickShell.Services;

internal sealed partial class DeferredLoadingContent : FormContent
{
    public DeferredLoadingContent(string title, string subtitle)
    {
        TemplateJson = $$"""
            {
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {
                  "type": "TextBlock",
                  "text": {{JsonSerializer.Serialize(title, QuickShellJsonContext.Default.String)}},
                  "weight": "Bolder",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": {{JsonSerializer.Serialize(subtitle, QuickShellJsonContext.Default.String)}},
                  "isSubtle": true,
                  "wrap": true
                }
              ]
            }
            """;
    }
}
