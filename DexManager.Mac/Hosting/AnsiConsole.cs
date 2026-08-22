namespace DexManager.Mac.Hosting;

public static class AnsiConsole
{
    public const string Reset = "\u001b[0m";
    public const string Bold = "\u001b[1m";
    public const string Dim = "\u001b[2m";
    public const string Italic = "\u001b[3m";
    public const string Underline = "\u001b[4m";

    public const string Black = "\u001b[30m";
    public const string Red = "\u001b[31m";
    public const string Green = "\u001b[32m";
    public const string Yellow = "\u001b[33m";
    public const string Blue = "\u001b[34m";
    public const string Magenta = "\u001b[35m";
    public const string Cyan = "\u001b[36m";
    public const string White = "\u001b[37m";

    public const string BrightBlack = "\u001b[90m";
    public const string BrightRed = "\u001b[91m";
    public const string BrightGreen = "\u001b[92m";
    public const string BrightYellow = "\u001b[93m";
    public const string BrightBlue = "\u001b[94m";
    public const string BrightMagenta = "\u001b[95m";
    public const string BrightCyan = "\u001b[96m";
    public const string BrightWhite = "\u001b[97m";

    public const string BgBlue = "\u001b[44m";
    public const string BgDarkGray = "\u001b[100m";

    public static void Clear()
    {
        try
        {
            Console.Clear();
        }
        catch
        {
            Console.Write("\u001b[2J\u001b[H");
        }
    }

    public static void WriteLine(string text = "") => Console.WriteLine(text + Reset);

    public static void Write(string text) => Console.Write(text + Reset);

    public static void Success(string message) => Console.WriteLine($"{BrightGreen}✓ {message}{Reset}");

    public static void Info(string message) => Console.WriteLine($"{BrightCyan}ℹ {message}{Reset}");

    public static void Warning(string message) => Console.WriteLine($"{BrightYellow}⚠ {message}{Reset}");

    public static void Error(string message) => Console.WriteLine($"{BrightRed}✖ {message}{Reset}");

    public static void Header(string title)
    {
        var line = new string('═', Math.Max(50, title.Length + 4));
        Console.WriteLine($"{BrightCyan}{line}");
        Console.WriteLine($"  {Bold}{BrightWhite}{title}");
        Console.WriteLine($"{BrightCyan}{line}{Reset}");
    }

    public static void SubHeader(string title) => Console.WriteLine($"\n{Bold}{BrightMagenta}▶ {title}{Reset}");

    public static void KeyValue(string key, string value, string keyColor = BrightBlack, string valColor = BrightWhite)
    {
        Console.WriteLine($"  {keyColor}{key,-22}:{Reset} {valColor}{value}{Reset}");
    }

    public static void Badge(string label, string color) => Console.Write($" {color}[{label}]{Reset}");
}
