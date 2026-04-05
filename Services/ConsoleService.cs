using System.Text;

namespace UserManagementSystem.Services;

public interface IConsoleService
{
    void PrintBanner();
    void PrintServerStarted(string baseUrl);
    void PrintInitSuccess(string username, string email, string password);
    void PrintError(string message);
    void PrintSuccess(string message);
    void PrintWarning(string message);
    void PrintInfo(string label, string value);
}

public class ConsoleService : IConsoleService
{
    private const string AppName = "UserManagementSystem";
    private const string Version = "v1.0.0";
    private const string Framework = "ASP.NET Core 8.0";

    public void PrintBanner()
    {
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine($"  {AppName} {Version}");
        Console.WriteLine($"  Framework: {Framework}");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }

    public void PrintServerStarted(string baseUrl)
    {
        var underline = new string('-', 70);
        
        Console.WriteLine($"  Server started successfully");
        Console.WriteLine($"  {underline}");
        Console.WriteLine($"  Swagger UI: {baseUrl}swagger");
        Console.WriteLine($"  API Docs:   {baseUrl}");
        Console.WriteLine($"  Health:     {baseUrl}health");
        Console.WriteLine($"  {underline}");
        Console.WriteLine();
    }

    public void PrintInitSuccess(string username, string email, string password)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  [IMPORTANT] Initial administrator account created");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
        Console.WriteLine($"  Username:  {username}");
        Console.WriteLine($"  Email:      {email}");
        Console.WriteLine($"  Password:   {password}");
        Console.WriteLine();
        Console.WriteLine("  Please login and change the password immediately.");
        Console.WriteLine("  Password has been saved to logs/init.log");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }

    public void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }

    public void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] {message}");
        Console.ResetColor();
    }

    public void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN] {message}");
        Console.ResetColor();
    }

    public void PrintInfo(string label, string value)
    {
        Console.WriteLine($"  {label}: {value}");
    }
}
