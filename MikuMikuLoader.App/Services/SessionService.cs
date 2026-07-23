using MikuMikuLoader.App.Models;

namespace MikuMikuLoader.App.Services;

public class SessionService
{
    private readonly ApiClient _api;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

    public SessionService(ApiClient api, SettingsService settingsService, AppSettings settings)
    {
        _api = api;
        _settingsService = settingsService;
        _settings = settings;
        _api.SetToken(settings.AuthToken);
    }

    public Account? Account { get; private set; }

    public bool IsLoggedIn => Account is not null;
    public bool IsDeveloper => Account?.Role is "Developer" or "Owner";
    public bool IsOwner => Account?.Role == "Owner";

    /// <summary>Owner, or any account granted a staff permission.</summary>
    public bool IsStaff => IsOwner || (Account?.IsStaff ?? false);

    public event Action? Changed;

    public async Task RestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.AuthToken)) return;
        try
        {
            var me = await _api.GetMeAsync();
            if (me is not null) SetSignedIn(me, _settings.AuthToken!);
            else ClearToken();
        }
        catch
        {

        }
    }

    public void SignIn(AuthResponse auth) => SetSignedIn(auth.Account, auth.Token);

    public void UpdateAccount(Account account)
    {
        Account = account;
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        await _api.LogoutAsync();
        ClearToken();
        _api.SetToken(null);
        Account = null;
        Changed?.Invoke();
    }

    private void SetSignedIn(Account account, string token)
    {
        Account = account;
        _api.SetToken(token);
        _settings.AuthToken = token;
        _settingsService.Save(_settings);
        Changed?.Invoke();
    }

    private void ClearToken()
    {
        _settings.AuthToken = null;
        _settingsService.Save(_settings);
    }
}
