using System.Windows;
using System.Windows.Controls;
using EduAnalytics.UI.Services;
using EduAnalytics.UI.ViewModels;

namespace EduAnalytics.UI.Views;

public partial class OpticalReadingView : UserControl
{
    public OpticalReadingView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            // Kaydetmeden önce özet gösterip kullanıcı onayı al.
            if (e.NewValue is OpticalReadingViewModel vm)
            {
                vm.ConfirmSave = summary => AppMessageBox.Show(
                    summary, "Optik Okuma Sonuçlarını Kaydet",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }
        };
    }
}
