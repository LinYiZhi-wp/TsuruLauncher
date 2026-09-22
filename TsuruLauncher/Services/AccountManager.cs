using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TsuruLauncher.Models;

namespace TsuruLauncher.Services
{
    public partial class AccountManager : ObservableObject
    {
        private readonly AuthenticationService _authService;

        [ObservableProperty]
        private Account? _currentAccount;

        public ObservableCollection<Account> Accounts { get; } = new ObservableCollection<Account>();

        public event Action? AccountsChanged;

        public AccountManager()
        {
            _authService = new AuthenticationService();
        }

        public void AddAccount(Account account)
        {
            var existing = Accounts.FirstOrDefault(a => a.Uuid == account.Uuid);
            if (existing != null)
            {
                Accounts.Remove(existing);
            }
            Accounts.Add(account);
            CurrentAccount = account;
            AccountsChanged?.Invoke();
        }

        public void LoginOffline(string username)
        {
            var account = _authService.LoginOffline(username);
            AddAccount(account);
        }

        public async System.Threading.Tasks.Task LoginMicrosoft()
        {
            var account = await _authService.LoginMicrosoftAsync();
            AddAccount(account);
        }

        /// <summary>外置登录（Yggdrasil / authlib-injector，例如 LittleSkin）。</summary>
        public async System.Threading.Tasks.Task LoginYggdrasil(string server, string username, string password)
        {
            var account = await new YggdrasilAuthService().LoginAsync(server, username, password);
            AddAccount(account);
        }

        public Account? ActiveAccount => CurrentAccount;

        public void Logout()
        {
            if (CurrentAccount != null)
            {
                RemoveAccount(CurrentAccount);
            }
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void RemoveAccount(Account account)
        {
             if (Accounts.Contains(account))
            {
                Accounts.Remove(account);
                if (CurrentAccount == account)
                {
                    CurrentAccount = Accounts.FirstOrDefault();
                }
                AccountsChanged?.Invoke();
            }
        }
    }
}