using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PackFrame;

public partial class OssSettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly string _language;

    public OssSettingsWindow()
    {
        InitializeComponent();
        _config = new AppConfig();
        _language = "zh-Hans";
    }

    public OssSettingsWindow(AppConfig config, string? language)
        : this()
    {
        _config = config;
        _language = string.IsNullOrWhiteSpace(language) ? "zh-Hans" : language;
        Configure();
    }

    private void Configure()
    {
        Title = OperationText.Choose(_language, "OSS settings", "OSS 配置", "OSS 設定", "OSS 設定");
        OssTitle.Text = OperationText.Choose(_language, "Aliyun OSS", "阿里云 OSS", "阿里雲 OSS", "Aliyun OSS");
        OssHint.Text = OperationText.Choose(
            _language,
            "Used only for the local mods archive. Metadata still goes to Gitee/GitHub.",
            "只用于保存本地模组压缩包，Gitee/GitHub 仍然只保存元数据。",
            "只用於儲存本地模組壓縮包，Gitee/GitHub 仍然只儲存中繼資料。",
            "ローカル MOD のアーカイブ専用です。メタデータは Gitee/GitHub に保存します。"
        );
        OssEnabledCheckBox.Content = OperationText.Choose(
            _language,
            "Enable OSS upload",
            "启用 OSS 上传",
            "啟用 OSS 上傳",
            "OSS アップロードを有効にする"
        );
        OssEnabledCheckBox.IsChecked = _config.OssEnabled;
        EndpointLabel.Text = OperationText.Choose(
            _language,
            "Endpoint (for example oss-cn-hangzhou.aliyuncs.com)",
            "Endpoint（例如 oss-cn-hangzhou.aliyuncs.com）",
            "Endpoint（例如 oss-cn-hangzhou.aliyuncs.com）",
            "Endpoint（例: oss-cn-hangzhou.aliyuncs.com）"
        );
        BucketLabel.Text = OperationText.Choose(_language, "Bucket", "Bucket", "Bucket", "Bucket");
        PrefixLabel.Text = OperationText.Choose(
            _language,
            "Object prefix (optional)",
            "对象前缀（可选）",
            "物件前綴（可選）",
            "オブジェクト接頭辞（任意）"
        );
        AccessKeyIdLabel.Text = OperationText.Choose(
            _language,
            "AccessKey ID",
            "AccessKey ID",
            "AccessKey ID",
            "AccessKey ID"
        );
        AccessKeySecretLabel.Text = OperationText.Choose(
            _language,
            "AccessKey Secret",
            "AccessKey Secret",
            "AccessKey Secret",
            "AccessKey Secret"
        );
        NoteText.Text = OperationText.Choose(
            _language,
            "Saved only in the local packframe.json. Use a RAM user with PutObject/GetObject permission; do not commit this file.",
            "只保存在本机 packframe.json。建议使用只有 PutObject/GetObject 权限的 RAM 子账号，不要把配置文件提交到仓库。",
            "只儲存在本機 packframe.json。建議使用只有 PutObject/GetObject 權限的 RAM 子帳號，不要把設定檔提交到倉庫。",
            "ローカルの packframe.json にのみ保存します。PutObject/GetObject 権限だけの RAM ユーザーを使い、リポジトリにはコミットしないでください。"
        );
        CancelButton.Content = OperationText.Choose(_language, "Cancel", "取消", "取消", "キャンセル");
        SaveButton.Content = OperationText.Choose(_language, "Save", "保存", "儲存", "保存");
        EndpointBox.Text = _config.OssEndpoint ?? string.Empty;
        BucketBox.Text = _config.OssBucket ?? string.Empty;
        PrefixBox.Text = _config.OssPrefix ?? string.Empty;
        AccessKeyIdBox.Text = _config.OssAccessKeyId ?? string.Empty;
        AccessKeySecretBox.Text = _config.OssAccessKeySecret ?? string.Empty;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var endpoint = EndpointBox.Text?.Trim();
        var bucket = BucketBox.Text?.Trim();
        var accessKeyId = AccessKeyIdBox.Text?.Trim();
        var accessKeySecret = AccessKeySecretBox.Text?.Trim();
        var enabled = OssEnabledCheckBox.IsChecked == true;
        if (enabled &&
            (string.IsNullOrWhiteSpace(endpoint) ||
             string.IsNullOrWhiteSpace(bucket) ||
             string.IsNullOrWhiteSpace(accessKeyId) ||
             string.IsNullOrWhiteSpace(accessKeySecret)))
        {
            StatusText.Text = OperationText.Choose(
                _language,
                "Endpoint, Bucket, AccessKey ID and AccessKey Secret are required.",
                "Endpoint、Bucket、AccessKey ID 和 AccessKey Secret 都必须填写。",
                "Endpoint、Bucket、AccessKey ID 和 AccessKey Secret 都必須填寫。",
                "Endpoint、Bucket、AccessKey ID、AccessKey Secret は必須です。"
            );
            return;
        }

        _config.OssEndpoint = endpoint;
        _config.OssBucket = bucket;
        _config.OssPrefix = PrefixBox.Text?.Trim();
        _config.OssAccessKeyId = accessKeyId;
        _config.OssAccessKeySecret = accessKeySecret;
        _config.OssEnabled = enabled;
        _config.Save();
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
