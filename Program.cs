namespace SmithMatch;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length > 0 && File.Exists(args[0])) form.Shown += (_, _) => form.LoadDesign(args[0]);   // open a .smith file given on the command line
        // SmithMatch.exe design.smith --screenshots <folder>: saves the README images and exits
        int i = Array.IndexOf(args, "--screenshots");
        if (i >= 0 && i + 1 < args.Length) form.Shown += async (_, _) => { await Task.Delay(500); form.SaveScreenshots(args[i + 1]); form.Close(); };
        Application.Run(form);
    }
}
