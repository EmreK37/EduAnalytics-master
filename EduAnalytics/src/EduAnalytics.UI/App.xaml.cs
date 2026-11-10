using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Net.Http;
using System.IO;
using EduAnalytics.Business.Services.Implementations;
using EduAnalytics.Business.Services.Interfaces;
using EduAnalytics.DataAccess.Context;
using EduAnalytics.DataAccess.Seed;
using EduAnalytics.Ocr.Reading;
using EduAnalytics.UI.Services;
using EduAnalytics.UI.Services.AIAssistant;
using EduAnalytics.UI.ViewModels;
using EduAnalytics.UI.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EduAnalytics.UI;

public partial class App : Application
{
    public static bool IsAdminMode { get; set; }

    public static IServiceProvider Services { get; private set; } = null!;
    public static IConfiguration Configuration { get; private set; } = null!;

    static App()
    {
        // Bağlamalar (ör. {0:N2}) sayıları ve tarihleri kod içindeki biçimlendirmeyle aynı bölge ayarıyla yazsın.
        // Ayar yapılmazsa WPF her zaman en-US kullanır; aynı ekranda hem "0,383" hem "0.50" görünür.
        // Language miras alınan bir özellik; Run gibi içerik öğeleri dili üst öğeden alır.
        var language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));

        // Kutuya yazılan metin, bağlama kaynağı güncellenirken yeniden biçimlenip yazanın elinden alınmasın
        // ("7," yazarken virgül kaybolmasın, "7,05" yazarken "7,0" 7'ye dönüşmesin). Kaynak değişince
        // (yükleme, temizleme) kutu yine güncellenir.
        FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty = false;

        // Örtük (implicit) Window stili türetilmiş pencerelere uygulanmadığı için yazı tipini
        // her pencere yüklenirken köke yazıyoruz; FontFamily ağaçta miras alındığından tüm
        // içerik (yan menü, tablolar, diyaloglar) aynı fontu kullanır.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded));
    }

    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;

        ApplyTitleBarTheme(window);

        if (window.ReadLocalValue(System.Windows.Controls.Control.FontFamilyProperty) != DependencyProperty.UnsetValue) return;

        if (Current?.TryFindResource("AppFontFamily") is System.Windows.Media.FontFamily font)
            window.FontFamily = font;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Standart başlık çubuklu diyaloglarda (Öğrenci Profili, Soruyu Düzenle vb.) Windows başlık
    /// çubuğunu uygulamanın o anki temasına uydurur; aksi halde koyu temada beyaz çubuk kalır.
    /// </summary>
    private static void ApplyTitleBarTheme(Window window)
    {
        try
        {
            if (Current?.TryFindResource("PageBgBrush") is not System.Windows.Media.SolidColorBrush pageBg) return;

            var c = pageBg.Color;
            var isDark = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            var value = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch
        {
            // Eski Windows sürümlerinde desteklenmeyebilir; görsel bir iyileştirme olduğu için yok sayılır.
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 1. Configuration Setup
        var basePath = File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            ? AppContext.BaseDirectory
            : Directory.GetCurrentDirectory();

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        Configuration = builder.Build();

        // 2. Serilog Setup
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(Configuration)
            .CreateLogger();

        Log.Information("EduAnalytics Application starting up...");

        // 3. Global Exception Handling
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Fatal(args.Exception, "A fatal WPF UI exception occurred.");
            AppMessageBox.Show($"Beklenmedik bir hata oluştu:\n\n{args.Exception.Message}\n\nDetaylar log dosyasına kaydedildi.", "Sistem Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // Prevent application crash if possible
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "An unhandled AppDomain exception occurred.");
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Fatal(args.Exception, "An unobserved task exception occurred.");
            args.SetObserved();
        };

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            // Splash ve giriş ekranı sistemin o anki Light/Dark modunu takip eder.
            var themeService = Services.GetRequiredService<IThemeService>();
            themeService.ApplySystemForStartup();

            var splash = new SplashWindow();
            splash.Show();

            // Let WPF render the splash before blocking work starts
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            Exception? startupError = null;
            await Task.WhenAll(
                Task.Run(() =>
                {
                    try
                    {
                        SeedDatabaseWithRetry();
                    }
                    catch (Exception ex)
                    {
                        startupError = ex;
                    }
                }),
                Task.Delay(600)
            );

            if (startupError != null)
            {
                splash.Close();
                Log.Fatal(startupError, "Veritabanı başlatılamadı.");
                AppMessageBox.Show(
                    $"Veritabanı başlatılamadı:\n\n{startupError.GetType().Name}: {startupError.Message}\n\n{startupError.InnerException?.Message}\n\nLocalDB hala açılmıyorsa SQL Server LocalDB instance'ını yeniden başlatmayı deneyin.",
                    "Başlangıç Hatası",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            await splash.FadeOutAndCloseAsync();

            var loginWindow = Services.GetRequiredService<LoginWindow>();
            if (loginWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            // Ana uygulama açılırken kullanıcının uygulama içi tema tercihi varsa geri yüklenir.
            themeService.LoadAndApply();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Uygulama başlatılırken beklenmeyen bir hata oluştu.");
            AppMessageBox.Show(
                $"Uygulama başlatılırken beklenmeyen bir hata oluştu:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.InnerException?.Message}",
                "Başlangıç Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("EduAnalytics Application shutting down...");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void SeedDatabaseWithRetry()
    {
        const int maxAttempts = 4;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var ctx = Services.GetRequiredService<EduAnalyticsDbContext>();
                DbInitializer.Seed(ctx);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientLocalDbStartupError(ex))
            {
                lastError = ex;
                Log.Warning(ex, "Veritabanı başlatılamadı, tekrar deneniyor ({Attempt}/{MaxAttempts})", attempt, maxAttempts);
                Thread.Sleep(TimeSpan.FromMilliseconds(650 * attempt));
            }
        }

        throw lastError ?? new InvalidOperationException("Veritabanı başlatılamadı.");
    }

    private static bool IsTransientLocalDbStartupError(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message.ToLowerInvariant();
            if (message.Contains("localdb") ||
                message.Contains("error: 50") ||
                message.Contains("0x89c5010a") ||
                message.Contains("failed to start") ||
                message.Contains("network-related") ||
                message.Contains("instance-specific"))
            {
                return true;
            }
        }

        return false;
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // Serilog DI
        services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

        // DbContext - Transient: Her servis çağrısı kendi context'ini alır.
        services.AddDbContext<EduAnalyticsDbContext>(options =>
            options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")),
            ServiceLifetime.Transient);

        // Business Services - Transient
        // ── FAZ 1 servisleri ──
        services.AddTransient<IExamAnalysisService, ExamAnalysisService>();
        services.AddTransient<IDistractorAnalysisService, DistractorAnalysisService>();
        services.AddTransient<ITopicPerformanceService, TopicPerformanceService>();
        services.AddTransient<IStudentPerformanceService, StudentPerformanceService>();
        services.AddTransient<IExamCrudService, ExamCrudService>();
        services.AddTransient<IAnswerEntryService, AnswerEntryService>();
        services.AddTransient<ILearningOutcomePerformanceService, LearningOutcomePerformanceService>();

        // ── FAZ 2 servisleri (ASOS yenileme) ──
        services.AddTransient<IQuestionBankService, QuestionBankService>();
        services.AddTransient<ILearningOutcomeService, LearningOutcomeService>();
        services.AddTransient<IProgramOutcomeService, ProgramOutcomeService>();
        services.AddTransient<IExamBookletService, ExamBookletService>();
        services.AddTransient<IExamBalanceCheckService, ExamBalanceCheckService>();
        services.AddTransient<IExamCancellationService, ExamCancellationService>();
        services.AddTransient<IStudentService, StudentService>();
        services.AddTransient<IAcademicStructureService, AcademicStructureService>();
        services.AddTransient<IExamStatisticsService, ExamStatisticsService>();
        services.AddTransient<IItemAnalysisService, ItemAnalysisService>();
        services.AddTransient<IDataImportService, DataImportService>();
        services.AddTransient<IDataExportService, DataExportService>();
        services.AddTransient<IMyCoursesService, MyCoursesService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<MyCoursesViewModel>();
        services.AddTransient<AdminExportViewModel>();
        services.AddTransient<ExamCalendarViewModel>();
        services.AddTransient<ExamAnalysisViewModel>();
        services.AddTransient<ExamCreateViewModel>();
        services.AddTransient<AnswerEntryViewModel>();

        // FAZ 3 — Yeni ASOS ekranları
        services.AddTransient<QuestionBankViewModel>();
        services.AddTransient<LearningOutcomeManagementViewModel>();
        services.AddTransient<ProgramOutcomeManagementViewModel>();
        services.AddTransient<ProgramOutcomeReportViewModel>();
        services.AddTransient<ProgramOutcomeMappingViewModel>();
        services.AddTransient<StudentManagementViewModel>();
        services.AddTransient<AcademicStructureViewModel>();

        // FAZ 4 — İleri özellikler
        services.AddTransient<QuestionGroupEditorViewModel>();
        services.AddTransient<ExamFromBankViewModel>();
        services.AddTransient<ExamManagementViewModel>();
        services.AddTransient<QuestionEditDialogViewModel>();
        services.AddTransient<SingleQuestionCreateViewModel>();

        // FAZ 5 — Rubric (klasik soru kriter-bazlı puanlama)
        services.AddTransient<IRubricService, RubricService>();
        services.AddTransient<RubricGradeDialogViewModel>();

        // FAZ 6 — Optik okuma (PaddleOCR + OMR)
        services.AddSingleton<IOpticalFormReader, OpticalFormReader>();
        services.AddTransient<OpticalReadingViewModel>();

        // UI altyapı
        services.AddSingleton<IAppLogService, AppLogService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IUserProfileService, UserProfileService>();
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
        services.AddSingleton<IAISettingsService, AISettingsService>();
        services.AddSingleton<IAIProvider, ClaudeProvider>();
        services.AddSingleton<IAIProvider, OpenAIProvider>();
        services.AddSingleton<IAIProvider, GeminiProvider>();
        services.AddSingleton<IAIAssistantService, AIAssistantService>();

        // Windows
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<UserSettingsDialog>();
    }
}
