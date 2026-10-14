using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace ImageTextDetector;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static StreamWriter? _logWriter;

    public App()
    {
        // Redirecionar Console.WriteLine para arquivo de log
        try
        {
            string logPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                "application.log");
            
            _logWriter = new StreamWriter(logPath, append: true, System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };
            
            Console.SetOut(_logWriter);
            Console.SetError(_logWriter);
            
            Console.WriteLine($"\n========== Sessão iniciada em {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao configurar logging: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Console.WriteLine("========== Sessão finalizada ==========\n");
        _logWriter?.Dispose();
        base.OnExit(e);
    }
}

