using System.Text.Json;

namespace Ubp.Web.Services;

public sealed class Translator
{
    private readonly Dictionary<string, Dictionary<string, string>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);

    public Translator(IWebHostEnvironment environment, ILogger<Translator> logger)
    {
        foreach (var language in new[] { "ar", "en" })
        {
            var path = Path.Combine(environment.ContentRootPath, "Resources", $"{language}.json");
            if (!File.Exists(path))
            {
                logger.LogWarning("Translation file missing: {Path}", path);
                _dictionaries[language] = new Dictionary<string, string>();
                continue;
            }
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                      ?? new Dictionary<string, string>();
            _dictionaries[language] = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string Get(string language, string key)
    {
        if (_dictionaries.TryGetValue(language, out var map) && map.TryGetValue(key, out var text))
            return text;
        if (_dictionaries.TryGetValue("en", out var english) && english.TryGetValue(key, out var fallback))
            return fallback;
        return key;
    }
}
