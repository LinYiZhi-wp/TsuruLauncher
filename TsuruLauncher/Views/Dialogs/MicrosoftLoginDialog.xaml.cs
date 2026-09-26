using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TsuruLauncher.Controls;

namespace TsuruLauncher.Views.Dialogs
{
    public partial class MicrosoftLoginDialog : Window
    {
        private readonly string _loginUrl;
        private readonly string _redirectUri;

        public string? AuthorizationCode { get; private set; }

        public MicrosoftLoginDialog(string loginUrl, string redirectUri)
        {
            InitializeComponent();
            _loginUrl = loginUrl;
            _redirectUri = redirectUri;
            
            InitializeWebView();
        }

        // ⚠ async void 抛异常会崩应用 —— 登录初始化失败要给用户提示，不能静默崩
        private async void InitializeWebView()
        {
            try
            {
                await LoginWebView.EnsureCoreWebView2Async();

                LoginWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                LoginWebView.Source = new Uri(_loginUrl);
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "初始化登录 WebView");
                Controls.iOS26Dialog.Show(
                    "登录窗口初始化失败，可能是缺少 WebView2 运行时。\n\n" + ex.Message,
                    "登录", Controls.DialogIcon.Error);
                Close();
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Check if we are redirecting to our callback URI
            if (e.Uri.StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase))
            {
                // Parse the URL to get the code
                try 
                {
                    var uri = new Uri(e.Uri);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    string? code = query["code"];

                    if (!string.IsNullOrEmpty(code))
                    {
                        AuthorizationCode = code;
                        this.DialogResult = true;
                        this.Close();
                    }
                    else if (!string.IsNullOrEmpty(query["error"])) 
                    {
                        // Handle error or cancellation
                        iOS26Dialog.Show($"登录错误: {query["error_description"] ?? query["error"]}", "登录失败", DialogIcon.Error);
                        this.DialogResult = false;
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    iOS26Dialog.Show($"解析重定向时出错: {ex.Message}", "内部错误", DialogIcon.Error);
                    this.DialogResult = false;
                    this.Close();
                }
            }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}