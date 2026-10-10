using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using EduAnalytics.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduAnalytics.UI.Controls;

/// <summary>
/// Üç seçenekli (Sistem / Açık / Koyu) hap biçiminde tema anahtarı.
/// Aktif seçenek, yay (spring) benzeri bir animasyonla kayan bir halkayla vurgulanır.
/// </summary>
public partial class ThemeSwitcher : UserControl
{
    private const double OptionWidth = 32;
    private IThemeService? _themeService;
    private bool _firstSync = true;

    public ThemeSwitcher()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _themeService = App.Services.GetRequiredService<IThemeService>();
        _themeService.ThemeChanged += OnThemeChanged;

        SyncWithTheme(animate: false);

        // Açılışta 0.3 sn'lik yumuşak belirme
        Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // Başka bir yerden (ör. kullanıcı menüsü) tema değişirse anahtar da güncellensin.
        Dispatcher.BeginInvoke(() => SyncWithTheme(animate: true));
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (_themeService == null || sender is not RadioButton { Tag: string tag })
            return;

        if (Enum.TryParse<AppThemeMode>(tag, out var mode) && mode != _themeService.CurrentTheme)
            _themeService.Apply(mode);
        // Gösterge ThemeChanged olayıyla senkronlanır.
    }

    private void SyncWithTheme(bool animate)
    {
        if (_themeService == null) return;

        var mode = _themeService.CurrentTheme;
        var index = mode switch
        {
            AppThemeMode.System => 0,
            AppThemeMode.Light => 1,
            _ => 2
        };

        SystemOption.IsChecked = index == 0;
        LightOption.IsChecked = index == 1;
        DarkOption.IsChecked = index == 2;

        var target = index * OptionWidth;
        if (!animate || _firstSync)
        {
            IndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            IndicatorShift.X = target;
            _firstSync = false;
            return;
        }

        // Yay etkisi: hafif aşıp geri oturan BackEase (bounce ≈ 0.3, ~0.5 sn)
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
        };
        IndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }
}
