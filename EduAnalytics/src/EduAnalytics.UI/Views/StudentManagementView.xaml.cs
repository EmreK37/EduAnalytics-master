using System.Windows.Controls;
using System.Windows.Input;

namespace EduAnalytics.UI.Views;

public partial class StudentManagementView : UserControl
{
    public StudentManagementView()
    {
        InitializeComponent();
    }

    /// <summary>Öğrenci numarası alanına yalnızca rakam girilebilsin.</summary>
    private void StudentNumber_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(c => c is >= '0' and <= '9');
    }
}
