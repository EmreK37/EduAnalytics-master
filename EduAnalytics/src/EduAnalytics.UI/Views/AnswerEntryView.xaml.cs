using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EduAnalytics.Core.Enums;
using EduAnalytics.UI.Services;
using EduAnalytics.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EduAnalytics.UI.Views;

public partial class AnswerEntryView : UserControl
{
    public AnswerEntryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AnswerEntryViewModel old)
        {
            old.QuestionsLoaded -= BuildColumns;
            old.OpenRubricRequested -= OnOpenRubricRequested;
        }

        if (e.NewValue is AnswerEntryViewModel vm)
        {
            vm.QuestionsLoaded += BuildColumns;
            vm.OpenRubricRequested += OnOpenRubricRequested;
            vm.ConfirmSave = message => AppMessageBox.Show(
                message, "Cevapları Kaydet",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (vm.Questions.Any())
                BuildColumns();
        }
    }

    private async void OnOpenRubricRequested(int questionId, int studentId)
    {
        if (DataContext is not AnswerEntryViewModel vm) return;

        var question = vm.Questions.FirstOrDefault(q => q.QuestionId == questionId);
        var student = vm.StudentRows.FirstOrDefault(s => s.StudentId == studentId);
        if (question == null || student == null) return;

        var rubricVm = App.Services.GetRequiredService<RubricGradeDialogViewModel>();
        await rubricVm.LoadAsync(
            vm.ExamId, questionId, studentId,
            $"Soru {question.QuestionNumber}: {question.QuestionTextPreview} ({question.MaxPoints} puan)",
            $"{student.StudentNumber} — {student.FullName}");

        var dialog = new RubricGradeDialog { Owner = Window.GetWindow(this) };
        dialog.Bind(rubricVm);

        if (dialog.ShowDialog() == true)
        {
            // Kaydedilen toplam puanı ilgili hücreye anında yansıt — listede de güncel görünsün.
            var cell = student.Cells.FirstOrDefault(c => c.QuestionId == questionId);
            if (cell != null)
                cell.Score = rubricVm.TotalScore;

            await vm.ReloadAsync();
        }
    }

    /// <summary>
    /// Sorular yüklendikten sonra DataGrid kolonlarını dinamik olarak oluşturur.
    /// Test sorusu → ComboBox (A/B/C/D/Boş). Klasik soru → TextBox (numerik).
    /// </summary>
    private void BuildColumns()
    {
        if (DataContext is not AnswerEntryViewModel vm) return;

        AnswersGrid.Columns.Clear();

        // Öğrenci bilgi sütunları (sabit / donmuş)
        AnswersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Numara",
            Binding = new Binding("StudentNumber"),
            IsReadOnly = true,
            Width = new DataGridLength(115),
            CellStyle = MakeCellStyle(bold: true, backgroundResourceKey: "CardSubtleBgBrush")
        });

        AnswersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Öğrenci",
            Binding = new Binding("FullName"),
            IsReadOnly = true,
            Width = new DataGridLength(200),
            CellStyle = MakeCellStyle(bold: true, backgroundResourceKey: "CardSubtleBgBrush")
        });

        // Kitapçık sütunu (sınavda 2+ kitapçık varsa kullanışlı; tek kitapçıkta da görünür)
        if (vm.AvailableBookletCodes.Count > 0)
        {
            var bookletCol = new DataGridComboBoxColumn
            {
                Header = "Kitapçık",
                ItemsSource = vm.AvailableBookletCodes,
                SelectedItemBinding = new Binding("BookletCode")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(95),
                EditingElementStyle = MakeEditingComboStyle(null),
                CellStyle = MakeCellStyle(bold: true, backgroundResourceKey: "WarningSubtleBgBrush")
            };
            AnswersGrid.Columns.Add(bookletCol);
        }

        // Boş kâğıt: sınava girip hiçbir soruyu cevaplamayan öğrenci. İşaretlenmezse boş satır
        // "sınava girmedi" sayılır ve analize girmez.
        var blankPaperHeaderStyle = MakeQuestionHeaderStyle();
        blankPaperHeaderStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
            "Öğrenci sınava girdi ama hiçbir soruyu cevaplamadı. İşaretlenirse 0 puanla analize girer; " +
            "işaretlenmezse boş satır \"sınava girmedi\" sayılır."));
        AnswersGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Boş\nkâğıt",
            Binding = new Binding(nameof(StudentRowViewModel.IsBlankPaper))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(64),
            CellStyle = MakeCellStyle(),
            HeaderStyle = blankPaperHeaderStyle
        });

        // Soru sütunları
        var headerStyle = MakeQuestionHeaderStyle();
        var optionTemplate = MakeOptionLetterTemplate();
        var optionDisplayStyle = MakeDisplayComboStyle(optionTemplate);
        var optionEditStyle = MakeEditingComboStyle(optionTemplate);
        for (int i = 0; i < vm.Questions.Count; i++)
        {
            var q = vm.Questions[i];
            var header = q.Type == QuestionType.MultipleChoice
                ? $"S{q.QuestionNumber}\nTest · {q.MaxPoints:0.##} p"
                : $"S{q.QuestionNumber}\nKlasik · {q.MaxPoints:0.##} p";

            DataGridColumn column;

            if (q.Type == QuestionType.MultipleChoice)
            {
                var comboCol = new DataGridComboBoxColumn
                {
                    Header = header,
                    ItemsSource = vm.OptionLetters,
                    SelectedItemBinding = new Binding($"Cells[{i}].SelectedOption")
                    {
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    },
                    Width = new DataGridLength(96),
                    CellStyle = MakeCellStyle(),
                    HeaderStyle = headerStyle,
                    ElementStyle = optionDisplayStyle,
                    EditingElementStyle = optionEditStyle
                };
                column = comboCol;
            }
            else
            {
                // Klasik: TextBox + not detay butonu (rubric kriter puanlaması)
                var template = new DataTemplate();
                var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
                stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
                stackFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                stackFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

                var tbFactory = new FrameworkElementFactory(typeof(TextBox));
                tbFactory.SetBinding(TextBox.TextProperty, new Binding($"Cells[{i}].Score")
                {
                    StringFormat = "N1",
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    TargetNullValue = string.Empty
                });
                tbFactory.SetValue(FrameworkElement.WidthProperty, 60.0);
                tbFactory.SetValue(FrameworkElement.HeightProperty, 26.0);
                tbFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                tbFactory.SetValue(Control.PaddingProperty, new Thickness(4, 2, 4, 2));
                tbFactory.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
                tbFactory.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Center);
                tbFactory.SetResourceReference(Control.BackgroundProperty, "WarningSubtleBgBrush");

                var btnFactory = new FrameworkElementFactory(typeof(Button));
                btnFactory.SetValue(Button.ContentProperty, "Not");
                btnFactory.SetValue(Button.ToolTipProperty, "Detaylı (kriter-bazlı) puanla");
                btnFactory.SetValue(FrameworkElement.WidthProperty, 38.0);
                btnFactory.SetValue(FrameworkElement.HeightProperty, 26.0);
                btnFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                btnFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));
                btnFactory.SetValue(Control.PaddingProperty, new Thickness(0));
                btnFactory.SetValue(Control.BorderThicknessProperty, new Thickness(1));
                btnFactory.SetValue(Control.FontSizeProperty, 11.0);
                btnFactory.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
                btnFactory.SetBinding(Button.CommandProperty, new Binding("DataContext.OpenRubricDialogCommand")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                    {
                        AncestorType = typeof(UserControl)
                    }
                });
                btnFactory.SetBinding(Button.CommandParameterProperty, new Binding($"Cells[{i}]"));

                stackFactory.AppendChild(tbFactory);
                stackFactory.AppendChild(btnFactory);
                template.VisualTree = stackFactory;

                column = new DataGridTemplateColumn
                {
                    Header = header,
                    CellTemplate = template,
                    Width = new DataGridLength(170),
                    CellStyle = MakeCellStyle(),
                    HeaderStyle = headerStyle
                };
            }

            AnswersGrid.Columns.Add(column);
        }
    }

    /// <summary>
    /// Hücre stili. Uygulamanın varsayılan DataGridCell stilinden türer; böylece seçili hücrede
    /// sistem vurgu rengi (beyaz yazı) yerine tema rengi kullanılır (açık/koyu temada okunur kalır).
    /// </summary>
    private static Style MakeCellStyle(bool bold = false, string? backgroundResourceKey = null)
    {
        var baseStyle = Application.Current.TryFindResource(typeof(DataGridCell)) as Style;
        var style = new Style(typeof(DataGridCell), baseStyle);
        if (bold)
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        if (backgroundResourceKey != null)
            style.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(backgroundResourceKey)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));

        // Seçili/odaklı hücrede de yazı rengi tema rengi kalsın, arka plan değişmesin.
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimaryBrush")));
        if (backgroundResourceKey != null)
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(backgroundResourceKey)));
        style.Triggers.Add(selected);
        return style;
    }

    /// <summary>Şık harfini gösterir; boş cevap "Empty" yerine "Boş" olarak görünür.</summary>
    private static DataTemplate MakeOptionLetterTemplate()
    {
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new Converters.OptionLetterLabelConverter() });
        template.VisualTree = text;
        return template;
    }

    /// <summary>Hücre düzenleme modunda değilken görünen (salt metin) ComboBox stili.</summary>
    private static Style MakeDisplayComboStyle(DataTemplate itemTemplate)
    {
        var style = new Style(typeof(ComboBox), DataGridComboBoxColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(ItemsControl.ItemTemplateProperty, itemTemplate));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        return style;
    }

    /// <summary>
    /// Hücre düzenleme modundaki ComboBox stili. Uygulamanın temalı ComboBox stilinden türer;
    /// aksi halde sistemin açık renkli kutusu koyu temada okunmaz kalır.
    /// </summary>
    private static Style MakeEditingComboStyle(DataTemplate? itemTemplate)
    {
        var baseStyle = Application.Current.TryFindResource(typeof(ComboBox)) as Style
                        ?? DataGridComboBoxColumn.DefaultEditingElementStyle;
        var style = new Style(typeof(ComboBox), baseStyle);
        if (itemTemplate != null)
            style.Setters.Add(new Setter(ItemsControl.ItemTemplateProperty, itemTemplate));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 6, 4)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32.0));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    /// <summary>Soru sütunu başlığı: ortalı, iki satırlı metin sığacak şekilde.</summary>
    private static Style MakeQuestionHeaderStyle()
    {
        var baseStyle = Application.Current.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style;
        var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), baseStyle);
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        return style;
    }

    private static Style MakeTextElementStyle(string? backgroundResourceKey = null)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 4, 8, 4)));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        if (backgroundResourceKey != null)
            style.Setters.Add(new Setter(TextBlock.BackgroundProperty,
                new DynamicResourceExtension(backgroundResourceKey)));
        return style;
    }

    private static Style MakeEditingStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Center));
        style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(6, 2, 6, 2)));
        return style;
    }
}
