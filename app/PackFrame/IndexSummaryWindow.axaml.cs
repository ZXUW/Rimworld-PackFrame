using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class IndexSummaryWindow : Window
{
    private readonly string _language;

    public IndexSummaryWindow()
    {
        InitializeComponent();
        _language = Localization.English;
    }

    public IndexSummaryWindow(string summary, string? language) : this()
    {
        _language = language ?? Localization.English;
        Title = OperationText.Choose(
            _language,
            "Index summary",
            "合集简介",
            "合集簡介",
            "一覧の概要"
        );
        HeadingText.Text = OperationText.Choose(
            _language,
            "Summary shown in the collection list",
            "合集列表中显示的简介",
            "合集列表中顯示的簡介",
            "コレクション一覧に表示する概要"
        );
        HintText.Text = OperationText.Choose(
            _language,
            "This text is written to index.json summary. It does not change the pack description.",
            "这段文字会写入 index.json 的 summary 字段，不会修改合集简介。",
            "這段文字會寫入 index.json 的 summary 欄位，不會修改合集簡介。",
            "このテキストは index.json の summary に書き込まれます。MOD パックの説明は変更されません。"
        );
        SummaryBox.Text = summary;
        ConfirmButton.Content = Localization.ConfirmButton(_language);
        CancelButton.Content = Localization.CancelButton(_language);
        Opened += (_, _) =>
        {
            SummaryBox.Focus();
            SummaryBox.CaretIndex = SummaryBox.Text?.Length ?? 0;
            SummaryBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    Confirm();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Close(null);
                    e.Handled = true;
                }
            };
        };
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm()
    {
        var summary = SummaryBox.Text?.Trim() ?? string.Empty;
        if (summary.Length == 0)
        {
            SummaryBox.Focus();
            return;
        }

        Close(summary);
    }
}
