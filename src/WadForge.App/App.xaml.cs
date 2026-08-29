using System.IO;
using System.Windows;

namespace WadForge.App;

public partial class App : Application
{
    private void Application_Startup(
        object sender,
        StartupEventArgs e)
    {
        if (e.Args.Length >=
                2 &&
            string.Equals(
                e.Args[0],
                "--edit-wad",
                StringComparison.OrdinalIgnoreCase) &&
            File.Exists(
                e.Args[1]))
        {
            TextureEditorWindow editor =
                new();

            MainWindow =
                editor;

            editor.Show();

            editor.OpenWad(
                e.Args[1]);

            return;
        }

        WadForge.App.MainWindow mainWindow =
            new();

        MainWindow =
            mainWindow;

        mainWindow.Show();
    }
}