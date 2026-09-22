using CommunityToolkit.Mvvm.ComponentModel;

namespace TsuruLauncher.Models
{
    public enum AccountType
    {
        Offline,
        Microsoft,

        /// <summary>外置登录（Yggdrasil / authlib-injector，例如 LittleSkin）。</summary>
        Yggdrasil
    }

    public partial class Account : ObservableObject
    {
        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _uuid = string.Empty;

        [ObservableProperty]
        private string _accessToken = string.Empty;
        
        // Microsoft Auth specific
        [ObservableProperty]
        private string _refreshToken = string.Empty;

        [ObservableProperty]
        private string _minecraftAccessToken = string.Empty;

        [ObservableProperty]
        private long _expiryTime = 0;
        
        [ObservableProperty]
        private AccountType _type = AccountType.Offline;

        /// <summary>外置登录服务器地址，例如 https://littleskin.cn/api/yggdrasil</summary>
        [ObservableProperty]
        private string _yggdrasilServer = string.Empty;

        /// <summary>Yggdrasil clientToken，刷新令牌时使用。</summary>
        [ObservableProperty]
        private string _clientToken = string.Empty;

        /// <summary>
        /// 是不是「当前正在使用的账号」——账号下拉浮层用它标出「当前」那一行。
        /// 由 <c>MainViewModel.RefreshCurrentAccountFlags</c> 统一维护（登录 / 切换 / 删除后刷新），
        /// 模型本身不做任何 IO，只是给绑定一个可观察的布尔位。
        /// </summary>
        [ObservableProperty]
        private bool _isCurrent;
    }
}