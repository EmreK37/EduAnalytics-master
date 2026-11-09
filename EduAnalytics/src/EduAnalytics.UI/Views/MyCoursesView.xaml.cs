using System.Windows.Controls;

namespace EduAnalytics.UI.Views;

public partial class MyCoursesView : UserControl
{
    public MyCoursesView()
    {
        // DataContext, MainWindow'daki DataTemplate tarafından verilen MainViewModel.CurrentView
        // örneğidir; veriler diğer ekranlarda olduğu gibi gezinme sırasında yüklenir.
        InitializeComponent();
    }
}
