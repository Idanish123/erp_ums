using Microsoft.Data.SqlClient;

namespace University_Management_System_2;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        IUniversityStore store;
        try
        {
            store = args.Contains("--demo", StringComparer.OrdinalIgnoreCase)
                ? new DemoUniversityStore()
                : new SqlUniversityStore(DatabaseLocation.ConnectionString());
        }
        catch (Exception)
        {
            MessageBox.Show("The database workspace could not be prepared. Check that your local application data folder is writable.",
                "University Management", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        while (true)
        {
            UserSession session;
            if (store.IsDemo)
                session = new UserSession("Demo administrator", UserRole.Administrator);
            else
            {
                using var signIn = new SignInForm(store);
                if (signIn.ShowDialog() != DialogResult.OK || signIn.Session is null) return;
                session = signIn.Session;
            }
            using var shell = new ShellForm(store, session);
            Application.Run(shell);
            if (!shell.LogoutRequested || store.IsDemo) return;
        }
    }
}

internal static class DatabaseLocation
{
    internal static string ConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("UMS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniversityManagement", "data");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "UniversityDataBase.mdf");
        if (!File.Exists(database))
        {
            var seed = Path.Combine(AppContext.BaseDirectory, "seed");
            if (!File.Exists(Path.Combine(seed, "UniversityDataBase.mdf")))
                throw new FileNotFoundException("The database seed was not included in this installation.");
            var logPath = Path.Combine(directory, "UniversityDataBase_log.ldf");
            if (File.Exists(logPath))
                throw new IOException("An existing database log requires recovery before copying the seed.");
            File.Copy(Path.Combine(seed, "UniversityDataBase_log.ldf"), logPath, false);
            try { File.Copy(Path.Combine(seed, "UniversityDataBase.mdf"), database, false); }
            catch { File.Delete(logPath); throw; }
        }
        return new SqlConnectionStringBuilder
        {
            DataSource = @"(LocalDB)\MSSQLLocalDB", AttachDBFilename = database,
            IntegratedSecurity = true, Encrypt = SqlConnectionEncryptOption.Optional,
            ConnectTimeout = 10
        }.ConnectionString;
    }
}
