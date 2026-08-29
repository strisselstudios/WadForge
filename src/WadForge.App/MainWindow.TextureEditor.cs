using System.Windows;

namespace WadForge.App;

public partial class MainWindow
{
    private void OpenTextureEditor_Click(
        object sender,
        RoutedEventArgs e)
    {
        TextureEditorWindow editor =
            new()
            {
                Owner =
                    this
            };

        editor.Show();
    }
}
