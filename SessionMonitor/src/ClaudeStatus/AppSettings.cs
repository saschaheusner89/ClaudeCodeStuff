using System.IO;
using System.Text.Json;
using ClaudeStatus.Shared;

namespace ClaudeStatus;

/// <summary>Window placement and options, stored in %LOCALAPPDATA%\ClaudeStatus\settings.json.</summary>
public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 720;
    public double Height { get; set; } = 420;
    public bool Topmost { get; set; } = true;

    private static string FilePath => Path.Combine(Paths.DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }
}
