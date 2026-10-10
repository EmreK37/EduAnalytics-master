using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EduAnalytics.UI.ViewModels;

namespace EduAnalytics.UI.Views;

public partial class ExamManagementView : UserControl
{
    public ExamManagementView()
    {
        InitializeComponent();
    }

    private void PendingPoints_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        CommitPendingPoints(sender as TextBox);
        e.Handled = true;
        Keyboard.ClearFocus();
    }

    private void PendingPoints_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitPendingPoints(sender as TextBox);
    }

    private static void CommitPendingPoints(TextBox? textBox)
    {
        if (textBox?.DataContext is not PendingExamQuestion pending)
            return;

        if (TryParsePoint(textBox.Text, out var points) && points > 0)
            pending.Points = Math.Round(points, 2);

        // Geçersiz giriş olduysa eski değere dön; geçerliyse biçimli göster.
        textBox.Text = pending.Points.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static bool TryParsePoint(string? text, out decimal points)
    {
        var normalized = (text ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace(",", ".");

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out points);
    }
}
