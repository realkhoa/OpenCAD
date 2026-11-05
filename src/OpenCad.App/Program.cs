namespace OpenCad.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Chế độ tự kiểm tra giao diện: chạy không cần chuột, in kết quả ra console.
        if (args.Length > 0 && args[0].Equals("--uitest", StringComparison.OrdinalIgnoreCase))
            return UiSelfTest.Run();

        // Một lỗi lẻ trong thao tác chuột không được phép làm mất bản vẽ của người dùng.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) HandleFatal(ex);
        };

        Application.Run(new MainForm(args.FirstOrDefault()));
        return 0;
    }

    /// <summary>Ghi log lỗi và báo cho người dùng, nhưng KHÔNG thoát ứng dụng.</summary>
    private static void HandleFatal(Exception exception)
    {
        var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception.GetType().Name}: {exception.Message}\r\n"
                      + exception.StackTrace + "\r\n\r\n";

        var logPath = Path.Combine(Path.GetTempPath(), "opencad-crash.log");

        try { File.AppendAllText(logPath, message); }
        catch { }

        try
        {
            MessageBox.Show(
                "Đã xảy ra lỗi nhưng bản vẽ vẫn được giữ nguyên:" + Environment.NewLine + Environment.NewLine
                + exception.Message + Environment.NewLine + Environment.NewLine
                + "Chi tiết đã ghi vào:" + Environment.NewLine + logPath,
                "OpenCAD — lỗi không mong đợi",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
    }
}
