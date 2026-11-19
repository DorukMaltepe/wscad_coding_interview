using System.IO;
using System.Windows;

namespace DesktopApp;

public partial class NewProjectDialog : Window
{
    public NewProjectDialog() => InitializeComponent();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = ProjectNameInput.Text.Trim();
        if (name.Length == 0)
            ValidationText.Text = "Enter a project name.";
        else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            ValidationText.Text = "The project name contains characters that are not allowed in file names.";
        else
            DialogResult = true;
    }
}
